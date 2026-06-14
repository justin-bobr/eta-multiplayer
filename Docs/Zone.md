# Zone

Named, non-blocking 3D region: drives the HUD "you are in" label (innermost match via `ZoneAt`) and serves as a bot navigation target. Self-contained — the box shape is attached via `CreateShapeOwner`; the editor outline is drawn by `ZoneGizmoPlugin`. CollisionLayer is forced to 0 (never blocks), Monitoring on, Monitorable off, default Mask = 2 (player body layer).

## Properties

| Name | Summary |
|------|---------|
| `Size` | Box extents in meters; drives the internal `BoxShape3D`, reapplied on change. |
| `ZoneName` | Display name shown in the HUD; keep it short ("Long", "B-Tunnels", "Pit"). |

## Fields

| Name | Summary |
|------|---------|
| `MethodName.EnsureShape` | Cached name for the 'EnsureShape' method. |
| `MethodName.UpdateBoxShape` | Cached name for the 'UpdateBoxShape' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.Size` | Cached name for the 'Size' property. |
| `PropertyName.ZoneName` | Cached name for the 'ZoneName' property. |
| `PropertyName._boxShape` | Cached name for the '_boxShape' field. |
| `PropertyName._shapeOwnerId` | Cached name for the '_shapeOwnerId' field. |
| `PropertyName._shapeOwnerReady` | Cached name for the '_shapeOwnerReady' field. |
| `PropertyName._size` | Cached name for the '_size' field. |

## Methods

| Name | Summary |
|------|---------|
| `EnsureShape()` | Attaches a fresh `BoxShape3D` shape owner; runs each tree entry since ShapeOwners aren't serialised. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
