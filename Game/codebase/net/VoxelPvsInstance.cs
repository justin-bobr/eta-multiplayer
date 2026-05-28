using Godot;

/// <summary>
/// Place one of these as a child of any map scene to bake a server-side Fog-of-War visibility grid
/// for that map. Click "Bake PVS" in the inspector to run the offline raycast pass and write the
/// result to a <see cref="VoxelPvsData"/> .tres resource next to the map's .tscn. At runtime,
/// <see cref="NetServer"/> looks for this node in the loaded scene and (if a baked Data resource
/// is present) loads it directly — FoW is active from tick 1 with zero build cost.
///
/// Modelled after Godot's own <c>VoxelGI</c> / <c>LightmapGI</c> bake nodes: the workflow is "add
/// node → set parameters → click Bake → save scene", and the baked data lives in source control
/// next to the level.
/// </summary>
[Tool]
[GlobalClass]
public partial class VoxelPvsInstance : Node3D
{
	/// <summary>The baked PVS data this node provides at runtime. Set automatically by <see cref="BakeNow"/>;
	/// can also be assigned manually in the inspector if the .tres lives somewhere unusual. When null
	/// or empty, <see cref="NetServer"/> falls back to its incremental runtime build (or no FoW at all
	/// when <c>sv_fog_of_war</c> is off).</summary>
	[Export] public VoxelPvsData Data;

	/// <summary>Edge length of one cubic voxel cell, in metres. Smaller = finer occlusion at quadratic
	/// memory + bake cost. The actual size used may be auto-coarsened during bake to stay within
	/// <c>VoxelPvs.MaxVoxels</c> for huge maps.</summary>
	[Export(PropertyHint.Range, "0.5,20,0.5")] public float BakeVoxelSize = 4.0f;

	/// <summary>Collision layer mask that counts as occluders during the raycast pass. Default 1 =
	/// the world geometry layer.</summary>
	[Export(PropertyHint.Layers3DPhysics)] public uint OccluderCollisionMask = 1u;

	/// <summary>When true, the bake uses <see cref="OverrideAabbOrigin"/> + <see cref="OverrideAabbSize"/>
	/// as the voxel grid extents instead of auto-deriving from scene meshes. Strongly recommended on
	/// real maps so a skybox/decoration mesh does not inflate the AABB and force the voxel size to
	/// auto-coarsen into uselessness (e.g. dust2 with auto-AABB ended up at 9.8m voxels — much larger
	/// than the thin walls that are supposed to occlude).</summary>
	[Export] public bool UseOverrideAabb = false;
	/// <summary>World-space min corner of the override AABB (= playable area lower-back-left). Only
	/// used when <see cref="UseOverrideAabb"/> is true. The viewport gizmo previews this so you can
	/// visually align the box to the playable bounds before clicking Bake.</summary>
	[Export] public Vector3 OverrideAabbOrigin = new(-32f, -2f, -32f);
	/// <summary>World-space size of the override AABB (= playable area extent in metres). Pick the
	/// tightest box that contains all standable positions; anything outside clamps to the nearest
	/// voxel at query time. Default 64×16×64 matches a dust2-scale map; tune per level.</summary>
	[Export] public Vector3 OverrideAabbSize = new(64f, 16f, 64f);

	/// <summary>Editor-only viewport gizmo toggle: draws the bake AABB (yellow wireframe) and
	/// optionally the voxel grid (blue lines) so the user can confirm visually that the AABB covers
	/// the playable area and the voxel resolution is appropriate. No runtime cost.</summary>
	[Export] public bool ShowGizmo = true;
	/// <summary>Draws every voxel cell's edges as lines on top of the AABB box. Useful for tuning
	/// <see cref="BakeVoxelSize"/> but visually noisy at high voxel counts (&gt; ~1000).</summary>
	[Export] public bool ShowVoxelGrid = false;
	/// <summary>Post-bake heatmap: draws a small colored cube at each playable voxel, colored by how
	/// many other voxels it can see. Red = mostly enclosed (interior, FoW filters a lot), green =
	/// mostly open (FoW barely filters). Solid voxels (centers inside walls/floor/ceiling) are
	/// omitted — those cells have no player presence. Requires <see cref="HasBakedData"/>.</summary>
	[Export] public bool ShowDensityHeatmap = false;

	/// <summary>Inspector button — kicks off an asynchronous bake. The actual raycast pass runs
	/// incrementally in <see cref="_Process"/> (~8000 rays per editor frame ≈ ~16ms hitch) so the
	/// editor stays responsive. Progress shows in <see cref="BakeStatus"/> (visible in the inspector)
	/// and the Output panel logs every 5%. Output is written to <see cref="Data"/> and saved to disk
	/// at <see cref="BakeSavePath"/> when complete.</summary>
	[ExportToolButton("Bake PVS")]
	public Callable BakePvsButton => Callable.From(BakeNow);

	/// <summary>Inspector button — aborts an in-flight bake. The partially-filled visibility buffer
	/// is discarded; <see cref="Data"/> is NOT modified (so the previous baked resource stays valid).
	/// No-op when no bake is running.</summary>
	[ExportToolButton("Cancel Bake")]
	public Callable CancelBakeButton => Callable.From(CancelBakeNow);

	/// <summary>Editor-only: signals the active bake to stop at the next frame. Safe to call when
	/// no bake is in progress (no-op). Reports the abort in the Output panel and resets
	/// <see cref="BakeStatus"/> back to "Idle.".</summary>
	public void CancelBakeNow()
	{
		if (!Engine.IsEditorHint()) return;
		if (_activeBakeBuilder == null) return;
		float pct = _activeBakeBuilder.BuildProgress01 * 100f;
		GD.Print($"[VoxelPvsInstance] Bake cancelled at {pct:F0}% ({_activeBakeBuilder.BuildRaysDone:N0} rays done) — Data unchanged.");
		_activeBakeBuilder.CancelBuild();
		_activeBakeBuilder = null;
		_bakeLastLoggedPct = -1f;
		BakeStatus = "Idle. (last bake cancelled)";
		NotifyPropertyListChanged();
	}

	/// <summary>Live bake status string — shows "Idle." when nothing is running, "Baking: X%" while
	/// the incremental bake progresses (updated every editor frame), and "Bake complete: ..." when
	/// finished. Inspector auto-refreshes; the Output panel also logs every ~5% increment.</summary>
	[Export(PropertyHint.MultilineText)] public string BakeStatus = "Idle.";

	/// <summary>Raycast budget per editor frame while the bake runs. Higher = faster bake but choppier
	/// editor. 8000 ≈ ~16ms hitch per frame at observed ~500k rays/sec (responsive, multi-min bake).
	/// 30000 ≈ ~60ms hitch (laggy but ~3-4× faster). For one-shot bakes that you walk away from,
	/// crank to 100000 — editor effectively unusable but bake finishes much faster.</summary>
	[Export(PropertyHint.Range, "500,200000,500")] public int BakeRaysPerFrame = 8_000;

	private VoxelPvs _activeBakeBuilder;
	private ulong _bakeStartUsec;
	private float _bakeLastLoggedPct = -1f;
	private string _bakeAabbSource = "";

	/// <summary>Editor-only: kicks off an asynchronous bake. Returns immediately after setting up the
	/// builder; subsequent _Process calls drive the raycast loop. Safe to call mid-bake — the previous
	/// bake's state is discarded and a fresh one starts.</summary>
	public void BakeNow()
	{
		if (!Engine.IsEditorHint())
		{
			GD.PushWarning("[VoxelPvsInstance] BakeNow is editor-only.");
			return;
		}
		var sceneRoot = GetTree()?.EditedSceneRoot;
		if (sceneRoot == null)
		{
			GD.PushError("[VoxelPvsInstance] No EditedSceneRoot — bake aborted.");
			return;
		}
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			GD.PushError("[VoxelPvsInstance] No DirectSpaceState available — bake aborted.");
			return;
		}

		Aabb aabb = UseOverrideAabb
			? new Aabb(OverrideAabbOrigin, OverrideAabbSize)
			: VoxelPvs.ComputeWorldAabb(sceneRoot, OccluderCollisionMask);
		_bakeAabbSource = UseOverrideAabb ? "user-override" : "auto-derived from collision shapes on layer mask";

		if (!UseOverrideAabb)
		{
			var bigOnes = VoxelPvs.DescribeLargestColliders(sceneRoot, OccluderCollisionMask, topN: 8);
			if (bigOnes.Length > 0)
			{
				GD.Print($"[VoxelPvsInstance] Top {bigOnes.Length} colliders by max-axis extent (if any look out-of-bounds, the AABB is inflated by them — exclude them from layer {OccluderCollisionMask} or use UseOverrideAabb):");
				foreach (var line in bigOnes) GD.Print($"  {line}");
			}
		}

		_activeBakeBuilder = new VoxelPvs();
		_activeBakeBuilder.BeginBuild(space, aabb, BakeVoxelSize, OccluderCollisionMask, maxVoxels: VoxelPvs.EditorBakeMaxVoxels);
		_bakeStartUsec = Time.GetTicksUsec();
		_bakeLastLoggedPct = -1f;
		int solid = _activeBakeBuilder.BuildSolidVoxels;
		int total = _activeBakeBuilder.TotalVoxels;
		float solidPct = total > 0 ? (float)solid / total * 100f : 0f;
		BakeStatus = $"Baking: 0% — {_activeBakeBuilder.Dims.X}×{_activeBakeBuilder.Dims.Y}×{_activeBakeBuilder.Dims.Z}={total} voxels @ {_activeBakeBuilder.VoxelSize:F1}m, {solid} solid ({solidPct:F0}%) skipped";
		GD.Print($"[VoxelPvsInstance] Bake started — scene='{sceneRoot.Name}', AABB={aabb} ({_bakeAabbSource}), {total} voxels @ {_activeBakeBuilder.VoxelSize:F1}m. Solid voxels (inside walls/floor): {solid} ({solidPct:F0}%) — those pairs will be skipped. Running at {BakeRaysPerFrame} rays/frame.");
	}

	/// <summary>Called once per editor frame while a bake is in progress. Steps the builder with the
	/// per-frame ray budget, updates <see cref="BakeStatus"/>, logs progress every ~5% and finalises
	/// (saves to disk) on completion.</summary>
	private void StepActiveBake()
	{
		bool done = _activeBakeBuilder.StepBuild(BakeRaysPerFrame);
		float pct = _activeBakeBuilder.BuildProgress01 * 100f;
		BakeStatus = $"Baking: {pct:F0}% — {_activeBakeBuilder.BuildRaysDone:N0} rays done";
		if (pct - _bakeLastLoggedPct >= 5f || done)
		{
			GD.Print($"[VoxelPvsInstance] Bake progress: {pct:F0}% ({_activeBakeBuilder.BuildRaysDone:N0} rays)");
			_bakeLastLoggedPct = pct;
		}
		if (done) FinishActiveBake();
	}

	private void FinishActiveBake()
	{
		var builder = _activeBakeBuilder;
		_activeBakeBuilder = null;
		double elapsedSec = (Time.GetTicksUsec() - _bakeStartUsec) / 1_000_000.0;

		string existingPath = Data?.ResourcePath;
		var data = new VoxelPvsData
		{
			Origin = builder.Origin,
			VoxelSize = builder.VoxelSize,
			Dims = builder.Dims,
			VisibilityBytes = builder.ExportBitsAsBytes(),
		};
		Data = data;

		string savePath = !string.IsNullOrEmpty(existingPath)
			? existingPath
			: DefaultSavePath(GetTree()?.EditedSceneRoot);
		var err = ResourceSaver.Save(data, savePath);
		if (err == Error.Ok)
		{
			long visible = CountVisible(data.VisibilityBytes);
			int n = data.TotalVoxels;
			double density = n > 0 ? (double)visible / ((double)n * n) : 0.0;
			BakeStatus = $"Bake complete: {data.Dims.X}×{data.Dims.Y}×{data.Dims.Z}={n} voxels @ {data.VoxelSize:F1}m, {density * 100.0:F1}% density, {elapsedSec:F1}s — saved to {savePath}";
			GD.Print($"[VoxelPvsInstance] Bake complete in {elapsedSec:F1}s — {data.Dims.X}×{data.Dims.Y}×{data.Dims.Z}={n} voxels @ {data.VoxelSize:F1}m, {builder.BuildRaysDone} rays, {visible} visible bit-pairs ({density * 100.0:F1}% density), saved to {savePath}. Don't forget to save the parent scene.");
		}
		else
		{
			BakeStatus = $"Save FAILED: {err}";
			GD.PushError($"[VoxelPvsInstance] Failed to save PVS data to {savePath}: {err}");
		}
		_bakeLastLoggedPct = -1f;
		NotifyPropertyListChanged();
	}

	/// <summary>Derives the default .tres save path from the parent scene's path. <c>res://x/y/foo.tscn</c>
	/// → <c>res://x/y/foo.pvs.tres</c>. Falls back to a project-root file when the scene is unsaved.</summary>
	private static string DefaultSavePath(Node sceneRoot)
	{
		string scenePath = sceneRoot.SceneFilePath;
		if (string.IsNullOrEmpty(scenePath)) return "res://voxel_pvs.tres";
		int dot = scenePath.LastIndexOf('.');
		if (dot < 0) return scenePath + ".pvs.tres";
		return scenePath.Substring(0, dot) + ".pvs.tres";
	}

	private static long CountVisible(byte[] bytes)
	{
		long count = 0;
		for (int i = 0; i < bytes.Length; i++)
		{
			byte b = bytes[i];
			while (b != 0) { count += b & 1; b >>= 1; }
		}
		return count;
	}

	/// <summary>True when <see cref="Data"/> contains a valid baked PVS.</summary>
	public bool HasBakedData => Data != null && Data.HasData;

	private MeshInstance3D _gizmoMesh;
	private StandardMaterial3D _gizmoMaterial;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint()) return;
		EnsureGizmoNode();
		UpdateGizmo();
	}

	public override void _Process(double delta)
	{
		if (!Engine.IsEditorHint()) return;
		if (_activeBakeBuilder != null) StepActiveBake();
		if (_gizmoMesh == null) EnsureGizmoNode();
		UpdateGizmo();
		UpdateHeatmap();
	}

	private void EnsureGizmoNode()
	{
		_gizmoMesh = GetNodeOrNull<MeshInstance3D>("_PvsGizmo");
		if (_gizmoMesh == null)
		{
			_gizmoMesh = new MeshInstance3D { Name = "_PvsGizmo" };
			AddChild(_gizmoMesh);
			_gizmoMesh.Owner = null;
		}
		if (_gizmoMaterial == null)
		{
			_gizmoMaterial = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				VertexColorUseAsAlbedo = true,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				NoDepthTest = false,
			};
			_gizmoMesh.MaterialOverride = _gizmoMaterial;
		}
	}

	private Aabb? _lastGizmoAabb;
	private float _lastGizmoVoxelSize;
	private Vector3I _lastGizmoDims;
	private bool _lastGizmoShowGrid;
	private bool _lastGizmoShow;

	private void UpdateGizmo()
	{
		if (_gizmoMesh == null) return;
		if (!ShowGizmo)
		{
			if (_lastGizmoShow) { _gizmoMesh.Hide(); _lastGizmoShow = false; }
			return;
		}
		Aabb aabb;
		float voxelSize;
		Vector3I dims;
		if (HasBakedData)
		{
			aabb = new Aabb(Data.Origin, new Vector3(Data.Dims.X, Data.Dims.Y, Data.Dims.Z) * Data.VoxelSize);
			voxelSize = Data.VoxelSize;
			dims = Data.Dims;
		}
		else
		{
			var sceneRoot = GetTree()?.EditedSceneRoot;
			if (sceneRoot == null) { _gizmoMesh.Hide(); return; }
			aabb = UseOverrideAabb
				? new Aabb(OverrideAabbOrigin, OverrideAabbSize)
				: VoxelPvs.ComputeWorldAabb(sceneRoot, OccluderCollisionMask);
			voxelSize = Mathf.Max(0.5f, BakeVoxelSize);
			dims = new Vector3I(
				Mathf.Max(1, Mathf.CeilToInt(aabb.Size.X / voxelSize)),
				Mathf.Max(1, Mathf.CeilToInt(aabb.Size.Y / voxelSize)),
				Mathf.Max(1, Mathf.CeilToInt(aabb.Size.Z / voxelSize)));
		}

		bool changed = !_lastGizmoAabb.HasValue
			|| _lastGizmoAabb.Value.Position != aabb.Position
			|| _lastGizmoAabb.Value.Size != aabb.Size
			|| _lastGizmoVoxelSize != voxelSize
			|| _lastGizmoDims != dims
			|| _lastGizmoShowGrid != ShowVoxelGrid
			|| !_lastGizmoShow;
		if (!changed) return;
		_lastGizmoAabb = aabb;
		_lastGizmoVoxelSize = voxelSize;
		_lastGizmoDims = dims;
		_lastGizmoShowGrid = ShowVoxelGrid;
		_lastGizmoShow = true;

		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Lines);
		Color aabbCol = HasBakedData ? new Color(1f, 0.85f, 0.2f, 0.9f) : new Color(1f, 0.5f, 0.2f, 0.7f);
		AddAabbWireframe(st, aabb, aabbCol);
		if (ShowVoxelGrid && dims.X * dims.Y * dims.Z <= 50_000)
		{
			Color gridCol = new(0.4f, 0.6f, 1f, 0.25f);
			AddVoxelGrid(st, aabb.Position, voxelSize, dims, gridCol);
		}
		var mesh = st.Commit();
		_gizmoMesh.Mesh = mesh;
		_gizmoMesh.GlobalTransform = Transform3D.Identity;
		_gizmoMesh.Show();
	}

	private static void AddAabbWireframe(SurfaceTool st, Aabb aabb, Color col)
	{
		Vector3 a = aabb.Position;
		Vector3 b = aabb.Position + aabb.Size;
		Vector3[] c = {
			new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, a.Y, b.Z), new(a.X, a.Y, b.Z),
			new(a.X, b.Y, a.Z), new(b.X, b.Y, a.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z),
		};
		int[] edges = { 0,1, 1,2, 2,3, 3,0,  4,5, 5,6, 6,7, 7,4,  0,4, 1,5, 2,6, 3,7 };
		for (int i = 0; i < edges.Length; i += 2) AddLine(st, c[edges[i]], c[edges[i + 1]], col);
	}

	private static void AddVoxelGrid(SurfaceTool st, Vector3 origin, float voxelSize, Vector3I dims, Color col)
	{
		Vector3 max = origin + new Vector3(dims.X, dims.Y, dims.Z) * voxelSize;
		for (int x = 0; x <= dims.X; x++) for (int y = 0; y <= dims.Y; y++)
			AddLine(st, new Vector3(origin.X + x * voxelSize, origin.Y + y * voxelSize, origin.Z),
				new Vector3(origin.X + x * voxelSize, origin.Y + y * voxelSize, max.Z), col);
		for (int x = 0; x <= dims.X; x++) for (int z = 0; z <= dims.Z; z++)
			AddLine(st, new Vector3(origin.X + x * voxelSize, origin.Y, origin.Z + z * voxelSize),
				new Vector3(origin.X + x * voxelSize, max.Y, origin.Z + z * voxelSize), col);
		for (int y = 0; y <= dims.Y; y++) for (int z = 0; z <= dims.Z; z++)
			AddLine(st, new Vector3(origin.X, origin.Y + y * voxelSize, origin.Z + z * voxelSize),
				new Vector3(max.X, origin.Y + y * voxelSize, origin.Z + z * voxelSize), col);
	}

	private static void AddLine(SurfaceTool st, Vector3 from, Vector3 to, Color col)
	{
		st.SetColor(col); st.AddVertex(from);
		st.SetColor(col); st.AddVertex(to);
	}

	private MultiMeshInstance3D _heatmapInstance;
	private MultiMesh _heatmapMesh;
	private VoxelPvsData _lastHeatmapData;
	private bool _lastHeatmapShown;

	/// <summary>Rebuilds (or hides) the per-voxel density heatmap. Lazy — only does real work when
	/// <see cref="ShowDensityHeatmap"/> just toggled on, or when <see cref="Data"/> got swapped. The
	/// underlying per-voxel visibility count is O(N²) — at 16k voxels takes ~1s — but only runs once
	/// per Data change.</summary>
	private void UpdateHeatmap()
	{
		if (!ShowDensityHeatmap || !HasBakedData)
		{
			if (_heatmapInstance != null && _lastHeatmapShown)
			{
				_heatmapInstance.Hide();
				_lastHeatmapShown = false;
			}
			return;
		}
		EnsureHeatmapNode();
		if (Data != _lastHeatmapData)
		{
			RebuildHeatmapData();
			_lastHeatmapData = Data;
		}
		if (!_lastHeatmapShown)
		{
			_heatmapInstance.Show();
			_lastHeatmapShown = true;
		}
	}

	private void EnsureHeatmapNode()
	{
		_heatmapInstance = GetNodeOrNull<MultiMeshInstance3D>("_PvsHeatmap");
		if (_heatmapInstance == null)
		{
			_heatmapInstance = new MultiMeshInstance3D { Name = "_PvsHeatmap" };
			AddChild(_heatmapInstance);
			_heatmapInstance.Owner = null;
			_heatmapInstance.MaterialOverride = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				VertexColorUseAsAlbedo = true,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				NoDepthTest = false,
			};
		}
		if (_heatmapMesh == null)
		{
			_heatmapMesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				UseColors = true,
			};
			_heatmapInstance.Multimesh = _heatmapMesh;
		}
	}

	private void RebuildHeatmapData()
	{
		var data = Data;
		var counts = data.ComputePerVoxelVisibleCounts();
		int n = data.TotalVoxels;
		int maxCount = 0;
		int playable = 0;
		for (int i = 0; i < n; i++)
		{
			int c = counts[i];
			if (c > 0) playable++;
			if (c > maxCount) maxCount = c;
		}
		_heatmapMesh.Mesh = new BoxMesh { Size = Vector3.One * data.VoxelSize * 0.4f };
		_heatmapMesh.InstanceCount = playable;
		int slot = 0;
		for (int i = 0; i < n; i++)
		{
			int c = counts[i];
			if (c == 0) continue;
			Vector3 center = VoxelCenterFromData(data, i);
			_heatmapMesh.SetInstanceTransform(slot, new Transform3D(Basis.Identity, center));
			float ratio = maxCount > 0 ? (float)c / maxCount : 0f;
			_heatmapMesh.SetInstanceColor(slot, Color.FromHsv(ratio * 0.333f, 0.9f, 0.95f, 0.55f));
			slot++;
		}
		GD.Print($"[VoxelPvsInstance] Density heatmap rebuilt — {playable} playable voxels of {n} (skipped {n - playable} solid), max visibility = {maxCount}, color: red=enclosed → green=open.");
	}

	private static Vector3 VoxelCenterFromData(VoxelPvsData data, int index)
	{
		int x = index % data.Dims.X;
		int rem = index / data.Dims.X;
		int y = rem % data.Dims.Y;
		int z = rem / data.Dims.Y;
		return data.Origin + new Vector3(
			(x + 0.5f) * data.VoxelSize,
			(y + 0.5f) * data.VoxelSize,
			(z + 0.5f) * data.VoxelSize);
	}
}
