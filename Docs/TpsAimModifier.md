# TpsAimModifier

`SkeletonModifier3D` for TPS body aim rotation. Runs after the AnimationMixer but before the skeleton render flush. Applies pitch (around body-right) and twist (around world-up) in world space so it is rig-orientation-independent. Add as a child of the Skeleton3D and set `HeadPitch`, `AimBoneName` and `PitchScale`.

## Fields

| Name | Summary |
|------|---------|
| `Additive` | false: replace the bone with rest+aim. true: add aim on top of the animated pose (preserves idle/montage spine motion; no-op at pitch=0). |
| `BodyNode` | Optional body-orientation source for the world-space pitch axis. Defaults to the owning CharacterBody3D; set it when the visible body is a separate node (NetworkPlayer's GlowVisual). |
| `MethodName.Resolve` | Cached name for the 'Resolve' method. |
| `MethodName._ProcessModificationWithDelta` | Cached name for the '_ProcessModificationWithDelta' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `Pitch` | Direct pitch (radians) used when `HeadPitch` is null — lets non-NetworkPlayer drivers (NetworkPlayer) feed the aim pitch straight in. |
| `PropertyName.Additive` | Cached name for the 'Additive' field. |
| `PropertyName.AimBoneName` | Cached name for the 'AimBoneName' field. |
| `PropertyName.BodyNode` | Cached name for the 'BodyNode' field. |
| `PropertyName.HeadPitch` | Cached name for the 'HeadPitch' field. |
| `PropertyName.Pitch` | Cached name for the 'Pitch' field. |
| `PropertyName.PitchScale` | Cached name for the 'PitchScale' field. |
| `PropertyName.SpineTwist` | Cached name for the 'SpineTwist' field. |
| `PropertyName.WeaponBoneName` | Cached name for the 'WeaponBoneName' field. |
| `PropertyName._boneIdx` | Cached name for the '_boneIdx' field. |
| `PropertyName._characterBody` | Cached name for the '_characterBody' field. |
| `PropertyName._parentBoneIdx` | Cached name for the '_parentBoneIdx' field. |
| `PropertyName._resolved` | Cached name for the '_resolved' field. |
| `PropertyName._restRot` | Cached name for the '_restRot' field. |
| `PropertyName._weaponBoneIdx` | Cached name for the '_weaponBoneIdx' field. |
| `PropertyName._weaponParentIdx` | Cached name for the '_weaponParentIdx' field. |
| `SpineTwist` | Y twist (radians). Set per frame by PuppetPlayer for upper-body rotation. 0 = no twist. |
| `WeaponBoneName` | Weapon bone (root-IK-chain, not under the spine) carried with the aim bone by the same extra rotation about the aim-bone joint, so the gun stays in the hands. Empty = no weapon follow. |

## Methods

| Name | Summary |
|------|---------|
| `Resolve()` | Lazily resolves the aim bone index, caches its rest rotation, and walks up to the owning CharacterBody3D. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_ProcessModificationWithDelta(double)` | Applies pitch and twist to the aim bone in world space, on top of the animated pose. Survives the AnimationMixer pass. |
| `_Ready()` | Resolves the aim bone index on ready. |
