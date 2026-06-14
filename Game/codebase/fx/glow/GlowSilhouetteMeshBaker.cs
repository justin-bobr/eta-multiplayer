using System.Collections.Generic;
using Godot;

/// <summary>
/// Editor tool + runtime asset. As a child of the puppet's Skeleton3D, the inspector <see cref="Bake"/>
/// trigger merges every visible skinned body MeshInstance3D's silhouette into this node's own
/// <see cref="MeshInstance3D.Mesh"/>; at runtime it's a regular skinned MeshInstance3D that PuppetPlayer
/// toggles and recolours via SetInstanceShaderParameter("team_color", …).
/// The bake welds vertices (<see cref="WeldVerts"/>) and suppresses boundary spikes (<see cref="SuppressBoundarySpikes"/>);
/// <see cref="Cancel"/> aborts in progress, leaving Mesh unchanged. Hidden meshes are skipped.
/// </summary>
[Tool, GlobalClass]
public partial class GlowSilhouetteMeshBaker : MeshInstance3D
{
	[Export(PropertyHint.Range, "0.0001,0.02,0.0001")]
	public float WeldEpsilon = 0.001f;

	/// <summary>Default team colour for the glow chain; the setter updates the live material chain. PuppetPlayer overrides it per-instance at runtime.</summary>
	[Export]
	public Color GlowColor
	{
		get => _glowColor;
		set
		{
			_glowColor = value;
			PropagateGlowColorToChain();
		}
	}
	private Color _glowColor = new Color(0.0f, 1.0f, 0.4f, 1.0f);

	/// <summary>Starting alpha of the inner rim; the second band and fade shells scale from it. Master glow-intensity knob.</summary>
	[Export(PropertyHint.Range, "0.0,1.0,0.01")]
	public float GlowStartAlpha = 1.0f;

	/// <summary>Inner sharp rim width in world metres, auto-scaled by skeleton scale at bake time.</summary>
	[Export(PropertyHint.Range, "0.0001,0.05,0.0001")]
	public float OutlineWidth = 0.004f;

	/// <summary>Width of the second solid band (the main visible halo) just outside the inner rim.</summary>
	[Export(PropertyHint.Range, "0.001,0.1,0.0005")]
	public float SecondLayerWidth = 0.015f;

	/// <summary>World-metre extent of the fade tail past the second band, across <see cref="GlowShellCount"/> shells.</summary>
	[Export(PropertyHint.Range, "0.005,0.5,0.001")]
	public float GlowMaxWidth = 0.05f;

	/// <summary>Number of fade-tail shells (more = smoother gradient, one draw call each).</summary>
	[Export(PropertyHint.Range, "0,30,1")]
	public int GlowShellCount = 10;

	/// <summary>MeshInstance3D NodePaths to exclude from the bake (resolved fresh per bake). For variants that must stay
	/// enabled for gameplay but should not be in the silhouette. Unresolvable paths are logged and skipped.</summary>
	[Export] public Godot.Collections.Array<NodePath> ExcludedMeshes = new();

	/// <summary>Optional material override; null builds the default outline/fade ShaderMaterial chain.</summary>
	[Export] public ShaderMaterial CustomOutlineMaterial;

	[Export]
	public bool Bake
	{
		get => false;
		set { if (value) StartBake(); }
	}

	[Export]
	public bool Cancel
	{
		get => false;
		set { if (value) RequestCancel(); }
	}

	[Export]
	public string Status
	{
		get => _status;
		set { /* readonly */ }
	}

	private string _status = "Idle";
	private bool _baking;
	private bool _cancelRequested;

	private Skeleton3D _bakeSkeleton;
	private List<MeshInstance3D> _bakeMeshes;
	private int _bakeMeshIdx;
	private List<Vector3> _allVerts;
	private List<Vector3> _allNormals;
	private List<int> _allBones;
	private List<float> _allWeights;
	private List<int> _allIndices;
	private int _boneCountPerVertex;
	private Skin _sourceSkin;

	private static Shader _outlineShaderCached;
	private static Shader OutlineShader => _outlineShaderCached ??= GD.Load<Shader>("res://codebase/fx/glow/team_outline_hull.gdshader");
	private static Shader _outlineDistanceShaderCached;
	private static Shader OutlineDistanceShader => _outlineDistanceShaderCached ??= GD.Load<Shader>("res://codebase/fx/glow/team_outline_distance.gdshader");
	private static Shader _outlineGlowShaderCached;
	private static Shader OutlineGlowShader => _outlineGlowShaderCached ??= GD.Load<Shader>("res://codebase/fx/glow/team_outline_glow.gdshader");
	private static Shader _xrayShaderCached;
	private static Shader XrayShader => _xrayShaderCached ??= GD.Load<Shader>("res://codebase/fx/glow/team_outline_xray.gdshader");
	private static Shader _innerFadeShaderCached;
	private static Shader InnerFadeShader => _innerFadeShaderCached ??= GD.Load<Shader>("res://codebase/fx/glow/team_inner_fade.gdshader");

	public override void _Ready()
	{
		if (!Engine.IsEditorHint()) return;
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		if (!_baking) return;

		if (_cancelRequested)
		{
			AbortBake();
			return;
		}

		if (_bakeMeshIdx >= _bakeMeshes.Count)
		{
			WeldAndCommit();
			return;
		}

		var mi = _bakeMeshes[_bakeMeshIdx];
		AppendMeshSurfaces(mi);
		_bakeMeshIdx++;
		UpdateStatus($"Baking {_bakeMeshIdx}/{_bakeMeshes.Count} — {mi.Name} ({_allVerts.Count} verts)");
	}

	private void StartBake()
	{
		if (_baking)
		{
			UpdateStatus("Already baking — ignored");
			return;
		}

		_bakeSkeleton = GetParent() as Skeleton3D;
		if (_bakeSkeleton == null)
		{
			UpdateStatus("ERROR: parent is not a Skeleton3D — re-parent this node under the puppet's Skeleton3D");
			return;
		}

		_bakeMeshes = new List<MeshInstance3D>();
		WalkForBodyMeshes(_bakeSkeleton, _bakeMeshes);
		if (_bakeMeshes.Count == 0)
		{
			UpdateStatus($"ERROR: 0 VISIBLE body meshes found under '{_bakeSkeleton.Name}' (check BodyPrefixes / visibility)");
			return;
		}

		_sourceSkin = _bakeMeshes[0].Skin;
		_bakeMeshIdx = 0;
		_allVerts = new List<Vector3>();
		_allNormals = new List<Vector3>();
		_allBones = new List<int>();
		_allWeights = new List<float>();
		_allIndices = new List<int>();
		_boneCountPerVertex = 4;
		_cancelRequested = false;
		_baking = true;
		SetProcess(true);
		UpdateStatus($"Starting bake of {_bakeMeshes.Count} visible meshes (weld ε={WeldEpsilon}m)...");
	}

	private void RequestCancel()
	{
		if (!_baking)
		{
			UpdateStatus("Cancel ignored — no bake in progress");
			return;
		}
		_cancelRequested = true;
	}

	private void AbortBake()
	{
		_baking = false;
		_cancelRequested = false;
		int processed = _bakeMeshIdx;
		_bakeSkeleton = null;
		_bakeMeshes = null;
		_allVerts = null;
		_allNormals = null;
		_allBones = null;
		_allWeights = null;
		_allIndices = null;
		SetProcess(false);
		UpdateStatus($"Cancelled after {processed} meshes — Mesh unchanged");
	}

	private void AppendMeshSurfaces(MeshInstance3D mi)
	{
		var mesh = mi.Mesh;
		if (mesh == null)
		{
			GD.PushWarning($"[GlowSilhouetteMeshBaker] {mi.Name} has no mesh, skipping");
			return;
		}

		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			var arrays = mesh.SurfaceGetArrays(s);
			var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
			var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
			var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
			var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

			if (verts.Length == 0) continue;
			if (normals.Length != verts.Length || indices.Length == 0)
			{
				GD.PushWarning($"[GlowSilhouetteMeshBaker] Skipping {mi.Name} surface {s}: missing normals or indices");
				continue;
			}

			int perVertex = bones.Length / verts.Length;
			if (perVertex != 4 && perVertex != 8)
			{
				GD.PushWarning($"[GlowSilhouetteMeshBaker] {mi.Name} surface {s}: unexpected {perVertex} bones/vertex");
				continue;
			}
			if (_allVerts.Count > 0 && perVertex != _boneCountPerVertex)
			{
				GD.PushWarning($"[GlowSilhouetteMeshBaker] {mi.Name} bones/vertex={perVertex} differs from accumulated {_boneCountPerVertex}");
			}
			_boneCountPerVertex = perVertex;

			int vertexOffset = _allVerts.Count;
			_allVerts.AddRange(verts);
			_allNormals.AddRange(normals);
			_allBones.AddRange(bones);
			_allWeights.AddRange(weights);
			foreach (var idx in indices) _allIndices.Add(idx + vertexOffset);
		}
	}

	/// <summary>Spatial-hash vertex weld; merged normals are averaged so the hull push is smooth, closing cut edges
	/// between body meshes. <see cref="WeldEpsilon"/> is world-metres and is rescaled by the skeleton's basis scale
	/// into mesh-local units before comparing (mesh is typically cm-authored under a 0.01x transform).</summary>
	private void WeldVerts()
	{
		int n = _allVerts.Count;
		if (n == 0) return;

		float worldScale = 1f;
		if (_bakeSkeleton != null)
		{
			var s = _bakeSkeleton.GlobalBasis.Scale;
			worldScale = (s.X + s.Y + s.Z) / 3f;
			if (worldScale <= 0f) worldScale = 1f;
		}
		float effectiveEpsilon = WeldEpsilon / worldScale;

		int perVert = _boneCountPerVertex;
		float cell = effectiveEpsilon * 2.0f;
		var grid = new Dictionary<(int X, int Y, int Z), List<int>>();
		var remap = new int[n];
		var newVerts = new List<Vector3>(n);
		var newNormals = new List<Vector3>(n);
		var newBones = new List<int>(n * perVert);
		var newWeights = new List<float>(n * perVert);
		float epsilonSq = effectiveEpsilon * effectiveEpsilon;

		for (int i = 0; i < n; i++)
		{
			var v = _allVerts[i];
			int kx = Mathf.FloorToInt(v.X / cell);
			int ky = Mathf.FloorToInt(v.Y / cell);
			int kz = Mathf.FloorToInt(v.Z / cell);

			int found = -1;
			for (int dx = -1; dx <= 1 && found < 0; dx++)
			for (int dy = -1; dy <= 1 && found < 0; dy++)
			for (int dz = -1; dz <= 1 && found < 0; dz++)
			{
				if (grid.TryGetValue((kx + dx, ky + dy, kz + dz), out var bucket))
				{
					foreach (int j in bucket)
					{
						if (v.DistanceSquaredTo(newVerts[j]) < epsilonSq)
						{
							found = j;
							break;
						}
					}
				}
			}

			if (found >= 0)
			{
				remap[i] = found;
				newNormals[found] += _allNormals[i];
			}
			else
			{
				int newIdx = newVerts.Count;
				remap[i] = newIdx;
				newVerts.Add(v);
				newNormals.Add(_allNormals[i]);
				for (int b = 0; b < perVert; b++)
				{
					newBones.Add(_allBones[i * perVert + b]);
					newWeights.Add(_allWeights[i * perVert + b]);
				}
				if (!grid.TryGetValue((kx, ky, kz), out var list))
				{
					list = new List<int>();
					grid[(kx, ky, kz)] = list;
				}
				list.Add(newIdx);
			}
		}

		for (int i = 0; i < newNormals.Count; i++) newNormals[i] = newNormals[i].Normalized();
		var newIndices = new List<int>(_allIndices.Count);
		foreach (int idx in _allIndices) newIndices.Add(remap[idx]);

		GD.Print($"[GlowSilhouetteMeshBaker] Welded {n} → {newVerts.Count} verts (ε={WeldEpsilon}m world / {effectiveEpsilon:F4} mesh-local, scale={worldScale:F4}, {n - newVerts.Count} collapsed)");
		_allVerts = newVerts;
		_allNormals = newNormals;
		_allBones = newBones;
		_allWeights = newWeights;
		_allIndices = newIndices;
	}

	/// <summary>Replaces open-boundary vertex normals (edges in exactly one triangle) with the average of their
	/// non-boundary neighbours so the hull push doesn't spike; zeroes the normal when no neighbour smooths it.</summary>
	private void SuppressBoundarySpikes()
	{
		int triCount = _allIndices.Count / 3;
		if (triCount == 0) return;

		var edgeCount = new Dictionary<(int U, int V), int>(triCount * 3);
		for (int t = 0; t < triCount; t++)
		{
			int a = _allIndices[t * 3];
			int b = _allIndices[t * 3 + 1];
			int c = _allIndices[t * 3 + 2];
			IncrementEdge(edgeCount, a, b);
			IncrementEdge(edgeCount, b, c);
			IncrementEdge(edgeCount, c, a);
		}

		var boundary = new HashSet<int>();
		foreach (var kvp in edgeCount)
		{
			if (kvp.Value == 1)
			{
				boundary.Add(kvp.Key.U);
				boundary.Add(kvp.Key.V);
			}
		}

		if (boundary.Count == 0)
		{
			GD.Print("[GlowSilhouetteMeshBaker] No open boundaries — mesh is already closed");
			return;
		}

		var smoothed = new Vector3[_allNormals.Count];
		var smoothedCount = new int[_allNormals.Count];
		for (int t = 0; t < triCount; t++)
		{
			int a = _allIndices[t * 3];
			int b = _allIndices[t * 3 + 1];
			int c = _allIndices[t * 3 + 2];
			AccumulateNonBoundaryNormal(boundary, smoothed, smoothedCount, a, b, c);
			AccumulateNonBoundaryNormal(boundary, smoothed, smoothedCount, b, c, a);
			AccumulateNonBoundaryNormal(boundary, smoothed, smoothedCount, c, a, b);
		}

		int zeroed = 0;
		for (int i = 0; i < _allNormals.Count; i++)
		{
			if (!boundary.Contains(i)) continue;
			if (smoothedCount[i] > 0)
			{
				_allNormals[i] = smoothed[i].Normalized();
			}
			else
			{
				_allNormals[i] = Vector3.Zero;
				zeroed++;
			}
		}

		GD.Print($"[GlowSilhouetteMeshBaker] Boundary spike suppression: {boundary.Count} boundary verts ({zeroed} zeroed, {boundary.Count - zeroed} smoothed)");
	}

	private static void IncrementEdge(Dictionary<(int U, int V), int> map, int u, int v)
	{
		var key = u < v ? (u, v) : (v, u);
		map[key] = map.TryGetValue(key, out var c) ? c + 1 : 1;
	}

	private void AccumulateNonBoundaryNormal(HashSet<int> boundary, Vector3[] sum, int[] count, int center, int a, int b)
	{
		if (!boundary.Contains(center)) return;
		if (!boundary.Contains(a))
		{
			sum[center] += _allNormals[a];
			count[center]++;
		}
		if (!boundary.Contains(b))
		{
			sum[center] += _allNormals[b];
			count[center]++;
		}
	}

	/// <summary>Drops degenerate, sub-millimetre sliver, and duplicate triangles (same vertex indices after welding overlapping source meshes).</summary>
	private void RemoveDuplicateTriangles()
	{
		int triCount = _allIndices.Count / 3;
		if (triCount == 0) return;

		var seen = new HashSet<(int A, int B, int C)>(triCount);
		var kept = new List<int>(_allIndices.Count);
		int degenerate = 0;
		int slivers = 0;
		float worldScale = 1f;
		if (_bakeSkeleton != null)
		{
			var s = _bakeSkeleton.GlobalBasis.Scale;
			worldScale = (s.X + s.Y + s.Z) / 3f;
			if (worldScale <= 0.0001f) worldScale = 1f;
		}
		float sliverEps = 0.001f / worldScale;
		float sliverEpsSq = sliverEps * sliverEps;

		for (int t = 0; t < triCount; t++)
		{
			int a = _allIndices[t * 3];
			int b = _allIndices[t * 3 + 1];
			int c = _allIndices[t * 3 + 2];
			if (a == b || b == c || a == c)
			{
				degenerate++;
				continue;
			}
			if (_allVerts[a].DistanceSquaredTo(_allVerts[b]) < sliverEpsSq ||
				_allVerts[b].DistanceSquaredTo(_allVerts[c]) < sliverEpsSq ||
				_allVerts[a].DistanceSquaredTo(_allVerts[c]) < sliverEpsSq)
			{
				slivers++;
				continue;
			}
			int s0 = a, s1 = b, s2 = c;
			if (s0 > s1) (s0, s1) = (s1, s0);
			if (s1 > s2) (s1, s2) = (s2, s1);
			if (s0 > s1) (s0, s1) = (s1, s0);
			if (seen.Add((s0, s1, s2)))
			{
				kept.Add(a);
				kept.Add(b);
				kept.Add(c);
			}
		}

		int dropped = triCount - kept.Count / 3;
		GD.Print($"[GlowSilhouetteMeshBaker] Removed {dropped} triangles ({degenerate} degenerate-index, {slivers} sliver < 1mm, {dropped - degenerate - slivers} duplicates), {kept.Count / 3} remaining of {triCount}");
		_allIndices = kept;
	}

	private void WeldAndCommit()
	{
		WeldVerts();
		RemoveDuplicateTriangles();
		SuppressBoundarySpikes();

		var combined = new ArrayMesh();
		var combinedArrays = new Godot.Collections.Array();
		combinedArrays.Resize((int)Mesh.ArrayType.Max);
		combinedArrays[(int)Mesh.ArrayType.Vertex] = _allVerts.ToArray();
		combinedArrays[(int)Mesh.ArrayType.Normal] = _allNormals.ToArray();
		combinedArrays[(int)Mesh.ArrayType.Bones] = _allBones.ToArray();
		combinedArrays[(int)Mesh.ArrayType.Weights] = _allWeights.ToArray();
		combinedArrays[(int)Mesh.ArrayType.Index] = _allIndices.ToArray();
		combined.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, combinedArrays);

		Mesh = combined;
		Skin = _sourceSkin;
		Skeleton = new NodePath("..");
		CastShadow = ShadowCastingSetting.Off;
		GIMode = GIModeEnum.Disabled;
		IgnoreOcclusionCulling = true;
		VisibilityRangeBegin = 0f;
		VisibilityRangeEnd = 0f;
		VisibilityRangeBeginMargin = 0f;
		VisibilityRangeEndMargin = 0f;
		VisibilityRangeFadeMode = VisibilityRangeFadeModeEnum.Disabled;
		LodBias = 8f;
		ExtraCullMargin = 5f;
		MaterialOverride = CustomOutlineMaterial ?? BuildDefaultOutlineMaterialChain();

		int finalVerts = _allVerts.Count;
		int finalTris = _allIndices.Count / 3;
		int finalBones = _boneCountPerVertex;

		_bakeSkeleton = null;
		_bakeMeshes = null;
		_allVerts = null;
		_allNormals = null;
		_allBones = null;
		_allWeights = null;
		_allIndices = null;
		_baking = false;
		SetProcess(false);
		NotifyPropertyListChanged();

		UpdateStatus($"Done — {finalVerts} verts, {finalTris} tris, {finalBones} bones/vertex baked into self.Mesh. Save the scene to persist.");
	}

	/// <summary>Builds the default outline-hull + second-band + fade-shell + xray material chain (widths scale-corrected). PuppetPlayer overrides team_color per-instance.</summary>
	private ShaderMaterial BuildDefaultOutlineMaterialChain()
	{
		var debugColor = _glowColor;

		float worldScale = 1f;
		if (_bakeSkeleton != null)
		{
			var s = _bakeSkeleton.GlobalBasis.Scale;
			worldScale = (s.X + s.Y + s.Z) / 3f;
			if (worldScale <= 0.0001f) worldScale = 1f;
		}
		else if (GetParent() is Skeleton3D parentSkel)
		{
			var s = parentSkel.GlobalBasis.Scale;
			worldScale = (s.X + s.Y + s.Z) / 3f;
			if (worldScale <= 0.0001f) worldScale = 1f;
		}
		float widthScale = 1f / worldScale;
		GD.Print($"[GlowSilhouetteMeshBaker] outline_width scale-correction: worldScale={worldScale:F4}, widthScale={widthScale:F2}, effective outline_width_base={0.004f * widthScale:F4}");

		float startAlpha = Mathf.Clamp(GlowStartAlpha, 0f, 1f);

		var outline = new ShaderMaterial { Shader = OutlineShader };
		outline.SetShaderParameter("team_color", debugColor);
		outline.SetShaderParameter("outline_width_base", OutlineWidth * widthScale);
		outline.SetShaderParameter("alpha", startAlpha);

		var secondLayer = new ShaderMaterial { Shader = OutlineDistanceShader };
		secondLayer.SetShaderParameter("team_color", debugColor);
		secondLayer.SetShaderParameter("outline_width_base", SecondLayerWidth * widthScale);
		secondLayer.SetShaderParameter("alpha", 0.55f * startAlpha);
		outline.NextPass = secondLayer;

		int shellCount = Mathf.Max(0, GlowShellCount);
		ShaderMaterial prevShell = secondLayer;
		float innerW = SecondLayerWidth;
		float outerW = Mathf.Max(GlowMaxWidth, innerW + 0.001f);
		for (int i = 0; i < shellCount; i++)
		{
			float t = (i + 1f) / shellCount;
			float w = Mathf.Lerp(innerW, outerW, t);
			float a = 0.40f * Mathf.Pow(1f - t, 1.5f) * startAlpha;
			var shell = new ShaderMaterial { Shader = OutlineGlowShader };
			shell.SetShaderParameter("team_color", debugColor);
			shell.SetShaderParameter("outline_width_base", w * widthScale);
			shell.SetShaderParameter("alpha", a);
			prevShell.NextPass = shell;
			prevShell = shell;
		}

		var xray = new ShaderMaterial { Shader = XrayShader };
		xray.SetShaderParameter("team_color", debugColor);
		xray.SetShaderParameter("alpha", 0.85f * startAlpha);
		xray.SetShaderParameter("fresnel_power", 3.0f);
		xray.SetShaderParameter("depth_tolerance", 0.50f);
		xray.SetShaderParameter("self_clip_distance", 0.50f);
		prevShell.NextPass = xray;

		return outline;
	}

	private void UpdateStatus(string message)
	{
		_status = message;
		GD.Print($"[GlowSilhouetteMeshBaker] {message}");
		NotifyPropertyListChanged();
	}

	/// <summary>Pushes <see cref="_glowColor"/> onto every shell's <c>team_color</c> uniform down the next_pass chain.</summary>
	private void PropagateGlowColorToChain()
	{
		var mat = MaterialOverride as ShaderMaterial;
		int updated = 0;
		while (mat != null)
		{
			mat.SetShaderParameter("team_color", _glowColor);
			updated++;
			mat = mat.NextPass as ShaderMaterial;
		}
		if (updated > 0 && Engine.IsEditorHint())
		{
			GD.Print($"[GlowSilhouetteMeshBaker] GlowColor → {updated} shells updated.");
		}
	}

	/// <summary>Collects every visible MeshInstance3D under the parent Skeleton3D, skipping this baker node, hidden meshes, and <see cref="ExcludedMeshes"/>.</summary>
	private void WalkForBodyMeshes(Node node, List<MeshInstance3D> sink)
	{
		var excludedSet = ResolveExcludedMeshes();
		WalkForBodyMeshesInternal(node, sink, excludedSet);
	}

	private void WalkForBodyMeshesInternal(Node node, List<MeshInstance3D> sink, HashSet<Node> excluded)
	{
		if (node == this) return;
		if (excluded.Contains(node)) return;
		if (node is MeshInstance3D mi && mi.IsVisibleInTree()) sink.Add(mi);
		for (int i = 0; i < node.GetChildCount(); i++) WalkForBodyMeshesInternal(node.GetChild(i), sink, excluded);
	}

	private HashSet<Node> ResolveExcludedMeshes()
	{
		var set = new HashSet<Node>();
		if (ExcludedMeshes == null) return set;
		foreach (var path in ExcludedMeshes)
		{
			if (path == null || path.IsEmpty) continue;
			var node = GetNodeOrNull(path);
			if (node == null)
			{
				GD.PushWarning($"[GlowSilhouetteMeshBaker] ExcludedMeshes: path '{path}' did not resolve to a node — skipping.");
				continue;
			}
			set.Add(node);
		}
		if (set.Count > 0) GD.Print($"[GlowSilhouetteMeshBaker] Excluding {set.Count} mesh nodes from bake.");
		return set;
	}
}
