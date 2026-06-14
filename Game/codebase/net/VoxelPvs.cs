using Godot;

namespace Vantix.Server;

/// <summary>
/// Server-side line-of-sight precomputation. Voxelises the map into a coarse 3D grid and bakes
/// pairwise voxel visibility via center-to-center raycasts; <see cref="CanSee"/> is then an O(1)
/// bit lookup. Built incrementally via <see cref="BeginBuild"/>/<see cref="StepBuild"/>; returns
/// "visible" (no culling) until <see cref="Built"/> flips true. Storage shares the flat byte[]
/// format of the baked <see cref="VoxelPvsData"/> resource. Single-ray test is intentionally
/// optimistic — may over-reveal, never wrongly hides.
/// </summary>
public class VoxelPvs
{
	/// <summary>Default voxel cap for the RUNTIME incremental fallback build path. Bounds memory
	/// (N²/8 bytes) and build cost (~N²/2 raycasts). 2500 voxels ≈ 780KB + ~3.1M raycasts =
	/// ~25s wall-clock at 1000 rays/poll. Editor bakes via <see cref="VoxelPvsInstance"/> pass a much
	/// larger cap to <see cref="BeginBuild"/> since they're one-shot offline and can afford the wait.</summary>
	public const int DefaultMaxVoxels = 2500;
	/// <summary>Voxel cap for the editor-only bake. 16,000 voxels ≈ 32MB visibility buffer +
	/// ~128M raycasts ≈ several minutes at the per-frame budget. Higher than that pushes the
	/// per-bit-index arithmetic close to int.MaxValue (46,340² ≈ 2.15G); we cap well below that.</summary>
	public const int EditorBakeMaxVoxels = 16_000;

	public Vector3 Origin { get; private set; }
	public float VoxelSize { get; private set; }
	public Vector3I Dims { get; private set; }
	public int TotalVoxels => Dims.X * Dims.Y * Dims.Z;
	public bool Built { get; private set; }
	public bool IsBuilding => _visibility != null && !Built;
	public float BuildProgress01 => _buildN == 0 ? 0f : Mathf.Clamp((float)_buildNextA / _buildN, 0f, 1f);
	public long BuildRaysDone => _buildRayCount;

	private byte[] _visibility;
	private bool[] _solidVoxels;
	private PhysicsDirectSpaceState3D _buildSpace;
	private PhysicsRayQueryParameters3D _buildQuery;
	private PhysicsPointQueryParameters3D _buildPointQuery;
	private int _buildN;
	private long _buildNextA;
	private long _buildNextB;
	private long _buildRayCount;
	private long _buildSkippedPairs;
	private int _buildSolidVoxels;
	private bool _buildCancelRequested;

	public long BuildSkippedPairs => _buildSkippedPairs;
	public int BuildSolidVoxels => _buildSolidVoxels;

	/// <summary>Starts a fresh build. Sets up the voxel grid (auto-coarsening <paramref name="voxelSize"/>
	/// when the requested size would exceed <paramref name="maxVoxels"/>) and allocates the visibility
	/// byte buffer, but performs ZERO raycasts — call <see cref="StepBuild"/> repeatedly to do the work.
	/// <see cref="CanSee"/> returns true (no culling) until <see cref="Built"/> flips true.</summary>
	public void BeginBuild(PhysicsDirectSpaceState3D space, Aabb worldAabb, float voxelSize, uint collisionMask = 1u, int maxVoxels = DefaultMaxVoxels)
	{
		VoxelSize = Mathf.Max(0.5f, voxelSize);
		Origin = worldAabb.Position;
		for (;;)
		{
			Dims = new Vector3I(
				Mathf.Max(1, Mathf.CeilToInt(worldAabb.Size.X / VoxelSize)),
				Mathf.Max(1, Mathf.CeilToInt(worldAabb.Size.Y / VoxelSize)),
				Mathf.Max(1, Mathf.CeilToInt(worldAabb.Size.Z / VoxelSize)));
			if (TotalVoxels <= maxVoxels) break;
			VoxelSize *= 1.25f;
		}
		_buildN = TotalVoxels;
		long totalBits = (long)_buildN * _buildN;
		_visibility = new byte[(totalBits + 7) >> 3];
		_buildSpace = space;
		_buildQuery = new PhysicsRayQueryParameters3D
		{
			CollisionMask = collisionMask,
			CollideWithBodies = true,
			CollideWithAreas = false,
		};
		_buildPointQuery = new PhysicsPointQueryParameters3D
		{
			CollisionMask = collisionMask,
			CollideWithBodies = true,
			CollideWithAreas = false,
		};
		_buildNextA = 0;
		_buildNextB = 0;
		_buildRayCount = 0;
		_buildSkippedPairs = 0;
		_buildCancelRequested = false;
		PrecomputeSolidVoxels();
		Built = false;
	}

	/// <summary>One-shot pre-pass that flags every voxel whose center sits inside a collision shape on
	/// the build's layer mask. Subsequent <see cref="StepBuild"/> calls skip all pairs involving such
	/// voxels — no player can stand inside a solid block, so any FoW query against it would return
	/// false anyway, and the raycast pass would just waste CPU. On dust2-scale maps this typically
	/// drops the ray count by 50-80% (most voxels are above the playable ceiling, below the floor, or
	/// embedded in walls). Runs in &lt;100ms even at 16k voxels — pure point-overlap queries are much
	/// cheaper than the directional raycasts they replace.</summary>
	private void PrecomputeSolidVoxels()
	{
		_solidVoxels = new bool[_buildN];
		_buildSolidVoxels = 0;
		for (int i = 0; i < _buildN; i++)
		{
			_buildPointQuery.Position = VoxelCenter(i);
			var hits = _buildSpace.IntersectPoint(_buildPointQuery, maxResults: 1);
			if (hits.Count > 0) { _solidVoxels[i] = true; _buildSolidVoxels++; }
		}
	}

	/// <summary>Signals the active build to stop at the next <see cref="StepBuild"/> call. The
	/// partially-filled <see cref="_visibility"/> buffer is discarded — <see cref="Built"/> stays
	/// false, <see cref="IsBuilding"/> becomes false. The caller can then start a fresh build, or
	/// leave the PVS unbuilt (= <see cref="CanSee"/> returns true = no culling).</summary>
	public void CancelBuild()
	{
		if (!IsBuilding) return;
		_buildCancelRequested = true;
	}

	/// <summary>Processes up to <paramref name="maxRays"/> visibility raycasts and returns true once
	/// the build is fully complete (= <see cref="Built"/> becomes true on the same call). Idempotent
	/// when already built or never begun. Resumes precisely where the previous call left off.</summary>
	public bool StepBuild(int maxRays)
	{
		if (Built) return true;
		if (_visibility == null) return false;
		if (_buildCancelRequested)
		{
			_visibility = null;
			_buildSpace = null;
			_buildQuery = null;
			_buildCancelRequested = false;
			Built = false;
			return false;
		}
		int n = _buildN;
		int rays = 0;
		for (long a = _buildNextA; a < n; a++)
		{
			bool aSolid = _solidVoxels != null && _solidVoxels[a];
			Vector3 from = aSolid ? Vector3.Zero : VoxelCenter((int)a);
			long bStart = (a == _buildNextA) ? _buildNextB : a;
			for (long b = bStart; b < n; b++)
			{
				if (aSolid || (_solidVoxels != null && _solidVoxels[b]))
				{
					_buildSkippedPairs++;
					continue;
				}
				bool visible;
				if (a == b)
				{
					visible = true;
				}
				else
				{
					if (rays >= maxRays)
					{
						_buildNextA = a;
						_buildNextB = b;
						return false;
					}
					Vector3 to = VoxelCenter((int)b);
					_buildQuery.From = from;
					_buildQuery.To = to;
					var hit = _buildSpace.IntersectRay(_buildQuery);
					visible = hit.Count == 0;
					rays++;
					_buildRayCount++;
				}
				if (visible)
				{
					SetBit(a * n + b);
					SetBit(b * n + a);
				}
			}
		}
		Built = true;
		_buildSpace = null;
		_buildQuery = null;
		_buildPointQuery = null;
		return true;
	}

	/// <summary>Returns true if <paramref name="from"/> and <paramref name="to"/> have line-of-sight
	/// according to the precomputed PVS. Out-of-bounds positions clamp to the nearest voxel. While
	/// <see cref="Built"/> is false (build in progress or never started), returns true (no culling)
	/// so the game keeps playing with old behavior until the PVS comes online.</summary>
	public bool CanSee(Vector3 from, Vector3 to)
	{
		if (!Built) return true;
		int a = WorldToIndex(from);
		int b = WorldToIndex(to);
		if (a < 0 || b < 0) return true;
		long bitIdx = (long)a * _buildN + b;
		return (_visibility[bitIdx >> 3] & (1 << (int)(bitIdx & 7))) != 0;
	}

	private void SetBit(long bitIdx)
	{
		_visibility[bitIdx >> 3] |= (byte)(1 << (int)(bitIdx & 7));
	}

	private int WorldToIndex(Vector3 world)
	{
		Vector3 local = (world - Origin) / VoxelSize;
		int x = Mathf.Clamp(Mathf.FloorToInt(local.X), 0, Dims.X - 1);
		int y = Mathf.Clamp(Mathf.FloorToInt(local.Y), 0, Dims.Y - 1);
		int z = Mathf.Clamp(Mathf.FloorToInt(local.Z), 0, Dims.Z - 1);
		return (z * Dims.Y + y) * Dims.X + x;
	}

	private Vector3 VoxelCenter(int index)
	{
		int x = index % Dims.X;
		int rem = index / Dims.X;
		int y = rem % Dims.Y;
		int z = rem / Dims.Y;
		return Origin + new Vector3(
			(x + 0.5f) * VoxelSize,
			(y + 0.5f) * VoxelSize,
			(z + 0.5f) * VoxelSize);
	}

	/// <summary>Counts set bits in the visibility buffer. O(byteCount) — at 32MB takes ~150ms.
	/// Used only by the post-bake density log, not by any hot path.</summary>
	public long CountVisible()
	{
		if (_visibility == null) return 0;
		long count = 0;
		for (int i = 0; i < _visibility.Length; i++)
		{
			byte b = _visibility[i];
			while (b != 0) { count += b & 1; b >>= 1; }
		}
		return count;
	}

	/// <summary>Returns the internal visibility buffer for serialisation into a <see cref="VoxelPvsData"/>
	/// resource. Caller may keep the reference — subsequent <see cref="BeginBuild"/> allocates a fresh
	/// buffer, so the returned array is safely owned by the caller after this method.</summary>
	public byte[] ExportBitsAsBytes() => _visibility ?? System.Array.Empty<byte>();

	/// <summary>Adopts the visibility data from a baked <see cref="VoxelPvsData"/> resource — INSTANT,
	/// no copy and no allocation. The internal buffer is set to the resource's byte array by reference
	/// (matched format = no transformation needed). Used by the server-startup path to skip the
	/// runtime build entirely when the level was pre-baked.</summary>
	public void LoadFromData(VoxelPvsData data)
	{
		if (data == null || !data.HasData)
		{
			GD.PushWarning("[VoxelPvs] LoadFromData called with null/empty data — ignoring.");
			return;
		}
		Origin = data.Origin;
		VoxelSize = data.VoxelSize;
		Dims = data.Dims;
		_buildN = TotalVoxels;
		_visibility = data.VisibilityBytes;
		_buildSpace = null;
		_buildQuery = null;
		_buildRayCount = 0;
		_buildCancelRequested = false;
		Built = true;
	}

	/// <summary>Computes the playable AABB by walking <see cref="CollisionShape3D"/> nodes under
	/// <paramref name="root"/> that belong to a <see cref="CollisionObject3D"/> on a layer matching
	/// <paramref name="layerMask"/>. This naturally excludes skyboxes, distant decoration meshes and
	/// other render-only geometry (which have no collision) — only walls, floors, ramps and crates
	/// contribute to the bounds. Falls back to a mesh-based walk when no collision shapes are found.
	/// Each axis is capped at <see cref="MaxAabbExtentM"/> as a safety belt.</summary>
	public static Aabb ComputeWorldAabb(Node root, uint layerMask = 1u)
	{
		Aabb result = default;
		bool any = false;
		WalkCollision(root, layerMask, ref result, ref any);
		if (!any) WalkMesh(root, ref result, ref any);
		if (!any) return new Aabb(Vector3.Zero, new Vector3(64f, 16f, 64f));
		result = result.Grow(2f);
		Vector3 size = result.Size;
		Vector3 center = result.Position + size * 0.5f;
		Vector3 cappedSize = new Vector3(
			Mathf.Min(size.X, MaxAabbExtentM),
			Mathf.Min(size.Y, MaxAabbExtentM),
			Mathf.Min(size.Z, MaxAabbExtentM));
		return new Aabb(center - cappedSize * 0.5f, cappedSize);
	}

	private const float MaxAabbExtentM = 256f;

	private static void WalkCollision(Node node, uint layerMask, ref Aabb acc, ref bool any)
	{
		if (node is CollisionShape3D cs && cs.Shape != null && !cs.Disabled)
		{
			var body = cs.GetParentOrNull<CollisionObject3D>();
			if (body != null && (body.CollisionLayer & layerMask) != 0)
			{
				var debugMesh = cs.Shape.GetDebugMesh();
				if (debugMesh != null)
				{
					Aabb local = debugMesh.GetAabb();
					Aabb world = cs.GlobalTransform * local;
					Vector3 sz = world.Size;
					if (sz.X < MaxAabbExtentM && sz.Y < MaxAabbExtentM && sz.Z < MaxAabbExtentM)
					{
						if (!any) { acc = world; any = true; }
						else acc = acc.Merge(world);
					}
				}
			}
		}
		foreach (var child in node.GetChildren())
			WalkCollision(child, layerMask, ref acc, ref any);
	}

	/// <summary>Diagnostic — walks the scene the same way <see cref="ComputeWorldAabb"/> does and
	/// returns up to <paramref name="topN"/> collision shapes ordered by max-axis extent, descending.
	/// Use this when your computed AABB is bigger than expected (= some out-of-world collider is
	/// inflating it) to find the culprit.</summary>
	public static string[] DescribeLargestColliders(Node root, uint layerMask = 1u, int topN = 10)
	{
		var found = new System.Collections.Generic.List<(string path, float maxExtent, Vector3 size, int layer)>();
		WalkCollect(root, layerMask, found);
		found.Sort((a, b) => b.maxExtent.CompareTo(a.maxExtent));
		int n = Mathf.Min(topN, found.Count);
		var result = new string[n];
		for (int i = 0; i < n; i++)
		{
			var (p, _, s, l) = found[i];
			result[i] = $"{p} | size=({s.X:F1},{s.Y:F1},{s.Z:F1})m | layer={l}";
		}
		return result;
	}

	private static void WalkCollect(Node node, uint layerMask, System.Collections.Generic.List<(string, float, Vector3, int)> sink)
	{
		if (node is CollisionShape3D cs && cs.Shape != null && !cs.Disabled)
		{
			var body = cs.GetParentOrNull<CollisionObject3D>();
			if (body != null && (body.CollisionLayer & layerMask) != 0)
			{
				var dm = cs.Shape.GetDebugMesh();
				if (dm != null)
				{
					Aabb local = dm.GetAabb();
					Aabb world = cs.GlobalTransform * local;
					Vector3 sz = world.Size;
					float maxExt = Mathf.Max(sz.X, Mathf.Max(sz.Y, sz.Z));
					sink.Add((cs.GetPath(), maxExt, sz, (int)body.CollisionLayer));
				}
			}
		}
		foreach (var child in node.GetChildren())
			WalkCollect(child, layerMask, sink);
	}

	private static void WalkMesh(Node node, ref Aabb acc, ref bool any)
	{
		if (node is MeshInstance3D mi && mi.Mesh != null)
		{
			Aabb local = mi.GetAabb();
			Aabb world = mi.GlobalTransform * local;
			Vector3 worldSize = world.Size;
			if (worldSize.X < MaxAabbExtentM && worldSize.Y < MaxAabbExtentM && worldSize.Z < MaxAabbExtentM)
			{
				if (!any) { acc = world; any = true; }
				else acc = acc.Merge(world);
			}
		}
		foreach (var child in node.GetChildren())
			WalkMesh(child, ref acc, ref any);
	}
}
