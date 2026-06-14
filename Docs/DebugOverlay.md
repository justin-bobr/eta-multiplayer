# DebugOverlay

Horizontal debug bar at the top of the screen. Visibility controlled via Settings.ShowDebugBar (default F3). Required: assign `Player` in the Inspector.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.BuildText` | Cached name for the 'BuildText' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.FontSize` | Cached name for the 'FontSize' field. |
| `PropertyName.Player` | Cached name for the 'Player' field. |
| `PropertyName.ShowOnStart` | Cached name for the 'ShowOnStart' field. |
| `PropertyName.UpdateInterval` | Cached name for the 'UpdateInterval' field. |
| `PropertyName._frameMaxWindow` | Cached name for the '_frameMaxWindow' field. |
| `PropertyName._label` | Cached name for the '_label' field. |
| `PropertyName._layer` | Cached name for the '_layer' field. |
| `PropertyName._maxWindowTimer` | Cached name for the '_maxWindowTimer' field. |
| `PropertyName._minFpsCurrent` | Cached name for the '_minFpsCurrent' field. |
| `PropertyName._minFpsLast` | Cached name for the '_minFpsLast' field. |
| `PropertyName._minFpsWindowTimer` | Cached name for the '_minFpsWindowTimer' field. |
| `PropertyName._panel` | Cached name for the '_panel' field. |
| `PropertyName._physMaxWindow` | Cached name for the '_physMaxWindow' field. |
| `PropertyName._procMaxWindow` | Cached name for the '_procMaxWindow' field. |
| `PropertyName._refreshTimer` | Cached name for the '_refreshTimer' field. |
| `PropertyName._smoothedFrameMs` | Cached name for the '_smoothedFrameMs' field. |
| `PropertyName._smoothedGpuMs` | Cached name for the '_smoothedGpuMs' field. |
| `PropertyName._smoothedPhysMs` | Cached name for the '_smoothedPhysMs' field. |
| `PropertyName._smoothedProcMs` | Cached name for the '_smoothedProcMs' field. |
| `PropertyName._smoothedRenderCpuMs` | Cached name for the '_smoothedRenderCpuMs' field. |

## Methods

| Name | Summary |
|------|---------|
| `BuildText()` | Composes the single-line overlay text from engine, performance and player stats. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Process(double)` | Per-frame: keeps smoothed metrics current and refreshes the label at UpdateInterval. |
| `_Ready()` | Builds the overlay layer, panel, and label; toggles visibility per Settings. |
