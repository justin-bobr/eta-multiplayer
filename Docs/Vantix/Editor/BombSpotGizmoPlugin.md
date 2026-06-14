# BombSpotGizmoPlugin

`Vantix.Editor.BombSpotGizmoPlugin`

Draws a wireframe outline of `Size` for every `BombSpot` node in the edited scene. Registered by `SpotsGizmoPlugin` at editor startup. Gizmo visibility follows the 3D View → Gizmos toggle automatically. Redraw is triggered by BombSpot.Size setter via `UpdateGizmos`.

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
