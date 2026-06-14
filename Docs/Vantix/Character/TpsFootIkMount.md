# TpsFootIkMount

`Vantix.Character.TpsFootIkMount`

Self-contained Node3D mount for the TPS Foot IK (child of the player scene, no NetworkPlayer hook). Per tick, reads the parent CharacterBody3D's velocity/position/basis and feeds the `TpsFootIk` sim (ground snap raycast + TwoBoneIK3D influence). Server scenes have no mount, so the server agent pays no Foot-IK cost.

## Fields

| Name | Summary |
|------|---------|
| `DebugMarkers` | Show visible foot target spheres. |
| `EnableFootIk` | Master toggle — when false, Init is skipped entirely. |
| `EnableLegIK` | Influence toggle for the leg IK (smooth-lerped 0..1). |
| `GroundMask` | Which physics layers count as "ground" for the snap raycast. |
| `MethodName._PhysicsProcess` | Cached name for the '_PhysicsProcess' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.DebugMarkers` | Cached name for the 'DebugMarkers' field. |
| `PropertyName.EnableFootIk` | Cached name for the 'EnableFootIk' field. |
| `PropertyName.EnableLegIK` | Cached name for the 'EnableLegIK' field. |
| `PropertyName.GroundMask` | Cached name for the 'GroundMask' field. |
| `PropertyName.IkLeftNode` | Cached name for the 'IkLeftNode' field. |
| `PropertyName.IkRightNode` | Cached name for the 'IkRightNode' field. |
| `PropertyName.PoleLeft` | Cached name for the 'PoleLeft' field. |
| `PropertyName.PoleRight` | Cached name for the 'PoleRight' field. |
| `PropertyName.Skeleton` | Cached name for the 'Skeleton' field. |
| `PropertyName.TargetLeft` | Cached name for the 'TargetLeft' field. |
| `PropertyName.TargetRight` | Cached name for the 'TargetRight' field. |
| `PropertyName._parent` | Cached name for the '_parent' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_PhysicsProcess(double)` | Drives the IK sim each physics tick using the parent body's transform and velocity. |
| `_Ready()` | Initializes the IK sim if a CharacterBody3D parent and skeleton are wired up. |
