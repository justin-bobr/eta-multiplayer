using Godot;

public struct HitInfo
{
	public bool Hit;
	public Vector3 Origin;
	public Vector3 Direction;
	public Vector3 Position;
	public Vector3 Normal;
	public Node3D Collider;
	public float Distance;
	public StringName Material;
}

/// <summary>
/// Pure hitscan logic. Server-replayable: identical input + world-state yields identical HitInfo.
/// Client runs it for visual impacts; server for damage authority and lag compensation.
/// </summary>
public static class Hitscan
{
	/// <summary>Manual ray-vs-shape cast against (hitbox, world-transform, shape) tuples, using the rewound
	/// GlobalTransforms from the bone history buffer. Bypasses the physics broadphase, whose deferred-updated
	/// positions are stale for lag-comp. Returns the closest hit nearer than <paramref name="maxDist"/>.</summary>
	public static HitInfo CastVsBoneShapes(Vector3 origin, Vector3 direction,
		System.Collections.Generic.List<(Node3D hitbox, Transform3D worldXform, Shape3D shape)> targets,
		float maxDist)
	{
		var info = new HitInfo { Origin = origin, Direction = direction, Material = "flesh" };
		float bestT = maxDist;
		Node3D bestHb = null;
		Transform3D bestXform = default;

		foreach (var (hb, xform, shape) in targets)
		{
			if (shape == null)
				continue;
			float t = float.PositiveInfinity;
			bool hit = false;
			switch (shape)
			{
				case SphereShape3D sph:
					hit = RaySphere(origin, direction, xform.Origin, sph.Radius * AvgScale(xform.Basis), out t);
					break;
				case CapsuleShape3D cap:
					hit = RayCapsule(origin, direction, xform, cap.Radius, cap.Height, out t);
					break;
				case BoxShape3D box:
					hit = RayBox(origin, direction, xform, box.Size, out t);
					break;
			}
			if (hit && t > 0 && t < bestT)
			{
				bestT = t;
				bestHb = hb;
				bestXform = xform;
			}
		}

		if (bestHb == null)
			return info;
		info.Hit = true;
		info.Distance = bestT;
		info.Position = origin + direction * bestT;
		info.Normal = (info.Position - bestXform.Origin).Normalized();
		info.Collider = bestHb;
		return info;
	}

	private static float AvgScale(Basis b) => (b.X.Length() + b.Y.Length() + b.Z.Length()) / 3f;

	private static bool RaySphere(Vector3 origin, Vector3 dir, Vector3 center, float radius, out float t)
	{
		t = 0;
		Vector3 oc = origin - center;
		float b = oc.Dot(dir);
		float c = oc.Dot(oc) - radius * radius;
		float h = b * b - c;
		if (h < 0)
			return false;
		h = Mathf.Sqrt(h);
		float t0 = -b - h;
		float t1 = -b + h;
		t = (t0 >= 0) ? t0 : t1;
		return t >= 0;
	}

	/// <summary>Ray-capsule test. Capsule runs along local Y; evaluates cylinder + two end-spheres and
	/// returns the nearest hit.</summary>
	private static bool RayCapsule(Vector3 origin, Vector3 dir, Transform3D xform, float radius, float height, out float t)
	{
		float scale = AvgScale(xform.Basis);
		float worldRadius = radius * scale;
		float worldHalfCyl = Mathf.Max(0f, (height * 0.5f - radius)) * scale;
		Vector3 yAxis = xform.Basis.Y.Normalized();
		Vector3 a = xform.Origin + yAxis * worldHalfCyl;
		Vector3 b = xform.Origin - yAxis * worldHalfCyl;

		float bestT = float.PositiveInfinity;
		bool any = false;
		if (RaySphere(origin, dir, a, worldRadius, out float ta))
		{ bestT = Mathf.Min(bestT, ta); any = true; }
		if (RaySphere(origin, dir, b, worldRadius, out float tb))
		{ bestT = Mathf.Min(bestT, tb); any = true; }

		if (worldHalfCyl > 0.0001f)
		{
			Vector3 ba = b - a;
			Vector3 oa = origin - a;
			float baba = ba.Dot(ba);
			float bard = ba.Dot(dir);
			float baoa = ba.Dot(oa);
			float rdoa = dir.Dot(oa);
			float oaoa = oa.Dot(oa);
			float A = baba - bard * bard;
			float B = baba * rdoa - baoa * bard;
			float C = baba * oaoa - baoa * baoa - worldRadius * worldRadius * baba;
			float h = B * B - A * C;
			if (h >= 0 && Mathf.Abs(A) > 0.0001f)
			{
				float tc = (-B - Mathf.Sqrt(h)) / A;
				if (tc >= 0)
				{
					float y = baoa + tc * bard;
					if (y >= 0 && y <= baba)
					{
						if (tc < bestT)
						{ bestT = tc; any = true; }
					}
				}
			}
		}

		t = any ? bestT : 0f;
		return any;
	}

	/// <summary>Ray-OBB (oriented bounding box). Slab test in local space (ray transformed into box-local frame).</summary>
	private static bool RayBox(Vector3 origin, Vector3 dir, Transform3D xform, Vector3 size, out float t)
	{
		Transform3D inv = xform.AffineInverse();
		Vector3 lo = inv * origin;
		Vector3 ld = inv.Basis * dir;
		Vector3 half = size * 0.5f;

		float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
		for (int axis = 0; axis < 3; axis++)
		{
			float d = ld[axis], o = lo[axis], h = half[axis];
			if (Mathf.Abs(d) < 1e-6f)
			{
				if (o < -h || o > h)
				{ t = 0; return false; }
				continue;
			}
			float t1 = (-h - o) / d;
			float t2 = (h - o) / d;
			if (t1 > t2)
				(t1, t2) = (t2, t1);
			tmin = Mathf.Max(tmin, t1);
			tmax = Mathf.Min(tmax, t2);
			if (tmin > tmax)
			{ t = 0; return false; }
		}
		t = tmin >= 0 ? tmin : tmax;
		return t >= 0;
	}


	private static PhysicsRayQueryParameters3D _sharedQuery;
	private static readonly PhysicsRayQueryResult3D _sharedResult = new();
	private static readonly Godot.Collections.Array<Rid> _emptyExcludes = new();
	private static readonly Godot.Collections.Array<Rid> _singleExcludeScratch = new();

	private static PhysicsRayQueryParameters3D EnsureSharedQuery()
	{
		if (_sharedQuery != null)
			return _sharedQuery;
		_sharedQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Right);
		_sharedQuery.CollideWithAreas = false;
		_sharedQuery.CollideWithBodies = true;
		return _sharedQuery;
	}

	/// <summary>Casts a ray for the given range (m). Optional single-RID exclude (e.g. the shooter).</summary>
	public static HitInfo Cast(PhysicsDirectSpaceState3D space, Vector3 origin, Vector3 direction, float range, Rid? exclude = null, uint mask = 1)
	{
		var query = EnsureSharedQuery();
		query.From = origin;
		query.To = origin + direction * range;
		query.CollisionMask = mask;
		if (exclude.HasValue)
		{
			_singleExcludeScratch.Clear();
			_singleExcludeScratch.Add(exclude.Value);
			query.Exclude = _singleExcludeScratch;
		}
		else
		{
			query.Exclude = _emptyExcludes;
		}
		return CastCore(space, query, origin, direction);
	}

	/// <summary>Like <see cref="Cast"/> but with a multi-RID exclude, so the shooter doesn't hit their own
	/// hitboxes (NetworkPlayer RID + all <see cref="HitboxRig.Rids"/>).</summary>
	public static HitInfo CastMulti(PhysicsDirectSpaceState3D space, Vector3 origin, Vector3 direction, float range, Godot.Collections.Array<Rid> excludes, uint mask = 1)
	{
		var query = EnsureSharedQuery();
		query.From = origin;
		query.To = origin + direction * range;
		query.CollisionMask = mask;
		query.Exclude = excludes ?? _emptyExcludes;
		return CastCore(space, query, origin, direction);
	}

	/// <summary>Shared body for <see cref="Cast"/>/<see cref="CastMulti"/>: runs the IntersectRay and copies
	/// the result into a <see cref="HitInfo"/>, including per-face and per-group material lookup.</summary>
	private static HitInfo CastCore(PhysicsDirectSpaceState3D space, PhysicsRayQueryParameters3D query, Vector3 origin, Vector3 direction)
	{
		var info = new HitInfo
		{
			Origin = origin,
			Direction = direction,
			Material = "default",
		};
		if (!space.IntersectRayInto(query, _sharedResult))
			return info;
		info.Hit = true;
		info.Position = _sharedResult.GetPosition();
		info.Normal = _sharedResult.GetNormal();
		info.Collider = _sharedResult.GetCollider() as Node3D;
		info.Distance = origin.DistanceTo(info.Position);
		int faceIndex = _sharedResult.GetFaceIndex();
		info.Material = DetectMaterialPerFace(info.Collider, faceIndex);
		if (info.Material == "default")
			info.Material = DetectMaterialPerGroup(info.Collider);
		return info;
	}

	/// <summary>Per-face material lookup: maps face_index to a surface, reads its material's "impact_tag"
	/// metadata. Works only for trimesh colliders. Triangle counts are memoized per ArrayMesh
	/// (<see cref="_triCountCache"/>) since SurfaceGetArrays allocates per call.</summary>
	private static StringName DetectMaterialPerFace(Node3D collider, int faceIndex)
	{
		if (collider == null || faceIndex < 0)
			return "default";

		var meshInst = FindVisualMesh(collider);
		if (meshInst?.Mesh is not ArrayMesh mesh)
			return "default";

		int[] triCounts = GetTriCounts(mesh);
		int cumFaces = 0;
		int surfaceIdx = -1;
		for (int s = 0; s < triCounts.Length; s++)
		{
			int triCount = triCounts[s];
			if (faceIndex < cumFaces + triCount)
			{
				surfaceIdx = s;
				break;
			}
			cumFaces += triCount;
		}
		if (surfaceIdx < 0)
			return "default";

		var material = meshInst.GetActiveMaterial(surfaceIdx);
		if (material == null)
			return "default";
		if (material.HasMeta("impact_tag"))
			return (StringName)material.GetMeta("impact_tag").AsString();
		return "default";
	}

	private static readonly System.Collections.Generic.Dictionary<ArrayMesh, int[]> _triCountCache = new();

	/// <summary>Returns the cached triangle counts per surface for the given mesh, populating the cache on first use.</summary>
	private static int[] GetTriCounts(ArrayMesh mesh)
	{
		if (_triCountCache.TryGetValue(mesh, out var cached))
			return cached;
		int surfaceCount = mesh.GetSurfaceCount();
		var counts = new int[surfaceCount];
		for (int s = 0; s < surfaceCount; s++)
		{
			var arrays = mesh.SurfaceGetArrays(s);
			counts[s] = TriangleCount(arrays);
		}
		_triCountCache[mesh] = counts;
		return counts;
	}

	/// <summary>Returns the triangle count of a surface-array (indexed if present, otherwise vertex-based).</summary>
	private static int TriangleCount(Godot.Collections.Array arrays)
	{
		var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
		if (indices.Length > 0)
			return indices.Length / 3;
		var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		return verts.Length / 3;
	}

	private static readonly System.Collections.Generic.Dictionary<Node3D, MeshInstance3D> _visualMeshCache = new();

	/// <summary>Looks for a MeshInstance3D as a sibling or child of the collider. Best-effort.
	/// Result is cached per collider to avoid repeated GetParent+GetChildren allocations.</summary>
	private static MeshInstance3D FindVisualMesh(Node3D collider)
	{
		if (_visualMeshCache.TryGetValue(collider, out var cached))
		{
			if (GodotObject.IsInstanceValid(cached))
				return cached;
			_visualMeshCache.Remove(collider);
		}
		MeshInstance3D found = null;
		var parent = collider.GetParent();
		if (parent != null)
		{
			foreach (var child in parent.GetChildren())
				if (child is MeshInstance3D mi)
				{ found = mi; break; }
		}
		if (found == null)
		{
			foreach (var child in collider.GetChildren())
				if (child is MeshInstance3D mi2)
				{ found = mi2; break; }
		}
		if (found != null)
			_visualMeshCache[collider] = found;
		return found;
	}

	/// <summary>Recognized surface material groups. Names match the audio/footsteps/ folders (1:1) plus
	/// "flesh". To add a ground type, add it here and create an identically named folder.</summary>
	private static readonly System.Collections.Generic.HashSet<string> MaterialGroups = new()
	{
		"flesh",
		"concrete", "concrete_2", "metal", "metal_2", "wood", "wood_2", "glass",
		"gravel", "gravel_2", "dirt", "dirt_2", "sand", "wet_sand", "mud",
		"grass", "grass_2", "high_grass", "ice", "snow",
		"carpet_hard", "carpet_wood", "deep_water", "shallow_water_wet_surface",
		"undergrowth_leaves", "broken_glass_glass_shards",
		"glass_shards_concrete", "glass_shards_concrete_2", "glass_shards_metal",
		"glass_shards_metal_2", "glass_shards_wood", "glass_shards_wood_2",
	};

	private static readonly System.Collections.Generic.Dictionary<Node3D, StringName> _groupMaterialCache = new();
	private static readonly StringName _defaultMaterial = "default";

	/// <summary>Per-collider group fallback - the first recognized material group wins. Cached per collider
	/// since group membership is static for the lifetime of the node.</summary>
	private static StringName DetectMaterialPerGroup(Node3D collider)
	{
		if (collider == null)
			return _defaultMaterial;
		if (_groupMaterialCache.TryGetValue(collider, out var cached))
		{
			if (GodotObject.IsInstanceValid(collider))
				return cached;
			_groupMaterialCache.Remove(collider);
		}
		StringName resolved = _defaultMaterial;
		foreach (StringName group in collider.GetGroups())
		{
			if (MaterialGroups.Contains(group.ToString()))
			{ resolved = group; break; }
		}
		_groupMaterialCache[collider] = resolved;
		return resolved;
	}
}
