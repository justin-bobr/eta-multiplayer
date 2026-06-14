# JitterGraph

`Vantix.Debug.JitterGraph`

Line-graph control drawing a ring buffer of ms values plus a threshold line.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.InitBuffer` | Cached name for the 'InitBuffer' method. |
| `MethodName.Push` | Cached name for the 'Push' method. |
| `MethodName._Draw` | Cached name for the '_Draw' method. |
| `PropertyName.BgColor` | Cached name for the 'BgColor' field. |
| `PropertyName.LineColor` | Cached name for the 'LineColor' field. |
| `PropertyName.SampleCount` | Cached name for the 'SampleCount' field. |
| `PropertyName.ThresholdColor` | Cached name for the 'ThresholdColor' field. |
| `PropertyName.ThresholdMs` | Cached name for the 'ThresholdMs' field. |
| `PropertyName.YMaxMs` | Cached name for the 'YMaxMs' field. |
| `PropertyName._samples` | Cached name for the '_samples' field. |
| `PropertyName._writeIdx` | Cached name for the '_writeIdx' field. |

## Methods

| Name | Summary |
|------|---------|
| `InitBuffer()` | Allocates the sample ring buffer with `SampleCount` entries. |
| `Push(float)` | Appends a new sample (in ms) to the ring buffer and queues a redraw. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Draw()` | Draws background, threshold line and the connected sample polyline. |
