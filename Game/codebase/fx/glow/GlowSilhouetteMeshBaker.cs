using System.Collections.Generic;
using Godot;

/// <summary>
/// Editor tool + runtime asset. Drop one of these as a child of the puppet's Skeleton3D — at edit
/// time it bakes the silhouette of every visible body-prefixed MeshInstance3D into ITS OWN
/// <see cref="MeshInstance3D.Mesh"/> via the inspector's <see cref="Bake"/> trigger; at runtime it
/// is just a regular MeshInstance3D, skin-deformed by the parent Skeleton3D, ready for
/// PuppetPlayer to flip <see cref="GeometryInstance3D.Visible"/> + push a team-specific
/// SetInstanceShaderParameter("team_color", …) onto it.
///
/// No extra child node, no orphan gizmo — this node IS the silhouette asset.
///
/// Workflow:
///   1. Open puppet_player.tscn in the editor.
///   2. Add a GlowSilhouetteMeshBaker as child of the Skeleton3D (right-click → Add Child Node).
///   3. Tick <see cref="Bake"/>. The baker walks the (visible) body meshes one per editor frame,
///      welds vertices within <see cref="WeldEpsilon"/>, suppresses boundary spikes, and writes
///      the merged result into its own <see cref="MeshInstance3D.Mesh"/> property.
///   4. Save the scene. The baker node — with its baked mesh + outline / inner-fade materials —
///      is now part of puppet_player.tscn.
///   5. Tick <see cref="Cancel"/> to abort a running bake (Mesh stays at whatever it was before).
///
/// Cut-edge welding + boundary-spike suppression: see <see cref="WeldVerts"/> and
/// <see cref="SuppressBoundarySpikes"/> for the rationale and math.
///
/// Visibility filter: hidden meshes (Visible = false on the mesh or any ancestor) are SKIPPED.
/// </summary>
[Tool, GlobalClass]
public partial class GlowSilhouetteMeshBaker : MeshInstance3D
{
	[Export(PropertyHint.Range, "0.0001,0.02,0.0001")]
	public float WeldEpsilon = 0.001f;

	/// <summary>Default team colour baked into every shell of the glow chain. Changing this in
	/// the inspector walks the existing MaterialOverride chain and updates each shell's team_color
	/// uniform immediately (no re-bake needed) — useful for quickly auditioning colour variants.
	/// At runtime, PuppetPlayer overrides this per-instance via SetInstanceShaderParameter, so
	/// this value is just the editor default / fallback when no instance override is set.</summary>
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

	/// <summary>Starting alpha of the inner sharp rim. The second layer and all fade-tail shells
	/// scale proportionally from this — set it to 1.0 for a full-opacity inner rim plus the
	/// normal fade tail, 0.5 for a half-transparent rim with proportionally fainter outer halo.
	/// Use this to dial the overall glow intensity without retuning every individual shell.</summary>
	[Export(PropertyHint.Range, "0.0,1.0,0.01")]
	public float GlowStartAlpha = 1.0f;

	/// <summary>Width of the inner sharp rim (outline_hull) in world-space metres. Values are
	/// auto-scaled by the detected skeleton scale at bake time, so 0.004 = 4mm world regardless of
	/// whether the source art is cm- or m-authored.</summary>
	[Export(PropertyHint.Range, "0.0001,0.05,0.0001")]
	public float OutlineWidth = 0.004f;

	/// <summary>Width of the SECOND solid band (outline_distance) — the main visible halo at high
	/// alpha, sitting just outside the sharp inner rim. Default 0.015m = 1.5cm wider band that
	/// reads as the primary "glow body" before the fade tail begins.</summary>
	[Export(PropertyHint.Range, "0.001,0.1,0.0005")]
	public float SecondLayerWidth = 0.015f;

	/// <summary>How far the outermost fade tail extends past the second band, in world-space
	/// metres. The fade shells are spaced linearly from <see cref="SecondLayerWidth"/> out to this
	/// value across <see cref="GlowShellCount"/> steps. Default 0.05m = 5cm fade tail.</summary>
	[Export(PropertyHint.Range, "0.005,0.5,0.001")]
	public float GlowMaxWidth = 0.05f;

	/// <summary>How many shell passes form the outward fade tail beyond the second band. Each
	/// shell is uniform alpha; more shells = smaller alpha jumps = smoother perceived gradient
	/// (no triangle artifacts from per-fragment normal gradients). One extra draw call per shell,
	/// so keep moderate at runtime. 10 looks smooth in most conditions; 15-20 if you want
	/// imperceptible banding at close range.</summary>
	[Export(PropertyHint.Range, "0,30,1")]
	public int GlowShellCount = 10;

	/// <summary>NodePaths (relative to this baker node) of MeshInstance3D nodes to exclude from
	/// the bake. Resolved fresh on every bake — drag-and-drop a mesh node into a slot in the
	/// inspector to opt it out. Useful for hidden gear variants that <see cref="Node3D.Visible"/>
	/// alone can't filter (because the variant must stay enabled for gameplay logic), or for
	/// floating equipment whose own internal silhouette ring is more noisy than helpful (large
	/// backpacks, antennas, microphone booms).
	///
	/// Unresolvable paths are logged and skipped — does not fail the bake.</summary>
	[Export] public Godot.Collections.Array<NodePath> ExcludedMeshes = new();

	/// <summary>Optional override — leave null to let the baker create a fresh ShaderMaterial chain
	/// (team_outline_hull.gdshader + team_inner_fade.gdshader via next_pass). Drop a custom material
	/// here to reuse pre-tuned outline_width / fresnel_power / max_alpha across re-bakes.</summary>
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

	/// <summary>Spatial-hash vertex weld; merged normals are summed then re-normalised so the
	/// inverted-hull's vertex push direction is the smooth average of all faces meeting at the
	/// welded position — closes the Schnittkanten between adjacent body meshes.
	///
	/// WeldEpsilon is interpreted as a metric in WORLD-SPACE metres (what the user intuitively
	/// thinks of as "1mm"), but mesh vertices live in MESH-LOCAL space which can be at a very
	/// different scale: the puppet scene typically wraps its skeleton in a 0.01x cm→m conversion
	/// transform (mesh authored in cm, world rendered in m). 1mm world = 100mm = 10cm in mesh-
	/// local. Without scale correction, WeldEpsilon = 0.001 looks at distances 100× too tight and
	/// effectively does nothing (verified empirically: 0.00001 vs 0.02 yielded the same triangle
	/// count). The fix sniffs the skeleton's global basis scale and rescales the epsilon into the
	/// vertex data's native units before comparing.</summary>
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

	/// <summary>Edges shared by exactly ONE triangle are open-mesh boundaries (Shirt-hem,
	/// Pants-top, neck-hole, etc.). Their vertex normals shoot outward perpendicular to the body
	/// surface, so an inverted-hull push along NORMAL produces sharp spikes there. Fix:
	/// for every boundary vertex, replace its normal with the average of its non-boundary
	/// neighbours; if no smoothable neighbour exists, zero the normal so the vertex push is a
	/// no-op (the shell tucks back to the original geometry there).</summary>
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

	/// <summary>After welding, two triangles that originally lived in different source meshes but
	/// at near-identical world positions (Body chest verts ↔ Shirt chest verts welded together)
	/// collapse to triangles with the SAME three vertex indices. Same indices regardless of winding
	/// = redundant surface coverage. Deduplicate via a sorted-tuple HashSet — keeps the first
	/// occurrence, drops every subsequent one. Typically halves triangle count on character meshes
	/// where body skin + shirt + vest all overlap in the torso area.</summary>
	private void RemoveDuplicateTriangles()
	{
		int triCount = _allIndices.Count / 3;
		if (triCount == 0) return;

		var seen = new HashSet<(int A, int B, int C)>(triCount);
		var kept = new List<int>(_allIndices.Count);
		int degenerate = 0;
		int slivers = 0;
		// Sliver threshold: triangles whose two verts are within this distance (mesh-local) get
		// dropped as visually degenerate. Cross-component welds frequently create long thin "fin"
		// triangles spanning the body↔equipment gap; after one vertex anchors to the body the
		// other two might be very close but not exactly identical. These render as sharp shards
		// in dense equipment areas. 1mm world threshold → scale-correct to mesh-local.
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
		// LOD / visibility-range / occlusion all disabled — the silhouette outline MUST render at
		// every distance and from every angle the puppet is visible from. Otherwise the rim pops
		// in/out as the puppet crosses the LOD threshold (~tier Far in PuppetPlayer's _lodTier
		// system) and the team-glow visibility becomes unreliable. ExtraCullMargin pads the frustum
		// AABB so a puppet whose center is just off-screen but whose silhouette extent sticks into
		// the screen still gets submitted.
		VisibilityRangeBegin = 0f;
		VisibilityRangeEnd = 0f;
		VisibilityRangeBeginMargin = 0f;
		VisibilityRangeEndMargin = 0f;
		VisibilityRangeFadeMode = VisibilityRangeFadeModeEnum.Disabled;
		LodBias = 8f;
		ExtraCullMargin = 5f;
		// Material applied only via MaterialOverride (NOT also on the ArrayMesh surface) — having
		// it in both slots was redundant; MaterialOverride takes precedence per-instance anyway,
		// and a duplicate surface material made re-bakes confusing in the inspector ("which one
		// does my edit go to?"). Per-puppet team_color is pushed via SetInstanceShaderParameter
		// on this MeshInstance3D — the instance slot overrides the override material's uniform
		// per render call without touching the shared material chain.
		// Force-rebuild every bake so re-bakes pick up new shader-default values (e.g. when we
		// tweak outline_width_base or add a new pass). User overrides via CustomOutlineMaterial
		// still win.
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

	/// <summary>Builds the default outline-hull + inner-fade material chain with a bright cyan
	/// team_color so the bake is immediately visible in the editor 3D viewport — otherwise a fresh
	/// bake gets a white default that disappears against typical character lighting. PuppetPlayer
	/// overrides team_color per-puppet at runtime via <c>SetInstanceShaderParameter("team_color", …)</c>
	/// on this MeshInstance3D, so every puppet can share the same baked mesh + material instance
	/// and the per-instance shader parameter does the per-team colour split for free.</summary>
	private ShaderMaterial BuildDefaultOutlineMaterialChain()
	{
		var debugColor = _glowColor;

		// Outline width is applied in the vertex shader as VERTEX += NORMAL * width * view_dist,
		// where VERTEX is in mesh-local space but view_dist is world-space metres. When the source
		// art is authored in centimetres (Skeleton3D scale ≈ 0.01) a world-space outline of 1 mm
		// would need width ≈ 0.1 in mesh-local — without compensation a default of 0.004 collapses
		// to 0.2 mm in world space, the inverted hull never extends past the lit body's surface,
		// and the rim disappears (user observation: "outline ist hinter dem körper"). Same fix
		// pattern as the welding epsilon — divide by the detected skeleton scale.
		//
		// Use _bakeSkeleton (set in StartBake, still alive at this point in WeldAndCommit) rather
		// than GetParent() — matches the working WeldVerts pattern. GetParent() in [Tool] script
		// editor context can return null if the scene tree isn't fully ready, leaving worldScale=1
		// (no scaling) → outline_width_base sticks at 0.004 → invisible rim on cm-authored meshes.
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

		// Inner thin solid rim — always-on, no distance ramp. Vertex push scales with view_dist
		// for constant screen-pixel thickness.
		// All alphas scale by GlowStartAlpha — global opacity dial that retunes the inner rim
		// AND the proportional fall-off through every shell at once.
		float startAlpha = Mathf.Clamp(GlowStartAlpha, 0f, 1f);

		// Layer 1: inner sharp rim, width = OutlineWidth, alpha = startAlpha.
		var outline = new ShaderMaterial { Shader = OutlineShader };
		outline.SetShaderParameter("team_color", debugColor);
		outline.SetShaderParameter("outline_width_base", OutlineWidth * widthScale);
		outline.SetShaderParameter("alpha", startAlpha);

		// Layer 2: the second main visible band at SecondLayerWidth, still high alpha. This is
		// the primary "glow body" the user sees before the fade tail starts.
		var secondLayer = new ShaderMaterial { Shader = OutlineDistanceShader };
		secondLayer.SetShaderParameter("team_color", debugColor);
		secondLayer.SetShaderParameter("outline_width_base", SecondLayerWidth * widthScale);
		secondLayer.SetShaderParameter("alpha", 0.55f * startAlpha);
		outline.NextPass = secondLayer;

		// Layer 3..N+2: fade tail shells from SecondLayerWidth out to GlowMaxWidth. Linear width
		// spacing, alpha curve pow(1-t, 1.5) so it fades quickly toward the outermost shell.
		// Uniform alpha per shell (no per-fragment gradient) → no triangle artifacts.
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

		// Wallhack RIM (not fill, CS:GO / TF2 style) — renders only the fresnel-style silhouette
		// edge through walls, suppressed on visible side. Appended at end of chain after the
		// visible-side rim + halo shells.
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

	/// <summary>Walks the MaterialOverride next_pass chain and pushes <see cref="_glowColor"/>
	/// onto every shell's <c>team_color</c> uniform. Called from the GlowColor setter so the
	/// editor 3D viewport updates immediately when the colour is tweaked — no re-bake required.
	/// Per-puppet runtime overrides via SetInstanceShaderParameter still win since instance
	/// parameters take precedence over material uniforms.</summary>
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

	/// <summary>Collects every visible MeshInstance3D under the parent Skeleton3D. The intent is
	/// "anything bone-related goes in the silhouette" — no name filter, no opt-in list. Skips:
	///   • This baker node itself (so re-bake doesn't fold the previous bake's mesh back in).
	///   • Anything with <see cref="CanvasItem.Visible"/> = false on the mesh or any ancestor —
	///     useful when the puppet ships multiple costume variants (only the live one bakes).
	///   • Anything explicitly listed in <see cref="ExcludedMeshes"/> (resolved fresh per bake).
	///   • Non-skinned surfaces are filtered downstream by <see cref="AppendMeshSurfaces"/>'s
	///     bones-per-vertex check, which warns and skips surfaces with no bone weights.
	/// </summary>
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
