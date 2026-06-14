# HitboxRig

Per-bone hitbox rig. Scans the `Skeleton` for `Hitbox` children and registers their RIDs for self-exclude and damage-hitscan. Authored hitboxes (BoneAttachment3D → Hitbox → CollisionShape3D) are found at runtime; if none exist, a default set is spawned. Hitboxes sit on `Layer` with mask 0, so they never collide with body capsules.

## Properties

| Name | Summary |
|------|---------|
| `CollisionShapes` | CollisionShape3D refs parallel to `HitboxNodes`. The shape sits at a local offset from the hitbox origin (auto-orient places capsules at the bone-to-child midpoint), so lag-comp and markers must use `GlobalTransform`. |
| `HitboxNodes` | Hitbox node refs in same order as `Rids` — used for bone pose lag-comp (snapshot + rewind/restore of GlobalTransform per tick). |
| `Rids` | RIDs of all registered hitboxes - used by the hitscan to exclude self-hits. |

## Fields

| Name | Summary |
|------|---------|
| `Layer` | Layer 3 — all player hitboxes. Body capsules don't collide with hitboxes (hitbox mask=0). |
| `MethodName.ApplyCachedSizes` | Cached name for the 'ApplyCachedSizes' method. |
| `MethodName.AutoOrientFromBoneChildren` | Cached name for the 'AutoOrientFromBoneChildren' method. |
| `MethodName.AutoSizeFromMesh` | Cached name for the 'AutoSizeFromMesh' method. |
| `MethodName.Build` | Cached name for the 'Build' method. |
| `MethodName.DetectRigScale` | Cached name for the 'DetectRigScale' method. |
| `MethodName.FindOwner` | Cached name for the 'FindOwner' method. |
| `MethodName.ReadGroup` | Cached name for the 'ReadGroup' method. |
| `MethodName.ScanForHitboxes` | Cached name for the 'ScanForHitboxes' method. |
| `PropertyName.Skeleton` | Cached name for the 'Skeleton' field. |

## Methods

| Name | Summary |
|------|---------|
| `ApplyCachedSizes()` | Applies cached values from `_sizeCache` to this rig; returns the count applied. |
| `AutoOrientFromBoneChildren()` | Orients each CollisionShape along its bone-to-child direction (capsule-Y → bone-to-child), overwriting scene transforms so it works for any rig. Bones without a child keep the bone origin. |
| `AutoSizeFromMesh()` | Fits each capsule/sphere/box to the skin-mesh vertices weighted to its bone (weight > 0.4). Runs after AutoOrient; skipped when no skinned MeshInstance3D exists. Vertices are mapped to bone-local space via skin.GetBindPose. Results are cached for subsequent spawns. |
| `AxisPercentile(List<Vector3>, int, float)` | Returns the `t`-percentile (0..1) of the vert component (axis 0=X,1=Y,2=Z) for outlier-resistant box fitting. |
| `BakeDefaultHitboxes(Node, Node, IReadOnlyDictionary<String,String>)` | Editor entry point: generates the default hitbox set, then orients and sizes the capsules from the rest pose. Pass the edited scene root as `owner` so the nodes are saved. |
| `Build(bool)` | Scans authored hitboxes (or spawns the fallback set) and registers their RIDs. Call after Skeleton._Ready, else bone indices are -1. `skipAutoOrient` skips the runtime orient/size pass when capsules are pre-baked in the editor. |
| `CollectAllSkinnedRecursive(Node, List<MeshInstance3D>)` | Collects skinned MeshInstance3D nodes with local Visible=true. Uses the local Visible flag (not IsVisibleInTree, since the agent root is hidden on the server) to skip inactive mesh variants that would contribute wrong vertices. |
| `CollectBoneAttachments(Node)` | Recursively collects every BoneAttachment3D under the skeleton (loose children and the "Hitboxes" container). |
| `DefaultSpecs()` | Returns the static fallback hitbox specification array (head + chest + waist + arms + legs + feet). |
| `DetectRigScale()` | Detects the rig's unit scale vs the cm-authored default specs by taking the median ratio of measured bone→child distance to spec height across limb capsules. Returns 1.0 if unmeasurable; feet are excluded (their default height isn't a bone length). |
| `FindFirstChildBoneRestOrigin(int)` | Returns the global rest origin of the first child bone of the given bone, or null if none. |
| `FindOwner(Node3D)` | Walks up the parent chain from the hitbox collider to the owning `NetworkPlayer` (common ancestor of all character variants); null if none. |
| `ReadGroup(Node3D)` | Reads the hitbox group (Head/Chest/Waist/Arm/Leg/Hand/Foot). Defaults to `Body` when the collider isn't a Hitbox (e.g. world geometry). |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `ScanForHitboxes(Node)` | Recursively scans the given subtree and registers every `Hitbox` RID + Node. |
| `SpawnDefaults(Node, Node, IReadOnlyDictionary<String,String>)` | Spawns the fallback default hitbox set under the skeleton (runtime, or editor when owner set). |
