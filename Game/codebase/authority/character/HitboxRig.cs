using Godot;
using System.Collections.Generic;

/// <summary>
/// Per-bone hitbox rig. Scans the <see cref="Skeleton"/> for <see cref="Hitbox"/> children
/// (typically under BoneAttachment3D nodes) and registers their RIDs for self-exclude and
/// damage-hitscan.
///
/// Primary workflow: the user places hitboxes as scene nodes in the weapon scene under the
/// TpsSkeleton (BoneAttachment3D -> Hitbox -> CollisionShape3D), tuning position, rotation,
/// shape, DamageMul and Label in the 3D editor. The code finds them at runtime and configures
/// the layer.
///
/// Fallback: if the skeleton has NO hitboxes, the rig spawns a default set
/// (head/chest/waist/2 thighs/2 calves) at runtime so the game works out of the box. As soon
/// as scene hitboxes exist, the fallback is automatically skipped.
///
/// Own layer (<see cref="Layer"/> = layer 4): body capsules are 2/3, so no cross-push.
/// </summary>
public partial class HitboxRig : Node
{
	/// <summary>Layer 3 - all player hitboxes. ServerAgents were moved to layer 5 (bit 4) so layer
	/// 3 is free (per user constraint). Body capsules (Char layer 2 / ServerAgent layer 5) do not
	/// collide with hitboxes (mask=0 on the hitbox side).</summary>
	public const uint Layer = 1u << 2;

	[Export] public Skeleton3D Skeleton;

	private readonly List<Rid> _rids = new();
	/// <summary>RIDs of all registered hitboxes - used by the hitscan to exclude self-hits.</summary>
	public IReadOnlyList<Rid> Rids => _rids;

	private readonly List<Hitbox> _hitboxNodes = new();
	/// <summary>Hitbox-Node-Refs in derselben Order wie <see cref="Rids"/> — gebraucht für Bone-Pose-
	/// Lag-Comp (snapshot + rewind/restore von GlobalTransform pro Tick).</summary>
	public IReadOnlyList<Hitbox> HitboxNodes => _hitboxNodes;

	private readonly List<CollisionShape3D> _collisionShapes = new();
	/// <summary>CollisionShape3D-Refs parallel zu <see cref="HitboxNodes"/>. Wichtig für Lag-Comp
	/// History + Debug-Marker: die Shape sitzt nicht am Hitbox-Origin (= Bone-Position) sondern hat
	/// einen lokalen Offset (Auto-Orient packt Capsules in die Mitte zwischen Bone und Child-Bone).
	/// Marker müssen <see cref="CollisionShape3D.GlobalTransform"/> nutzen damit sie wirklich auf
	/// der Collision-Shape sitzen statt am Bone-Joint.</summary>
	public IReadOnlyList<CollisionShape3D> CollisionShapes => _collisionShapes;

	/// <summary>Scans and optionally spawns the fallback set. Call AFTER Skeleton._Ready, otherwise bone indices are -1.</summary>
	/// <summary>Scans the authored hitbox nodes and registers their RIDs. When <paramref name="skipAutoOrient"/>
	/// is true the capsules are assumed pre-baked in the editor (NetworkPlayer.BakeHitboxes) and the runtime
	/// auto-orient pass is skipped — deterministic + no per-spawn cost.</summary>
	public void Build(bool skipAutoOrient = false)
	{
		if (Skeleton == null)
		{
			GD.PushWarning("[HitboxRig] Skeleton not assigned -> no hitboxes");
			return;
		}
		_rids.Clear();

		ScanForHitboxes(Skeleton);

		if (_rids.Count == 0)
		{
			Dbg.Print("[HitboxRig] No scene hitboxes found -> spawning default set at runtime");
			SpawnDefaults();
		}
		else
		{
			Dbg.Print($"[HitboxRig] {_rids.Count} scene hitboxes registered");
		}

		if (!skipAutoOrient)
		{
			AutoOrientFromBoneChildren();
			AutoSizeFromMesh();
		}

		Node ownerNode = Skeleton;
		while (ownerNode != null && ownerNode is not NetworkPlayer) ownerNode = ownerNode.GetParent();
		string ownerInfo = ownerNode is NetworkPlayer owner ? $"owner={owner.GetType().Name} netId={owner.NetId}" : "owner=null";
		Dbg.Print($"[HitboxRig] Build complete: {_rids.Count} hitboxes, skel={Skeleton.GetPath()} {ownerInfo}");
	}

	/// <summary>Computes the direction from each hitbox bone to its first child bone and orients
	/// the CollisionShape along this direction (capsule-Y -> bone-to-child). Overwrites any
	/// scene-side transforms, so it works for any rig (mannequin, custom, mirrored L/R) without
	/// hand-tuning. For bones without a child (e.g. head) the shape stays at the bone origin
	/// (irrelevant for spheres).</summary>
	/// <summary>Positions + sizes each authored capsule between its bone and the first child bone (rest pose).
	/// Run at runtime by <see cref="Build"/> (unless pre-baked) or once in the editor by the
	/// NetworkPlayer.BakeHitboxes button, which saves the result into the scene.</summary>
	/// <summary>Recursively collects every BoneAttachment3D under the skeleton (handles both loose children
	/// and the "Hitboxes" container).</summary>
	private static System.Collections.Generic.IEnumerable<BoneAttachment3D> CollectBoneAttachments(Node root)
	{
		foreach (Node c in root.GetChildren())
		{
			if (c is BoneAttachment3D a) yield return a;
			else foreach (var nested in CollectBoneAttachments(c)) yield return nested;
		}
	}

	public void AutoOrientFromBoneChildren()
	{
		if (Skeleton == null) return;

		// Detect the rig's unit scale. The default spec sizes are authored in centimetres (UE convention), but
		// this rig's bones may be in metres (or any scale). Bones that get no mesh fit (hand, foot, clavicle, …)
		// would otherwise keep cm-scale defaults and render ~100× too big. The default capsule heights were
		// authored as ~cm bone lengths, so ratio = measured-bone-length / default-height ≈ the scale; the median
		// over the limb capsules is robust to the few specs whose default isn't a literal bone length.
		float rigScale = DetectRigScale();

		foreach (BoneAttachment3D attach in CollectBoneAttachments(Skeleton))
		{
			int boneIdx = Skeleton.FindBone(attach.BoneName);
			if (boneIdx < 0) continue;

			Hitbox hb = null;
			foreach (Node ch in attach.GetChildren())
				if (ch is Hitbox h) { hb = h; break; }
			if (hb == null) continue;
			CollisionShape3D cs = null;
			foreach (Node ch in hb.GetChildren())
				if (ch is CollisionShape3D c) { cs = c; break; }
			if (cs == null || cs.Shape == null) continue;

			Transform3D boneRest = Skeleton.GetBoneGlobalRest(boneIdx);
			Vector3? childPosWorld = FindFirstChildBoneRestOrigin(boneIdx);

			if (cs.Shape is BoxShape3D box)
			{
				// Box default size is in centimetres; bring it into the rig's unit scale. AutoSize overrides this
				// for boxes whose bone has mesh verts (chest, pelvis, arms); the rest keep this geometric box.
				Vector3 defScaled = box.Size * rigScale;

				// Head (or any leaf without a usable child): leave the box axis-aligned to the bone-local frame —
				// that frame is body-aligned, and AutoSize centres + sizes it from the bone's verts.
				if (hb.Group == HitboxGroup.Head || !childPosWorld.HasValue)
				{
					box.Size = defScaled;
					continue;
				}

				// Orient the box LENGTH (local Y) along the bone→child direction (same basis the capsule uses).
				// A bone-local axis-aligned box balloons when the bone runs diagonally through its local frame —
				// the AABB then has to enclose the diagonal limb. AutoSize refits width/depth (local X/Z) from the
				// verts in THIS oriented frame so the box hugs the limb. X/Z default = scaled cross-section.
				Vector3 worldDir = childPosWorld.Value - boneRest.Origin;
				float dist = worldDir.Length();
				if (dist < 0.001f) { box.Size = defScaled; continue; }

				bool boxExtremity = hb.Group == HitboxGroup.Foot || hb.Group == HitboxGroup.Hand;
				Vector3 dirLocal = (boneRest.Basis.Inverse() * worldDir).Normalized();
				Vector3 yAxis = dirLocal;
				Vector3 xAxis = yAxis.Cross(Vector3.Forward);
				if (xAxis.LengthSquared() < 0.01f) xAxis = yAxis.Cross(Vector3.Right);
				xAxis = xAxis.Normalized();
				Vector3 zAxis = xAxis.Cross(yAxis).Normalized();

				// Extremity: extend past the child (hand→fingers, foot→toes). Limb/torso segment: span bone→child.
				float length = boxExtremity ? dist * 2f : dist;
				box.Size = new Vector3(defScaled.X, length, defScaled.Z);
				float frac = boxExtremity ? 0.25f : 0.5f;
				cs.Transform = new Transform3D(new Basis(xAxis, yAxis, zAxis), dirLocal * (length * frac));
				continue;
			}

			if (cs.Shape is CapsuleShape3D capsule && childPosWorld.HasValue)
			{
				Vector3 worldDir = childPosWorld.Value - boneRest.Origin;
				float dist = worldDir.Length();
				if (dist < 0.001f) continue;

				bool isExtremity = hb.Group == HitboxGroup.Foot || hb.Group == HitboxGroup.Hand;

				// Bring the cm-authored default radius into the rig's unit scale before deriving the height from
				// it. AutoSize later overrides the radius for capsules whose bone has mesh verts; the others
				// (clavicle, hand, foot) keep this scaled default.
				capsule.Radius *= rigScale;

				// Auto-Length: Capsule-Höhe = volle bone-to-child Distanz. Die Hemisphere-Kappen ragen dann um
				// ~einen Radius über Bone- und Child-Origin hinaus, sodass benachbarte Capsules am Gelenk
				// (Knie, Ellbogen, Schulter) überlappen statt nur tangential zu enden → keine Gelenk-Lücken.
				// Hand/Foot: the bone→child span only reaches the first knuckle / the ball, so the fingers / toes
				// would be uncovered. Use a FIXED, bounded length (a multiple of the radius) laid along the
				// bone→child direction so it spans the whole hand / foot and can never explode on a stray child.
				float extremityLen = hb.Group == HitboxGroup.Hand ? 4f : 3.5f;
				float autoHeight = isExtremity
					? capsule.Radius * extremityLen
					: dist;
				capsule.Height = autoHeight;

				Vector3 dirLocal = (boneRest.Basis.Inverse() * worldDir).Normalized();

				Vector3 yAxis = dirLocal;
				Vector3 xAxis = yAxis.Cross(Vector3.Forward);
				if (xAxis.LengthSquared() < 0.01f) xAxis = yAxis.Cross(Vector3.Right);
				xAxis = xAxis.Normalized();
				Vector3 zAxis = xAxis.Cross(yAxis).Normalized();

				// Centre: limbs at the bone→child midpoint; hand/foot shifted forward by ~⅓ of its own (fixed)
				// length so the capsule reaches past the knuckles / ball into the fingers / toes.
				Vector3 centreOffset = isExtremity ? dirLocal * (autoHeight * 0.3f) : dirLocal * (dist * 0.5f);
				cs.Transform = new Transform3D(new Basis(xAxis, yAxis, zAxis), centreOffset);
			}
		}
	}

	/// <summary>Auto-Sizing aus dem Skin-Mesh: sammelt Vertices die zu jedem Skelett-Bone gewichtet
	/// sind (weight > 0.4) und fittet jede Capsule/Sphere tight um diese Vertices. Läuft einmal nach
	/// AutoOrient damit die Shape-Orientation steht. Skipped wenn kein skinned MeshInstance3D unter
	/// dem Skeleton oder Skin fehlt.
	///
	/// Vertex-Mapping: skin.GetBindPose(skinIdx) ist die Matrix Mesh→Bone (= bind-pose-inverse). Mit
	/// `bindPose * vertex` kriegen wir die Vertex-Position in Bone-Local-Space. Skin-Bone-Index wird
	/// via GetBindBone/GetBindName auf den Skeleton-Bone-Index gemappt.</summary>
	/// <summary>Cache der berechneten Hitbox-Sizes pro (bone-index). Gefüllt beim ersten erfolgreichen
	/// AutoSize-Run, danach werden alle Spawns daraus appliziert (= 0 Mesh-Parsing pro Spawn). Game
	/// nutzt ein einziges Character-Mesh, also reicht ein flacher Cache pro Bone-Index. Bei Future-
	/// Multi-Character müsste der Key auf (Mesh-UID, Bone-Index) erweitert werden.</summary>
	private struct CachedSize
	{
		public bool IsCapsule;
		public bool IsBox;
		public float Radius;
		public float Height;
		public Vector3 BoxSize;
		public Transform3D BoxTransform;   // box: full baked transform (oriented basis + centre)
		public Vector3 OriginShiftBoneSpace;
	}
	private static Dictionary<int, CachedSize> _sizeCache;

	private void AutoSizeFromMesh()
	{
		// Cache-Hit: spätere Spawns überspringen das gesamte Mesh-Parsing (~Tausende Vertices iterieren)
		// und appliziert nur die einmal berechneten Werte. Zero allocations, ~instant.
		if (_sizeCache != null)
		{
			int applied = ApplyCachedSizes();
			Dbg.Print($"[HitboxRig] AutoSize CACHED: {applied}/{_hitboxNodes.Count} hitboxes from cache");
			return;
		}

		// Character hat oft DUTZENDE skinned MeshInstance3D Nodes (Body in mehrere Teile gesplittet:
		// Body, Head, Gloves, Belt, Pouches, BackBanger, etc — alle teilen denselben Skin). Nur ein
		// Mesh zu nehmen würde nur DESSEN Bones abdecken. Sammle daher Vertices aus ALLEN sichtbaren
		// skinned MeshInstances (Variant-Meshes wie BackBanger_A/B/D haben nur EINEN aktiv via Visible).
		var allMeshes = new List<MeshInstance3D>();
		CollectAllSkinnedRecursive(Skeleton.GetParent() ?? Skeleton, allMeshes);
		if (allMeshes.Count == 0)
		{
			Dbg.Print("[HitboxRig] AutoSize SKIPPED — no visible skinned MeshInstance3D found");
			return;
		}
		Dbg.Print($"[HitboxRig] AutoSize START — {allMeshes.Count} visible skinned meshes, merging (cache will be populated for next spawns)");

		var vertsPerBone = new Dictionary<int, List<Vector3>>();
		int totalVerts = 0;
		foreach (var mi in allMeshes)
		{
			var mesh = mi.Mesh;
			var skin = mi.Skin;
			int bindCount = skin.GetBindCount();
			var skinToSkel = new int[bindCount];
			for (int i = 0; i < bindCount; i++)
			{
				int sb = skin.GetBindBone(i);
				if (sb < 0)
				{
					string n = skin.GetBindName(i);
					sb = !string.IsNullOrEmpty(n) ? Skeleton.FindBone(n) : -1;
				}
				skinToSkel[i] = sb;
			}

			for (int s = 0; s < mesh.GetSurfaceCount(); s++)
			{
				var arrays = mesh.SurfaceGetArrays(s);
				var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
				var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
				var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
				if (verts.Length == 0 || bones.Length == 0 || weights.Length == 0) continue;
				int bonesPerVertex = bones.Length / verts.Length;
				if (bonesPerVertex < 1 || bonesPerVertex > 8) continue;
				totalVerts += verts.Length;

				for (int v = 0; v < verts.Length; v++)
				{
					for (int b = 0; b < bonesPerVertex; b++)
					{
						int idx = v * bonesPerVertex + b;
						float w = weights[idx];
						if (w < 0.4f) continue;
						int skinIdx = bones[idx];
						if (skinIdx < 0 || skinIdx >= bindCount) continue;
						int skelBoneIdx = skinToSkel[skinIdx];
						if (skelBoneIdx < 0) continue;

						Transform3D bindPose = skin.GetBindPose(skinIdx);
						Vector3 boneLocal = bindPose * verts[v];

						if (!vertsPerBone.TryGetValue(skelBoneIdx, out var list))
						{
							list = new List<Vector3>();
							vertsPerBone[skelBoneIdx] = list;
						}
						list.Add(boneLocal);
					}
				}
			}
		}
		Dbg.Print($"[HitboxRig] AutoSize MERGED {totalVerts} verts across {allMeshes.Count} meshes → {vertsPerBone.Count} bones covered");

		// Per-Hitbox: BoneAttachment3D-Parent gibt Bone-Index. Vertices fittten + Cache füllen.
		var newCache = new Dictionary<int, CachedSize>();
		int resized = 0;
		for (int h = 0; h < _hitboxNodes.Count; h++)
		{
			var hb = _hitboxNodes[h];
			var cs = _collisionShapes[h];
			if (hb == null || cs?.Shape == null) continue;
			if (hb.GetParent() is not BoneAttachment3D attach) continue;
			int boneIdx = Skeleton.FindBone(attach.BoneName);
			if (boneIdx < 0) continue;

			if (!vertsPerBone.TryGetValue(boneIdx, out var bvs) || bvs.Count < 8) continue;

			// Transform vertices from Bone-Local nach Shape-Local (= inverse cs.Transform). cs ist child
			// vom Hitbox der am Bone-Origin sitzt → cs's bone-local-frame = cs.Transform.
			Transform3D shapeLocalFromBone = cs.Transform.AffineInverse();

			if (cs.Shape is SphereShape3D sph)
			{
				// Center the sphere on the centroid of the bone's weighted vertices (the real head mass).
				// The head bone origin sits at the skull base, so a bone-origin-centered sphere bleeds into
				// the neck. Radius = 90th-percentile distance from that centroid (outlier-resistant vs the
				// helmet/hair tips that would otherwise double the sphere size).
				Vector3 centroid = Vector3.Zero;
				foreach (var bv in bvs) centroid += bv;
				centroid /= bvs.Count;

				var dists = new List<float>(bvs.Count);
				foreach (var bv in bvs)
					dists.Add((bv - centroid).Length());
				dists.Sort();
				float p90 = dists[(int)(dists.Count * 0.90f)];
				if (p90 > 0.001f)
				{
					cs.Transform = new Transform3D(Basis.Identity, centroid);
					sph.Radius = p90;
					newCache[boneIdx] = new CachedSize { IsCapsule = false, Radius = p90, OriginShiftBoneSpace = centroid };
					resized++;
				}
			}
			else if (cs.Shape is CapsuleShape3D cap)
			{
				// HÖHE behält auto-orient (= bone-to-child Distance, anatomically korrekt). Radius
				// kommt aus dem Mesh-Fit (90th-Percentile XZ-Thickness). Sonst werden Capsules zu
				// Sphären weil strong-weighted Vertices pro Bone lokal sind (Skin-Falloff um den
				// Joint herum), die Y-Range im Skin viel kleiner ist als die echte Bone-Länge.
				var radii = new List<float>(bvs.Count);
				foreach (var bv in bvs)
				{
					Vector3 lv = shapeLocalFromBone * bv;
					float r = Mathf.Sqrt(lv.X * lv.X + lv.Z * lv.Z);
					radii.Add(r);
				}
				radii.Sort();
				float p90 = radii[(int)(radii.Count * 0.90f)];
				if (p90 > 0.001f)
				{
					cap.Radius = p90;
					// Höhe + Origin bleiben wie auto-orient sie gesetzt hat.
					newCache[boneIdx] = new CachedSize { IsCapsule = true, Radius = p90, Height = cap.Height, OriginShiftBoneSpace = Vector3.Zero };
					resized++;
				}
			}
			else if (cs.Shape is BoxShape3D box)
			{
				// Tight oriented box straight from the mesh — the "compute width/length/height from the verts"
				// fit. Length stays along the bone (local Y, set by AutoOrient); width + depth come from a 2D PCA
				// of the cross-section so the box aligns with the limb's REAL width/thickness, not arbitrary axes.
				// This is what stops a limb box from ballooning when the bone runs diagonally through its frame.
				var lv = new List<Vector3>(bvs.Count);
				foreach (var bv in bvs) lv.Add(shapeLocalFromBone * bv);

				// 2D PCA on the cross-section (local X,Z): principal rotation θ about the length axis (local Y).
				float mx = 0f, mz = 0f;
				foreach (var p in lv) { mx += p.X; mz += p.Z; }
				mx /= lv.Count; mz /= lv.Count;
				float cxx = 0f, cxz = 0f, czz = 0f;
				foreach (var p in lv) { float dx = p.X - mx, dz = p.Z - mz; cxx += dx * dx; cxz += dx * dz; czz += dz * dz; }
				float theta = 0.5f * Mathf.Atan2(2f * cxz, cxx - czz);

				Basis crossRot = new Basis(Vector3.Up, theta);
				Basis crossRotInv = crossRot.Transposed();   // rotation inverse = transpose
				var rv = new List<Vector3>(lv.Count);
				foreach (var p in lv) rv.Add(crossRotInv * p);

				Vector3 rmin = new Vector3(AxisPercentile(rv, 0, 0.05f), AxisPercentile(rv, 1, 0.05f), AxisPercentile(rv, 2, 0.05f));
				Vector3 rmax = new Vector3(AxisPercentile(rv, 0, 0.95f), AxisPercentile(rv, 1, 0.95f), AxisPercentile(rv, 2, 0.95f));
				Vector3 bsize = rmax - rmin;
				Vector3 rcenter = (rmin + rmax) * 0.5f;
				if (bsize.LengthSquared() > 0.0001f)
				{
					Basis finalBasis = cs.Transform.Basis * crossRot;
					var finalXf = new Transform3D(finalBasis, cs.Transform.Origin + finalBasis * rcenter);
					box.Size = bsize;
					cs.Transform = finalXf;
					newCache[boneIdx] = new CachedSize { IsBox = true, BoxSize = bsize, BoxTransform = finalXf };
					resized++;
				}
			}
		}
		_sizeCache = newCache;
		Dbg.Print($"[HitboxRig] AutoSize DONE: {resized}/{_hitboxNodes.Count} hitboxes resized. Cache populated with {newCache.Count} bone entries — subsequent spawns reuse.");
	}

	/// <summary>Wendet die gecachten Werte aus <see cref="_sizeCache"/> auf diese Rig-Instanz an.
	/// Genutzt für Spawn #2+ wenn das Mesh-Parsing schon einmal lief. Returnt die Anzahl applied.</summary>
	private int ApplyCachedSizes()
	{
		int applied = 0;
		for (int h = 0; h < _hitboxNodes.Count; h++)
		{
			var hb = _hitboxNodes[h];
			var cs = _collisionShapes[h];
			if (hb == null || cs?.Shape == null) continue;
			if (hb.GetParent() is not BoneAttachment3D attach) continue;
			int boneIdx = Skeleton.FindBone(attach.BoneName);
			if (!_sizeCache.TryGetValue(boneIdx, out var spec)) continue;

			if (!spec.IsCapsule && !spec.IsBox && cs.Shape is SphereShape3D sph)
			{
				sph.Radius = spec.Radius;
				cs.Transform = new Transform3D(Basis.Identity, spec.OriginShiftBoneSpace);   // centroid (absolute, not additive)
				applied++;
			}
			else if (spec.IsCapsule && cs.Shape is CapsuleShape3D cap)
			{
				cap.Radius = spec.Radius;
				cap.Height = spec.Height;
				cs.Transform = new Transform3D(cs.Transform.Basis, cs.Transform.Origin + spec.OriginShiftBoneSpace);
				applied++;
			}
			else if (spec.IsBox && cs.Shape is BoxShape3D box)
			{
				box.Size = spec.BoxSize;
				cs.Transform = spec.BoxTransform;   // full baked transform (oriented basis + centre)
				applied++;
			}
		}
		return applied;
	}

	/// <summary>Sammelt ALLE skinned MeshInstance3D Nodes mit lokaler Visible=true. Charakter hat
	/// Variant-Meshes (SK_BackBanger_A/B/D etc) wo nur EINER eingeblendet ist — die unsichtbaren
	/// Varianten würden falsche Vertices beitragen (andere Geometrie an gleichen Bones). Local
	/// Visible-Flag statt IsVisibleInTree weil der Agent-Root auf Server unsichtbar ist aber
	/// die Body-Parts trotzdem als "aktiv" markiert sein können.</summary>
	private static void CollectAllSkinnedRecursive(Node n, List<MeshInstance3D> outList)
	{
		if (n is MeshInstance3D mi && mi.Skin != null && mi.Mesh != null && mi.Visible)
			outList.Add(mi);
		foreach (Node ch in n.GetChildren()) CollectAllSkinnedRecursive(ch, outList);
	}

	/// <summary>Detects the rig's unit scale relative to the centimetre-authored default specs. For every limb
	/// capsule it compares the measured bone→child distance against the spec's default height (≈ a cm bone
	/// length) and returns the median ratio. 1.0 when nothing can be measured (assume cm). The foot is excluded
	/// (its default height is not a bone length).</summary>
	private float DetectRigScale()
	{
		var samples = new List<float>();
		foreach (BoneAttachment3D attach in CollectBoneAttachments(Skeleton))
		{
			int bi = Skeleton.FindBone(attach.BoneName);
			if (bi < 0) continue;
			Hitbox hb = null;
			foreach (Node ch in attach.GetChildren()) if (ch is Hitbox h) { hb = h; break; }
			if (hb == null || hb.Group == HitboxGroup.Foot) continue;
			CollisionShape3D cs = null;
			foreach (Node ch in hb.GetChildren()) if (ch is CollisionShape3D c) { cs = c; break; }
			if (cs?.Shape is not CapsuleShape3D cap || cap.Height < 0.0001f) continue;
			Vector3? child = FindFirstChildBoneRestOrigin(bi);
			if (!child.HasValue) continue;
			float d = (child.Value - Skeleton.GetBoneGlobalRest(bi).Origin).Length();
			if (d > 0.0001f) samples.Add(d / cap.Height);
		}
		if (samples.Count == 0)
		{
			Dbg.Print("[HitboxRig] Rig scale not measurable -> assuming cm (×1.0)");
			return 1f;
		}
		samples.Sort();
		float scale = samples[samples.Count / 2];
		Dbg.Print($"[HitboxRig] Detected rig scale ×{scale:0.0000} from {samples.Count} limb capsules");
		return scale;
	}

	/// <summary>Returns the <paramref name="t"/>-percentile (0..1) of the given vert component (axis 0=X,1=Y,2=Z).
	/// Used for outlier-resistant box fitting — full min/max would catch stray skin-falloff verts.</summary>
	private static float AxisPercentile(List<Vector3> verts, int axis, float t)
	{
		var vals = new List<float>(verts.Count);
		foreach (var v in verts) vals.Add(axis == 0 ? v.X : axis == 1 ? v.Y : v.Z);
		vals.Sort();
		int i = Mathf.Clamp((int)(vals.Count * t), 0, vals.Count - 1);
		return vals[i];
	}

	/// <summary>Returns the global rest origin of the first child bone of the given bone, or null if none.</summary>
	private Vector3? FindFirstChildBoneRestOrigin(int boneIdx)
	{
		// Skip twist / roll / helper / tip bones — they sit ALONG a segment (e.g. lowerarm_twist mid-forearm),
		// so picking them as "the child" makes the capsule end early. We want the real next joint (the elbow
		// for the upper arm, the wrist for the lower arm), so the capsule spans the full segment.
		int count = Skeleton.GetBoneCount();
		for (int i = 0; i < count; i++)
		{
			if (Skeleton.GetBoneParent(i) != boneIdx) continue;
			string name = Skeleton.GetBoneName(i).ToLowerInvariant();
			if (name.Contains("twist") || name.Contains("roll") || name.EndsWith("_end") || name.EndsWith("_tip")) continue;
			return Skeleton.GetBoneGlobalRest(i).Origin;
		}
		return null;
	}

	/// <summary>Recursively scans the given subtree and registers every <see cref="Hitbox"/> RID + Node.</summary>
	private void ScanForHitboxes(Node root)
	{
		foreach (Node n in root.GetChildren())
		{
			if (n is Hitbox hb)
			{
				_rids.Add(hb.GetRid());
				_hitboxNodes.Add(hb);
				CollisionShape3D cs = null;
				foreach (Node ch in hb.GetChildren()) if (ch is CollisionShape3D c) { cs = c; break; }
				_collisionShapes.Add(cs);
			}
			ScanForHitboxes(n);
		}
	}

	/// <summary>Fallback specification for a runtime-spawned default hitbox.</summary>
	private class Spec
	{
		public string BoneName;
		public HitboxGroup Group;
		public Shape3D Shape;
	}

	/// <summary>Returns the static fallback hitbox specification array (head + chest + waist + arms + legs + feet).</summary>
	private static Spec[] DefaultSpecs() => new Spec[]
	{
		// Head: a box — AutoSize fits it per-axis (length/width/depth) from the skull verts and centres it on the
		// skull mass (the head bone origin sits at the skull base, so a bone-origin shape would bleed into the neck).
		new Spec { BoneName = "head", Group = HitboxGroup.Head,
			Shape = new BoxShape3D { Size = new Vector3(15f, 18f, 20f) } },
		// Torso: boxes (a torso is wider than it is deep — independent width/depth, not a round tube).
		// spine_03 + pelvis carry mesh verts → AutoSize fits them per-axis (body-aligned, tight). The spine
		// subdivisions have no verts → AutoOrient gives them a geometric filler box (length along the spine,
		// square cross-section so the unknown width/depth axis can't be swapped wrong).
		new Spec { BoneName = "spine_03", Group = HitboxGroup.Chest,
			Shape = new BoxShape3D { Size = new Vector3(36f, 30f, 24f) } },
		new Spec { BoneName = "pelvis", Group = HitboxGroup.Waist,
			Shape = new BoxShape3D { Size = new Vector3(32f, 24f, 24f) } },
		new Spec { BoneName = "spine_01", Group = HitboxGroup.Waist,
			Shape = new BoxShape3D { Size = new Vector3(28f, 28f, 28f) } },
		new Spec { BoneName = "spine_02", Group = HitboxGroup.Chest,
			Shape = new BoxShape3D { Size = new Vector3(28f, 28f, 28f) } },
		new Spec { BoneName = "spine_04", Group = HitboxGroup.Chest,
			Shape = new BoxShape3D { Size = new Vector3(28f, 28f, 28f) } },
		new Spec { BoneName = "spine_05", Group = HitboxGroup.Chest,
			Shape = new BoxShape3D { Size = new Vector3(28f, 28f, 28f) } },
		// Arms: boxes — a capsule's round cross-section forces depth = width, so it's too thick on an arm that
		// is wider than it is deep. A box fits width/depth independently. upperarm/lowerarm have verts → per-axis
		// fit; clavicle has none → geometric box (length along the bone, square cross-section).
		new Spec { BoneName = "clavicle_l", Group = HitboxGroup.Arm,
			Shape = new BoxShape3D { Size = new Vector3(7f, 14f, 7f) } },
		new Spec { BoneName = "clavicle_r", Group = HitboxGroup.Arm,
			Shape = new BoxShape3D { Size = new Vector3(7f, 14f, 7f) } },
		new Spec { BoneName = "upperarm_l", Group = HitboxGroup.Arm,
			Shape = new BoxShape3D { Size = new Vector3(12f, 28f, 12f) } },
		new Spec { BoneName = "upperarm_r", Group = HitboxGroup.Arm,
			Shape = new BoxShape3D { Size = new Vector3(12f, 28f, 12f) } },
		new Spec { BoneName = "lowerarm_l", Group = HitboxGroup.Arm,
			Shape = new BoxShape3D { Size = new Vector3(10f, 26f, 10f) } },
		new Spec { BoneName = "lowerarm_r", Group = HitboxGroup.Arm,
			Shape = new BoxShape3D { Size = new Vector3(10f, 26f, 10f) } },
		// Hand: a box along wrist→finger (AutoOrient extends it past the knuckles to span the fingers). No mesh
		// verts on the hand bone → geometric; square cross-section so the unknown width/thickness axis is safe.
		new Spec { BoneName = "hand_l", Group = HitboxGroup.Hand,
			Shape = new BoxShape3D { Size = new Vector3(7f, 16f, 7f) } },
		new Spec { BoneName = "hand_r", Group = HitboxGroup.Hand,
			Shape = new BoxShape3D { Size = new Vector3(7f, 16f, 7f) } },
		new Spec { BoneName = "thigh_l", Group = HitboxGroup.Leg,
			Shape = new CapsuleShape3D { Radius = 11f, Height = 42f } },
		new Spec { BoneName = "thigh_r", Group = HitboxGroup.Leg,
			Shape = new CapsuleShape3D { Radius = 11f, Height = 42f } },
		new Spec { BoneName = "calf_l", Group = HitboxGroup.Leg,
			Shape = new CapsuleShape3D { Radius = 9f, Height = 42f } },
		new Spec { BoneName = "calf_r", Group = HitboxGroup.Leg,
			Shape = new CapsuleShape3D { Radius = 9f, Height = 42f } },
		// Foot: a box along ankle→ball (AutoOrient extends it forward to cover the heel + toes). No mesh verts
		// on the foot bone → geometric; square cross-section so the unknown width/height axis is safe.
		new Spec { BoneName = "foot_l", Group = HitboxGroup.Foot,
			Shape = new BoxShape3D { Size = new Vector3(9f, 24f, 9f) } },
		new Spec { BoneName = "foot_r", Group = HitboxGroup.Foot,
			Shape = new BoxShape3D { Size = new Vector3(9f, 24f, 9f) } },
	};

	/// <summary>Spawns the fallback default hitbox set under the skeleton at runtime.</summary>
	/// <summary>Editor entry point: generates the default hitbox set (BoneAttachment3D → Hitbox → capsule per
	/// major bone) if none are authored yet, then positions/sizes the capsules between bone+child from the
	/// rest pose. Pass the edited scene root as <paramref name="owner"/> so the created nodes are saved.</summary>
	public void BakeDefaultHitboxes(Node owner, Node container = null, IReadOnlyDictionary<string, string> boneRemap = null)
	{
		if (Skeleton == null) return;
		_sizeCache = null;   // editor re-bake always recomputes from the mesh (the static cache is a runtime-respawn optimisation)
		_rids.Clear(); _hitboxNodes.Clear(); _collisionShapes.Clear();

		// Re-bake: wipe the existing hitboxes from the target container so changed bone/size assignments
		// take effect cleanly (no leftover stale capsules).
		Node3D target = (container as Node3D) ?? Skeleton.GetNodeOrNull<Node3D>("Hitboxes");
		if (target != null)
			foreach (Node c in target.GetChildren())
				c.Free();

		SpawnDefaults(owner, container, boneRemap);
		AutoOrientFromBoneChildren();
		AutoSizeFromMesh();
	}

	private void SpawnDefaults(Node owner = null, Node providedContainer = null, IReadOnlyDictionary<string, string> remap = null)
	{
		// All hitboxes live under one container (not loose under the skeleton). Since a BoneAttachment3D only
		// auto-binds when it is a DIRECT child of a Skeleton3D, each attachment gets an external-skeleton ref
		// computed back to the skeleton, so the container may live anywhere in the tree.
		Node3D container = providedContainer as Node3D
			?? Skeleton.GetNodeOrNull<Node3D>("Hitboxes")
			?? new Node3D { Name = "Hitboxes" };
		if (container.GetParent() == null)
		{
			Skeleton.AddChild(container);
			if (owner != null) container.Owner = owner;
		}

		int built = 0;
		foreach (var spec in DefaultSpecs())
		{
			string boneName = remap != null && remap.TryGetValue(spec.BoneName, out var mapped) && !string.IsNullOrEmpty(mapped)
				? mapped : spec.BoneName;
			int boneIdx = Skeleton.FindBone(boneName);
			if (boneIdx < 0)
			{
				GD.PushWarning($"[HitboxRig] Bone '{boneName}' not found -> hitbox '{spec.Group}' skipped");
				continue;
			}

			var attach = new BoneAttachment3D { Name = $"hb_attach_{boneName}" };
			container.AddChild(attach);
			attach.SetUseExternalSkeleton(true);
			attach.SetExternalSkeleton(attach.GetPathTo(Skeleton));
			attach.BoneName = boneName;   // sets the name (+ resolves bone_idx now the skeleton is wired)

			var body = new Hitbox
			{
				Name = $"hb_{spec.Group}_{spec.BoneName}",
				Group = spec.Group,
			};
			attach.AddChild(body);

			var cs = new CollisionShape3D { Shape = spec.Shape };
			body.AddChild(cs);

			// Editor bake: parent the created nodes to the edited scene so they're persisted on save.
			if (owner != null)
			{
				attach.Owner = owner;
				body.Owner = owner;
				cs.Owner = owner;
			}

			_rids.Add(body.GetRid());
			_hitboxNodes.Add(body);
			_collisionShapes.Add(cs);
			built++;
		}
		Dbg.Print($"[HitboxRig] {(owner != null ? "Editor-baked" : "Fallback-spawned")} {built}/{DefaultSpecs().Length} hitboxes");
	}

	/// <summary>Reads the hitbox group (Head/Chest/Waist/Arm/Leg/Hand/Foot). Defaults to <see cref="HitboxGroup.Body"/>
	/// when the collider isn't a Hitbox (e.g. world geometry).</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	public static HitboxGroup ReadGroup(Node3D hitCollider) => hitCollider is Hitbox hb ? hb.Group : HitboxGroup.Body;

	/// <summary>Walks the parent chain from the hitbox collider upwards until a <see cref="NetworkPlayer"/>
	/// is found. Path: Hitbox -> BoneAttachment3D -> Skeleton3D -> ... -> NetworkPlayer/PuppetPlayer/
	/// ServerPlayer/ServerBotPlayer owner. NetworkPlayer is the common ancestor, so this works for
	/// all character variants.</summary>
	public static NetworkPlayer FindOwner(Node3D hitCollider)
	{
		Node n = hitCollider;
		while (n != null)
		{
			if (n is NetworkPlayer bc) return bc;
			n = n.GetParent();
		}
		return null;
	}
}
