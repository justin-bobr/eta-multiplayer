# ZoneGizmoPlugin

Draws a wireframe outline of `Size` for every `Zone` node in the edited scene. Registered by `SpotsGizmoPlugin` at editor startup. Gizmo visibility follows the 3D View → Gizmos toggle automatically — this is the whole point of using `EditorNode3DGizmoPlugin` instead of a child MeshInstance3D. Redraw is triggered by Zone.Size setter via `UpdateGizmos`.

## Fields

| Name | Summary |
|------|---------|
| `MethodName._GetGizmoName` | Cached name for the '_GetGizmoName' method. |
| `MethodName._HasGizmo` | Cached name for the '_HasGizmo' method. |
| `MethodName._Redraw` | Cached name for the '_Redraw' method. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
