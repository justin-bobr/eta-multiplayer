# SpotsGizmoPlugin

`Vantix.Editor.SpotsGizmoPlugin`

Editor plugin entry point: registers wireframe-box gizmos for `Zone`, `BombSpot`, and `Spawn`. Uses `EditorNode3DGizmoPlugin` so the 3D View → Gizmos toggle hides/shows these outlines automatically. The nodes themselves render no editor-visible geometry.

## Fields

| Name | Summary |
|------|---------|
| `MethodName._EnterTree` | Cached name for the '_EnterTree' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `PropertyName._bombSpotGizmo` | Cached name for the '_bombSpotGizmo' field. |
| `PropertyName._spawnGizmo` | Cached name for the '_spawnGizmo' field. |
| `PropertyName._zoneGizmo` | Cached name for the '_zoneGizmo' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
