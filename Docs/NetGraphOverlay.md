# NetGraphOverlay

Compact net_graph box top-right under the DebugOverlay bar: a 3x3 stats grid plus down/up jitter line-graphs. Visibility via Settings.ShowNetGraph.

## Fields

| Name | Summary |
|------|---------|
| `JitterGraphSamples` | Number of samples retained in the ring buffer of the graphs. |
| `JitterGraphYMaxMs` | Y-axis max of the jitter graphs (in ms). Values above are clamped. |
| `MethodName.MakeGraphHeader` | Cached name for the 'MakeGraphHeader' method. |
| `MethodName.Refresh` | Cached name for the 'Refresh' method. |
| `MethodName.Set` | Cached name for the 'Set' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `OffsetTopPx` | Pixel offset to the top edge — should sit beneath the DebugOverlay bar. |
| `PropertyName.FontSize` | Cached name for the 'FontSize' field. |
| `PropertyName.JitterGraphSamples` | Cached name for the 'JitterGraphSamples' field. |
| `PropertyName.JitterGraphYMaxMs` | Cached name for the 'JitterGraphYMaxMs' field. |
| `PropertyName.OffsetRightPx` | Cached name for the 'OffsetRightPx' field. |
| `PropertyName.OffsetTopPx` | Cached name for the 'OffsetTopPx' field. |
| `PropertyName.ShowOnStart` | Cached name for the 'ShowOnStart' field. |
| `PropertyName.UpdateInterval` | Cached name for the 'UpdateInterval' field. |
| `PropertyName._cells` | Cached name for the '_cells' field. |
| `PropertyName._downGraph` | Cached name for the '_downGraph' field. |
| `PropertyName._downHeader` | Cached name for the '_downHeader' field. |
| `PropertyName._downJitterMs` | Cached name for the '_downJitterMs' field. |
| `PropertyName._frameMsSmoothed` | Cached name for the '_frameMsSmoothed' field. |
| `PropertyName._frameMsVarSmoothed` | Cached name for the '_frameMsVarSmoothed' field. |
| `PropertyName._layer` | Cached name for the '_layer' field. |
| `PropertyName._panel` | Cached name for the '_panel' field. |
| `PropertyName._reconcileLabel` | Cached name for the '_reconcileLabel' field. |
| `PropertyName._refreshTimer` | Cached name for the '_refreshTimer' field. |
| `PropertyName._smoothedFps` | Cached name for the '_smoothedFps' field. |
| `PropertyName._upGraph` | Cached name for the '_upGraph' field. |
| `PropertyName._upHeader` | Cached name for the '_upHeader' field. |
| `PropertyName._upJitterMs` | Cached name for the '_upJitterMs' field. |

## Methods

| Name | Summary |
|------|---------|
| `MakeGraphHeader(string)` | Creates a styled small label used as a header above each jitter graph. |
| `Refresh()` | Updates the cell labels, reconcile indicator and graph headers from current NetStats. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `Set(int, int, string)` | Writes text into the cell at (row, col) of the 3x3 grid. |
| `_Process(double)` | Per-frame: updates smoothed metrics and triggers grid + graph refresh on the configured interval. |
| `_Ready()` | Builds the overlay UI (panel, grid, separator and jitter graphs). |
