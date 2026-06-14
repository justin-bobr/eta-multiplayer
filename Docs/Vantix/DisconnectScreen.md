# DisconnectScreen

`Vantix.DisconnectScreen`

Fullscreen overlay shown after a disconnect — displays the reason plus reconnect and quit buttons. Code-driven UI; instantiated by `HandleDisconnect`.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.OnReconnectPressed` | Cached name for the 'OnReconnectPressed' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.Reason` | Cached name for the 'Reason' field. |

## Methods

| Name | Summary |
|------|---------|
| `OnReconnectPressed()` | Forwards the reconnect press to NetMain. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Ready()` | Builds the overlay UI, frees the mouse, and grabs focus. |
