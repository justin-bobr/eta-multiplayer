# HudMiniProfiler

`Vantix.UI.HudMiniProfiler`

HUD overlay for `MiniProfiler`. Lists samples sorted by 5-second peak descending; values over `Cl`.ProfilerThresholdMs shown red. Toggle via `cl_profiler 1`. The peak rises only and resets to current after 5 s, keeping a stable sort. Prints once per sample when it first crosses the threshold.

## Fields

| Name | Summary |
|------|---------|
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.LayerOrder` | Cached name for the 'LayerOrder' field. |
| `PropertyName._hudUpdateAccumSec` | Cached name for the '_hudUpdateAccumSec' field. |
| `PropertyName._label` | Cached name for the '_label' field. |
| `PropertyName._panel` | Cached name for the '_panel' field. |
| `PropertyName._writeAccumSec` | Cached name for the '_writeAccumSec' field. |
| `PropertyName._writePathPrinted` | Cached name for the '_writePathPrinted' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
