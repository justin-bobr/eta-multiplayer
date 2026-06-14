# CrosshairDrawer

`Vantix.UI.CrosshairDrawer`

Inner drawing node. Polls movement state, animates the fire-kick gap, and renders via custom _Draw.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.DrawLineSeg` | Cached name for the 'DrawLineSeg' method. |
| `MethodName._Draw` | Cached name for the '_Draw' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `PropertyName.Player` | Cached name for the 'Player' field. |
| `PropertyName._fireKickGap` | Cached name for the '_fireKickGap' field. |
| `PropertyName._lastSeenShotIndex` | Cached name for the '_lastSeenShotIndex' field. |

## Methods

| Name | Summary |
|------|---------|
| `DrawLineSeg(Vector2, Vector2, float, float, bool, Color, Color)` | Draws an axis-aligned line as a rect (for clean pixel thickness) with an optional outline. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Draw()` | Renders the four-line crosshair plus optional outline and center dot. |
| `_Process(double)` | Per-frame poll of player state and decay of the fire-kick gap. |
