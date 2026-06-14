# GrenadeAimGuide

`Vantix.Fx.GrenadeAimGuide`

Draws the predicted grenade trajectory as a camera-facing `ImmediateMesh` ribbon plus a `TorusMesh` landing ring, from `Predict` path points. `TopLevel` = true so points are written in world coordinates.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.AlignToNormal` | Cached name for the 'AlignToNormal' method. |
| `MethodName.EnsureBuilt` | Cached name for the 'EnsureBuilt' method. |
| `MethodName.FadeColor` | Cached name for the 'FadeColor' method. |
| `MethodName.SetGuideVisible` | Cached name for the 'SetGuideVisible' method. |
| `MethodName.SideVec` | Cached name for the 'SideVec' method. |
| `MethodName.Vert` | Cached name for the 'Vert' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName._built` | Cached name for the '_built' field. |
| `PropertyName._line` | Cached name for the '_line' field. |
| `PropertyName._mesh` | Cached name for the '_mesh' field. |
| `PropertyName._ring` | Cached name for the '_ring' field. |

## Methods

| Name | Summary |
|------|---------|
| `AlignToNormal(Vector3, Vector3)` | Builds a transform with the Y axis along `up` so the ring lies flat on the surface. |
| `EnsureBuilt()` | Builds line and ring lazily, robust to whichever of _Ready/`UpdatePath`/`SetGuideVisible` runs first. |
| `FadeColor(float)` | Returns a slightly more saturated/opaque color toward the landing — the line "points" at the target. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SetGuideVisible(bool)` | Shows or hides the entire aim guide. |
| `SideVec(Vector3, Vector3, Vector3, Vector3)` | Side vector of the ribbon at point p — perpendicular to both path tangent and view direction. |
| `UpdatePath(IReadOnlyList<Vector3>, Vector3, Vector3)` | Rebuilds line and ring from a predicted trajectory. `points` are world coordinates (from `Predict`). |
| `Vert(Vector3, Color)` | Emits a single colored vertex into the current ImmediateMesh surface. |
| `_Ready()` | Ensures meshes exist when the node enters the tree. |
