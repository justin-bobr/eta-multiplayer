# HudServerHitboxesDebug

`Vantix.Debug.HudServerHitboxesDebug`

Renders the server's hitbox transforms (from `DebugHitboxes` at ~10 Hz) as what `RunAuthoritativeHitscan` actually casts: shape, size, position, rotation. Shapes are read from the puppet's HitboxRig (identical specs); position/rotation from the packet. Active only when `DebugHitboxes`; pools MeshInstance3D per agent, invalidating the per-netId mesh cache when the HitboxRig reference changes (respawn/reconnect).

## Fields

| Name | Summary |
|------|---------|
| `MethodName.BuildMeshFromShape` | Cached name for the 'BuildMeshFromShape' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName._markerMat` | Cached name for the '_markerMat' field. |

## Methods

| Name | Summary |
|------|---------|
| `BuildMeshFromShape(Vantix.Character.HitboxRig, int)` | Builds the mesh for the hitbox index from the puppet's HitboxRig collision shape. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
