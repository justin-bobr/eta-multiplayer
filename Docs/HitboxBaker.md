# HitboxBaker

Editor tool that generates and sizes per-bone hitbox capsules from a skeleton. Assign `Skeleton` (optionally a `HitboxContainer` and per-slot bone overrides), then tick `Build`: creates BoneAttachment3D → Hitbox → CollisionShape3D per slot, positions the capsule between the bone and its first child, sizes the radius from the mesh, and parents into the edited scene so it saves. Each *Bone field is a dropdown of the skeleton's bone names.

## Fields

| Name | Summary |
|------|---------|
| `HitboxContainer` | Where the generated hitbox nodes are placed. Leave empty to create + use a "Hitboxes" node under the skeleton. |
| `MethodName.Bake` | Cached name for the 'Bake' method. |
| `MethodName.BoneEnumHint` | Cached name for the 'BoneEnumHint' method. |
| `MethodName._ValidateProperty` | Cached name for the '_ValidateProperty' method. |
| `PropertyName.Baked` | Cached name for the 'Baked' field. |
| `PropertyName.Build` | Cached name for the 'Build' property. |
| `PropertyName.ChestBone` | Cached name for the 'ChestBone' field. |
| `PropertyName.HeadBone` | Cached name for the 'HeadBone' field. |
| `PropertyName.HitboxContainer` | Cached name for the 'HitboxContainer' field. |
| `PropertyName.LeftCalfBone` | Cached name for the 'LeftCalfBone' field. |
| `PropertyName.LeftClavicleBone` | Cached name for the 'LeftClavicleBone' field. |
| `PropertyName.LeftFootBallBone` | Cached name for the 'LeftFootBallBone' field. |
| `PropertyName.LeftFootBone` | Cached name for the 'LeftFootBone' field. |
| `PropertyName.LeftHandBone` | Cached name for the 'LeftHandBone' field. |
| `PropertyName.LeftLowerArmBone` | Cached name for the 'LeftLowerArmBone' field. |
| `PropertyName.LeftThighBone` | Cached name for the 'LeftThighBone' field. |
| `PropertyName.LeftUpperArmBone` | Cached name for the 'LeftUpperArmBone' field. |
| `PropertyName.RightCalfBone` | Cached name for the 'RightCalfBone' field. |
| `PropertyName.RightClavicleBone` | Cached name for the 'RightClavicleBone' field. |
| `PropertyName.RightFootBallBone` | Cached name for the 'RightFootBallBone' field. |
| `PropertyName.RightFootBone` | Cached name for the 'RightFootBone' field. |
| `PropertyName.RightHandBone` | Cached name for the 'RightHandBone' field. |
| `PropertyName.RightLowerArmBone` | Cached name for the 'RightLowerArmBone' field. |
| `PropertyName.RightThighBone` | Cached name for the 'RightThighBone' field. |
| `PropertyName.RightUpperArmBone` | Cached name for the 'RightUpperArmBone' field. |
| `PropertyName.Skeleton` | Cached name for the 'Skeleton' property. |
| `PropertyName.Spine01Bone` | Cached name for the 'Spine01Bone' field. |
| `PropertyName.Spine02Bone` | Cached name for the 'Spine02Bone' field. |
| `PropertyName.Spine04Bone` | Cached name for the 'Spine04Bone' field. |
| `PropertyName.Spine05Bone` | Cached name for the 'Spine05Bone' field. |
| `PropertyName.WaistBone` | Cached name for the 'WaistBone' field. |
| `PropertyName._skeleton` | Cached name for the '_skeleton' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
