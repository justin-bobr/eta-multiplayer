# SpotsGizmoPlugin

Editor plugin entry point: registers a wireframe-box gizmo for `Zone` and a second for `BombSpot`. Uses Godot's own `EditorNode3DGizmoPlugin` system so the standard 3D View → Gizmos toggle hides / shows these outlines automatically (same as the built-in CollisionShape3D outline). The Zone / BombSpot nodes themselves don't render any editor-visible geometry — that's the plugin's job.

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
