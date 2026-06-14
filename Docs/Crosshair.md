# Crosshair

Dynamic ConVar-driven crosshair. Expands with player speed and per-shot fire kicks (decaying), optionally hides during ADS. Creates its own CanvasLayer and drawer; assign `Player`.

## Fields

| Name | Summary |
|------|---------|
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.CanvasLayerOrder` | Cached name for the 'CanvasLayerOrder' field. |
| `PropertyName.Player` | Cached name for the 'Player' field. |
| `PropertyName._drawer` | Cached name for the '_drawer' field. |
| `PropertyName._layer` | Cached name for the '_layer' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Ready()` | Creates the canvas layer and drawer control on scene entry. |
