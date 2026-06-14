using Godot;
using System.Collections.Generic;

namespace Vantix.Character;

/// <summary>
/// Per-bone hitbox rig. Scans the <see cref="Skeleton"/> for <see cref="Hitbox"/> children and registers
/// their RIDs for self-exclude and damage-hitscan. Authored hitboxes (BoneAttachment3D → Hitbox →
/// CollisionShape3D) are found at runtime; if none exist, a default set is spawned. Hitboxes sit on
/// <see cref="Layer"/> with mask 0, so they never collide with body capsules.
/// </summary>
public partial class HitboxRig : Node
{
	/// <summary>Layer 3 — all player hitboxes. Body capsules don't collide with hitboxes (hitbox mask=0).</summary>
	public const uint Layer = 1u << 2;

	[Export] public Skeleton3D Skeleton;

	private readonly List<Rid> _rids = new();
	/// <summary>RIDs of all registered hitboxes - used by the hitscan to exclude self-hits.</summary>
	public IReadOnlyList<Rid> Rids => _rids;

	private readonly List<Hitbox> _hitboxNodes = new();
	/// <summary>Hitbox node refs in same order as <see cref="Rids"/> — used for bone pose lag-comp
	/// (snapshot + rewind/restore of GlobalTransform per tick).</summary>
	public IReadOnlyList<Hitbox> HitboxNodes => _hitboxNodes;

	private readonly List<CollisionShape3D> _collisionShapes = new();
	/// <summary>CollisionShape3D refs parallel to <see cref="HitboxNodes"/>. The shape sits at a local
	/// offset from the hitbox origin (auto-orient places capsules at the bone-to-child midpoint), so
	/// lag-comp and markers must use <see cref="CollisionShape3D.GlobalTransform"/>.</summary>
	public IReadOnlyList<CollisionShape3D> CollisionShapes => _collisionShapes;

	/// <summary>Scans authored hitboxes (or spawns the fallback set) and registers their RIDs. Call after
	/// Skeleton._Ready, else bone indices are -1. <paramref name="skipAutoOrient"/> skips the runtime
	/// orient/size pass when capsules are pre-baked in the editor.</summary>
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

	/// <summary>Recursively collects every BoneAttachment3D under the skeleton (loose children and the
	/// "Hitboxes" container).</summary>
	private static System.Collections.Generic.IEnumerable<BoneAttachment3D> CollectBoneAttachments(Node root)
	{
		foreach (Node c in root.GetChildren())
		{
			if (c is BoneAttachment3D a) yield return a;
			else foreach (var nested in CollectBoneAttachments(c)) yield return nested;
		}
	}

	/// <summary>Orients each CollisionShape along its bone-to-child direction (capsule-Y → bone-to-child),
	/// overwriting scene transforms so it works for any rig. Bones without a child keep the bone origin.</summary>
	public void AutoOrientFromBoneChildren()
	{
		if (Skeleton == null) return;

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
				Vector3 defScaled = box.Size * rigScale;

				if (hb.Group == HitboxGroup.Head || !childPosWorld.HasValue)
				{
					box.Size = defScaled;
					continue;
				}

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

				capsule.Radius *= rigScale;

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

				Vector3 centreOffset = isExtremity ? dirLocal * (autoHeight * 0.3f) : dirLocal * (dist * 0.5f);
				cs.Transform = new Transform3D(new Basis(xAxis, yAxis, zAxis), centreOffset);
			}
		}
	}

	/// <summary>Cache of computed hitbox sizes per bone index, populated on the first AutoSize run and
	/// reused by later spawns. Flat key by bone index assumes a single character mesh.</summary>
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

	/// <summary>Fits each capsule/sphere/box to the skin-mesh vertices weighted to its bone (weight > 0.4).
	/// Runs after AutoOrient; skipped when no skinned MeshInstance3D exists. Vertices are mapped to
	/// bone-local space via skin.GetBindPose. Results are cached for subsequent spawns.</summary>
	private void AutoSizeFromMesh()
	{
		if (_sizeCache != null)
		{
			int applied = ApplyCachedSizes();
			Dbg.Print($"[HitboxRig] AutoSize CACHED: {applied}/{_hitboxNodes.Count} hitboxes from cache");
			return;
		}

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

			Transform3D shapeLocalFromBone = cs.Transform.AffineInverse();

			if (cs.Shape is SphereShape3D sph)
			{
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
					newCache[boneIdx] = new CachedSize { IsCapsule = true, Radius = p90, Height = cap.Height, OriginShiftBoneSpace = Vector3.Zero };
					resized++;
				}
			}
			else if (cs.Shape is BoxShape3D box)
			{
				var lv = new List<Vector3>(bvs.Count);
				foreach (var bv in bvs) lv.Add(shapeLocalFromBone * bv);

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

	/// <summary>Applies cached values from <see cref="_sizeCache"/> to this rig; returns the count applied.</summary>
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

	/// <summary>Collects skinned MeshInstance3D nodes with local Visible=true. Uses the local Visible flag
	/// (not IsVisibleInTree, since the agent root is hidden on the server) to skip inactive mesh variants
	/// that would contribute wrong vertices.</summary>
	private static void CollectAllSkinnedRecursive(Node n, List<MeshInstance3D> outList)
	{
		if (n is MeshInstance3D mi && mi.Skin != null && mi.Mesh != null && mi.Visible)
			outList.Add(mi);
		foreach (Node ch in n.GetChildren()) CollectAllSkinnedRecursive(ch, outList);
	}

	/// <summary>Detects the rig's unit scale vs the cm-authored default specs by taking the median ratio of
	/// measured bone→child distance to spec height across limb capsules. Returns 1.0 if unmeasurable; feet
	/// are excluded (their default height isn't a bone length).</summary>
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

	/// <summary>Returns the <paramref name="t"/>-percentile (0..1) of the vert component (axis 0=X,1=Y,2=Z)
	/// for outlier-resistant box fitting.</summary>
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
		new Spec { BoneName = "head", Group = HitboxGroup.Head,
			Shape = new BoxShape3D { Size = new Vector3(15f, 18f, 20f) } },
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
		new Spec { BoneName = "foot_l", Group = HitboxGroup.Foot,
			Shape = new BoxShape3D { Size = new Vector3(9f, 24f, 9f) } },
		new Spec { BoneName = "foot_r", Group = HitboxGroup.Foot,
			Shape = new BoxShape3D { Size = new Vector3(9f, 24f, 9f) } },
	};

	/// <summary>Editor entry point: generates the default hitbox set, then orients and sizes the capsules
	/// from the rest pose. Pass the edited scene root as <paramref name="owner"/> so the nodes are saved.</summary>
	public void BakeDefaultHitboxes(Node owner, Node container = null, IReadOnlyDictionary<string, string> boneRemap = null)
	{
		if (Skeleton == null) return;
		_sizeCache = null;
		_rids.Clear(); _hitboxNodes.Clear(); _collisionShapes.Clear();

		Node3D target = (container as Node3D) ?? Skeleton.GetNodeOrNull<Node3D>("Hitboxes");
		if (target != null)
			foreach (Node c in target.GetChildren())
				c.Free();

		SpawnDefaults(owner, container, boneRemap);
		AutoOrientFromBoneChildren();
		AutoSizeFromMesh();
	}

	/// <summary>Spawns the fallback default hitbox set under the skeleton (runtime, or editor when owner set).</summary>
	private void SpawnDefaults(Node owner = null, Node providedContainer = null, IReadOnlyDictionary<string, string> remap = null)
	{
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
			attach.BoneName = boneName;

			var body = new Hitbox
			{
				Name = $"hb_{spec.Group}_{spec.BoneName}",
				Group = spec.Group,
			};
			attach.AddChild(body);

			var cs = new CollisionShape3D { Shape = spec.Shape };
			body.AddChild(cs);

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

	/// <summary>Walks up the parent chain from the hitbox collider to the owning
	/// <see cref="NetworkPlayer"/> (common ancestor of all character variants); null if none.</summary>
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
