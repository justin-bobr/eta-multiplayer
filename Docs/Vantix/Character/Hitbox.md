# Hitbox

`Vantix.Character.Hitbox`

Scene-node hitbox (capsule/box/sphere), dropped under a BoneAttachment3D in the skeleton. `HitboxRig` scans the skeleton in _Ready and configures the layer / self-exclude RIDs. The `Group` routes damage via `Damages`, configured per weapon.

## Fields

| Name | Summary |
|------|---------|
| `Group` | Hitbox zone (Head/Chest/Arm/Leg/...); keys the damage lookup in `Damages`. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.Group` | Cached name for the 'Group' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Ready()` | Configures the collision layer/mask and ensures the "flesh" group membership. |
