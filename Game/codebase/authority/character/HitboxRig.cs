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
	public void Build()
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

		AutoOrientFromBoneChildren();
		AutoSizeFromMesh();

		// Debug-Visualization: für Puppets (= remote-rendered Spieler) ein grünes Wireframe-Overlay je
		// Hitbox spawnen. Initial sind die unsichtbar — werden via ConVars.Cl.DebugHitbox runtime
		// getoggelt (default off; User aktiviert per console: cl_debug_hitbox 1).
		// Nur für Puppets weil LocalPlayer eh FPS-View hat und ServerAgent headless-server ist.
		Node ownerNode = Skeleton;
		while (ownerNode != null && ownerNode is not BaseCharacter) ownerNode = ownerNode.GetParent();
		if (ownerNode is PlayerCore ownerPc && ownerPc.IsPuppet)
			SpawnDebugWireframes();

		string ownerInfo = ownerNode is BaseCharacter ownerBc2 ? $"owner={ownerBc2.GetType().Name} netId={ownerBc2.NetId}" : "owner=null";
		Dbg.Print($"[HitboxRig] Build complete: {_rids.Count} hitboxes, skel={Skeleton.GetPath()} {ownerInfo}");
	}

	private readonly System.Collections.Generic.List<MeshInstance3D> _debugWireframes = new();
	private bool _lastWireframeVisible;
	/// <summary>Per-Frame: Wireframe-Sichtbarkeit basierend auf cl_debug_hitbox ConVar togglen.
	/// Spawn passiert einmalig in <see cref="SpawnDebugWireframes"/>, hier nur Visible-Flag.
	/// State-Cache via _lastWireframeVisible statt _debugWireframes[0].Visible — der [0]-Pfad
	/// war fragil (wenn die erste Mesh-Instance externally freed wurde oder ihre Visibility
	/// von einem Parent getoggled wurde, bailte der Toggle nicht mehr → manchmal "geht nicht").</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("HitboxRig._Process");
		if (_debugWireframes.Count == 0) return;
		bool wantVisible = ConVars.Cl.DebugHitbox;
		if (_lastWireframeVisible == wantVisible) return;
		_lastWireframeVisible = wantVisible;
		for (int i = _debugWireframes.Count - 1; i >= 0; i--)
		{
			var mi = _debugWireframes[i];
			if (!GodotObject.IsInstanceValid(mi)) { _debugWireframes.RemoveAt(i); continue; }
			mi.Visible = wantVisible;
		}
	}

	/// <summary>Spawnt eine grüne wireframe MeshInstance3D pro Hitbox so dass man sehen kann wo die
	/// Hitboxen tatsächlich im Welt-Raum sitzen. Nur Dbg-mode + Puppet (= sichtbarer Remote-Spieler).</summary>
	private void SpawnDebugWireframes()
	{
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.2f, 1f, 0.3f, 0.45f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			NoDepthTest = true,
		};
		foreach (Node n in Skeleton.GetChildren())
		{
			if (n is not BoneAttachment3D attach) continue;
			Hitbox hb = null;
			foreach (Node ch in attach.GetChildren()) if (ch is Hitbox h) { hb = h; break; }
			if (hb == null) continue;
			CollisionShape3D cs = null;
			foreach (Node ch in hb.GetChildren()) if (ch is CollisionShape3D c) { cs = c; break; }
			if (cs == null || cs.Shape == null) continue;

			Mesh debugMesh = cs.Shape switch
			{
				CapsuleShape3D cap => new CapsuleMesh { Radius = cap.Radius, Height = cap.Height, RadialSegments = 8, Rings = 4 },
				SphereShape3D sph => new SphereMesh { Radius = sph.Radius, Height = sph.Radius * 2f, RadialSegments = 8, Rings = 4 },
				BoxShape3D box => new BoxMesh { Size = box.Size },
				_ => null,
			};
			if (debugMesh == null) continue;
			var mi = new MeshInstance3D { Mesh = debugMesh, MaterialOverride = mat, Transform = cs.Transform, Visible = false };
			hb.AddChild(mi);
			_debugWireframes.Add(mi);
		}
	}

	/// <summary>Computes the direction from each hitbox bone to its first child bone and orients
	/// the CollisionShape along this direction (capsule-Y -> bone-to-child). Overwrites any
	/// scene-side transforms, so it works for any rig (mannequin, custom, mirrored L/R) without
	/// hand-tuning. For bones without a child (e.g. head) the shape stays at the bone origin
	/// (irrelevant for spheres).</summary>
	private void AutoOrientFromBoneChildren()
	{
		foreach (Node n in Skeleton.GetChildren())
		{
			if (n is not BoneAttachment3D attach) continue;
			int boneIdx = attach.BoneIdx;
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

			if (cs.Shape is SphereShape3D sphereShape)
			{
				// Spheres haben keine Richtung — Spec2.LocalOffset behält Vorrang wenn explizit gesetzt.
				// Für den HEAD-Bone speziell: Bone-Origin sitzt an der Schädelbasis (Hals→Kopf-Joint).
				// Standard-Sphere am Origin würde nur den Kiefer-Bereich treffen, Schädelkalotte wäre ungedeckt.
				// Auto-Offset: gegen-Direction zum PARENT-Bone (= neck) = Direction Schädelmitte. Verschiebt
				// die Sphere um sphere-radius * 0.7 in die "weg-vom-Hals"-Richtung. Rig-orientation-unabhängig.
				if (hb.Group == HitboxGroup.Head && cs.Transform.Origin == Vector3.Zero)
				{
					int parentBoneIdx = Skeleton.GetBoneParent(boneIdx);
					if (parentBoneIdx >= 0)
					{
						Vector3 parentOrigin = Skeleton.GetBoneGlobalRest(parentBoneIdx).Origin;
						Vector3 worldDirAway = boneRest.Origin - parentOrigin;
						if (worldDirAway.LengthSquared() > 0.0001f)
						{
							Vector3 dirLocalUp = (boneRest.Basis.Inverse() * worldDirAway).Normalized();
							cs.Transform = new Transform3D(Basis.Identity, dirLocalUp * (sphereShape.Radius * 0.7f));
						}
					}
				}
				continue;
			}

			if (cs.Shape is CapsuleShape3D capsule && childPosWorld.HasValue)
			{
				Vector3 worldDir = childPosWorld.Value - boneRest.Origin;
				float dist = worldDir.Length();
				if (dist < 0.001f) continue;

				// Auto-Length: Capsule-Höhe = bone-to-child Distance minus 2× radius (sodass die
				// Hemispheres genau bei bone-origin und child-origin enden = perfekte body-shape-Coverage).
				// Spec.Height ist nur noch Fallback wenn dist sehr klein ist.
				float autoHeight = Mathf.Max(dist - 2f * capsule.Radius, dist * 0.4f);
				capsule.Height = autoHeight;

				Vector3 dirLocal = (boneRest.Basis.Inverse() * worldDir).Normalized();

				Vector3 yAxis = dirLocal;
				Vector3 xAxis = yAxis.Cross(Vector3.Forward);
				if (xAxis.LengthSquared() < 0.01f) xAxis = yAxis.Cross(Vector3.Right);
				xAxis = xAxis.Normalized();
				Vector3 zAxis = xAxis.Cross(yAxis).Normalized();

				cs.Transform = new Transform3D(new Basis(xAxis, yAxis, zAxis), dirLocal * (dist * 0.5f));
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
		public float Radius;
		public float Height;
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
			int boneIdx = attach.BoneIdx;
			if (boneIdx < 0 || !vertsPerBone.TryGetValue(boneIdx, out var bvs)) continue;
			if (bvs.Count < 8) continue;

			// Transform vertices from Bone-Local nach Shape-Local (= inverse cs.Transform). cs ist child
			// vom Hitbox der am Bone-Origin sitzt → cs's bone-local-frame = cs.Transform.
			Transform3D shapeLocalFromBone = cs.Transform.AffineInverse();

			if (cs.Shape is SphereShape3D sph)
			{
				// 90th-Percentile statt MAX — outlier-resistent gegen Helm-Spitzen / Hair-Tips die
				// sonst den Head-Sphere doppelt so gross machen wie den eigentlichen Kopf.
				var dists = new List<float>(bvs.Count);
				foreach (var bv in bvs)
				{
					Vector3 lv = shapeLocalFromBone * bv;
					dists.Add(lv.Length());
				}
				dists.Sort();
				float p90 = dists[(int)(dists.Count * 0.90f)];
				if (p90 > 0.001f)
				{
					sph.Radius = p90;
					newCache[boneIdx] = new CachedSize { IsCapsule = false, Radius = p90 };
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
			int boneIdx = attach.BoneIdx;
			if (!_sizeCache.TryGetValue(boneIdx, out var spec)) continue;

			if (!spec.IsCapsule && cs.Shape is SphereShape3D sph)
			{
				sph.Radius = spec.Radius;
				applied++;
			}
			else if (spec.IsCapsule && cs.Shape is CapsuleShape3D cap)
			{
				cap.Radius = spec.Radius;
				cap.Height = spec.Height;
				cs.Transform = new Transform3D(cs.Transform.Basis, cs.Transform.Origin + spec.OriginShiftBoneSpace);
				applied++;
			}
		}
		return applied;
	}

	/// <summary>Sucht das skinned MeshInstance3D mit den MEISTEN Vertices irgendwo unter dem Owner-
	/// Subtree. Mehr-Vertex = Hauptkörper-Mesh (vs Accessory wie Waffe/Helm). Rekursiv, ignoriert
	/// MeshInstances ohne Skin.</summary>
	private MeshInstance3D FindSkinnedMesh()
	{
		var owner = Skeleton.GetParent() ?? Skeleton;
		MeshInstance3D best = null;
		int bestVerts = 0;
		CollectSkinnedRecursive(owner, ref best, ref bestVerts);
		return best;
	}

	private static void CollectSkinnedRecursive(Node n, ref MeshInstance3D best, ref int bestVerts)
	{
		if (n is MeshInstance3D mi && mi.Skin != null && mi.Mesh != null)
		{
			int total = 0;
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
			{
				var arr = mi.Mesh.SurfaceGetArrays(s);
				if (arr.Count > (int)Mesh.ArrayType.Vertex)
					total += arr[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
			}
			if (total > bestVerts) { best = mi; bestVerts = total; }
		}
		foreach (Node ch in n.GetChildren()) CollectSkinnedRecursive(ch, ref best, ref bestVerts);
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

	/// <summary>Returns the global rest origin of the first child bone of the given bone, or null if none.</summary>
	private Vector3? FindFirstChildBoneRestOrigin(int boneIdx)
	{
		int count = Skeleton.GetBoneCount();
		for (int i = 0; i < count; i++)
			if (Skeleton.GetBoneParent(i) == boneIdx)
				return Skeleton.GetBoneGlobalRest(i).Origin;
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

	/// <summary>Fallback hitbox spec with an optional skeleton-local offset (used for the head sphere).</summary>
	private class Spec2 : Spec
	{
		public Vector3 LocalOffset;
	}

	/// <summary>Returns the static fallback hitbox specification array (head + chest + waist + arms + legs + feet).</summary>
	private static Spec[] DefaultSpecs() => new Spec[]
	{
		// Head: r=13cm Sphere. LocalOffset = Vector3.Zero (= am Bone-Origin) — der AutoOrient code unten
		// berechnet jetzt automatisch den richtigen Up-Offset basierend auf der Direction vom Head-Bone
		// zu seinem Parent (neck_02). Das verlässt sich nicht mehr auf Rig-spezifische Y-Achse-Konventionen.
		new Spec2 { BoneName = "head", Group = HitboxGroup.Head,
			Shape = new SphereShape3D { Radius = 13f }, LocalOffset = Vector3.Zero },
		new Spec { BoneName = "spine_03", Group = HitboxGroup.Chest,
			Shape = new CapsuleShape3D { Radius = 28f, Height = 55f } },
		new Spec { BoneName = "pelvis", Group = HitboxGroup.Waist,
			Shape = new CapsuleShape3D { Radius = 22f, Height = 32f } },
		new Spec { BoneName = "upperarm_l", Group = HitboxGroup.Arm,
			Shape = new CapsuleShape3D { Radius = 10f, Height = 28f } },
		new Spec { BoneName = "upperarm_r", Group = HitboxGroup.Arm,
			Shape = new CapsuleShape3D { Radius = 10f, Height = 28f } },
		new Spec { BoneName = "lowerarm_l", Group = HitboxGroup.Arm,
			Shape = new CapsuleShape3D { Radius = 8f, Height = 26f } },
		new Spec { BoneName = "lowerarm_r", Group = HitboxGroup.Arm,
			Shape = new CapsuleShape3D { Radius = 8f, Height = 26f } },
		new Spec { BoneName = "hand_l", Group = HitboxGroup.Hand,
			Shape = new SphereShape3D { Radius = 6f } },
		new Spec { BoneName = "hand_r", Group = HitboxGroup.Hand,
			Shape = new SphereShape3D { Radius = 6f } },
		new Spec { BoneName = "thigh_l", Group = HitboxGroup.Leg,
			Shape = new CapsuleShape3D { Radius = 11f, Height = 42f } },
		new Spec { BoneName = "thigh_r", Group = HitboxGroup.Leg,
			Shape = new CapsuleShape3D { Radius = 11f, Height = 42f } },
		new Spec { BoneName = "calf_l", Group = HitboxGroup.Leg,
			Shape = new CapsuleShape3D { Radius = 9f, Height = 42f } },
		new Spec { BoneName = "calf_r", Group = HitboxGroup.Leg,
			Shape = new CapsuleShape3D { Radius = 9f, Height = 42f } },
		// Foot: Capsule entlang Ankle→Ball-Bone-Achse (= horizontal Richtung Toes). Rig hat ball_l/r
		// als Child-Bones → AutoOrient findet die Richtung. War vorher SphereShape weil ich falsch
		// angenommen hatte das fehlt — Sphere am Ankle-Origin deckte die Toes nicht ab.
		new Spec { BoneName = "foot_l", Group = HitboxGroup.Foot,
			Shape = new CapsuleShape3D { Radius = 7f, Height = 18f } },
		new Spec { BoneName = "foot_r", Group = HitboxGroup.Foot,
			Shape = new CapsuleShape3D { Radius = 7f, Height = 18f } },
	};

	/// <summary>Spawns the fallback default hitbox set under the skeleton at runtime.</summary>
	private void SpawnDefaults()
	{
		int built = 0;
		foreach (var spec in DefaultSpecs())
		{
			int boneIdx = Skeleton.FindBone(spec.BoneName);
			if (boneIdx < 0)
			{
				GD.PushWarning($"[HitboxRig] Bone '{spec.BoneName}' not found -> hitbox '{spec.Group}' skipped");
				continue;
			}

			var attach = new BoneAttachment3D
			{
				Name = $"hb_attach_{spec.BoneName}",
				BoneIdx = boneIdx,
			};
			Skeleton.AddChild(attach);

			var body = new Hitbox
			{
				Name = $"hb_{spec.Group}_{spec.BoneName}",
				Group = spec.Group,
			};
			attach.AddChild(body);

			var cs = new CollisionShape3D { Shape = spec.Shape };
			if (spec is Spec2 s2 && s2.LocalOffset != Vector3.Zero)
				cs.Transform = new Transform3D(Basis.Identity, s2.LocalOffset);
			body.AddChild(cs);

			_rids.Add(body.GetRid());
			_hitboxNodes.Add(body);
			_collisionShapes.Add(cs);
			built++;
		}
		Dbg.Print($"[HitboxRig] Fallback spawn: {built}/{DefaultSpecs().Length} hitboxes generated");
	}

	/// <summary>Reads the hitbox group (Head/Chest/Waist/Arm/Leg/Hand/Foot). Defaults to <see cref="HitboxGroup.Body"/>
	/// when the collider isn't a Hitbox (e.g. world geometry).</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	public static HitboxGroup ReadGroup(Node3D hitCollider) => hitCollider is Hitbox hb ? hb.Group : HitboxGroup.Body;

	/// <summary>Walks the parent chain from the hitbox collider upwards until a <see cref="BaseCharacter"/>
	/// is found. Path: Hitbox -> BoneAttachment3D -> Skeleton3D -> ... -> PlayerCore/PuppetPlayer/
	/// ServerPlayer/ServerBotPlayer owner. BaseCharacter is the common ancestor, so this works for
	/// all character variants.</summary>
	public static BaseCharacter FindOwner(Node3D hitCollider)
	{
		Node n = hitCollider;
		while (n != null)
		{
			if (n is BaseCharacter bc) return bc;
			n = n.GetParent();
		}
		return null;
	}
}
