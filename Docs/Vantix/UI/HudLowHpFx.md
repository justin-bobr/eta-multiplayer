# HudLowHpFx

`Vantix.UI.HudLowHpFx`

Low-HP red vignette pulse when HP (from LastSelfSnap) drops below `WarnHpThreshold`. Pulse intensifies toward 0 HP and fades once HP recovers above the threshold.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.BuildShader` | Cached name for the 'BuildShader' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName._lastAppliedStrength` | Cached name for the '_lastAppliedStrength' field. |
| `PropertyName._shaderMat` | Cached name for the '_shaderMat' field. |
| `PropertyName._time` | Cached name for the '_time' field. |
| `PropertyName._vignetteRect` | Cached name for the '_vignetteRect' field. |
| `WarnHpThreshold` | HP threshold below which the effect activates (30% of max). |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
