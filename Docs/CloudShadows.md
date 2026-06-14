# CloudShadows

Ground cloud-shadow overlay driving `cloud_shadows.gdshader` via the material_override on this `MeshInstance3D`. Sun direction is auto-derived from `SunLightPath`. Exports are pushed in _Ready and on inspector edits; _Process only feeds the per-frame smoke fields.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.PushAllExports` | Cached name for the 'PushAllExports' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.CloudHeight` | Cached name for the 'CloudHeight' property. |
| `PropertyName.CloudNoise` | Cached name for the 'CloudNoise' property. |
| `PropertyName.CloudSag` | Cached name for the 'CloudSag' property. |
| `PropertyName.Coverage` | Cached name for the 'Coverage' property. |
| `PropertyName.FalloffRange` | Cached name for the 'FalloffRange' property. |
| `PropertyName.MaxDistance` | Cached name for the 'MaxDistance' property. |
| `PropertyName.NoiseTiling` | Cached name for the 'NoiseTiling' property. |
| `PropertyName.ShadowBrightnessFloor` | Cached name for the 'ShadowBrightnessFloor' property. |
| `PropertyName.ShadowBrightnessFull` | Cached name for the 'ShadowBrightnessFull' property. |
| `PropertyName.ShadowStrength` | Cached name for the 'ShadowStrength' property. |
| `PropertyName.ShadowTint` | Cached name for the 'ShadowTint' property. |
| `PropertyName.SmokeDensityMul` | Cached name for the 'SmokeDensityMul' property. |
| `PropertyName.Softness` | Cached name for the 'Softness' property. |
| `PropertyName.SunLightPath` | Cached name for the 'SunLightPath' property. |
| `PropertyName.SurfaceFalloff` | Cached name for the 'SurfaceFalloff' property. |
| `PropertyName.WindSpeed` | Cached name for the 'WindSpeed' property. |
| `PropertyName._cloudHeight` | Cached name for the '_cloudHeight' field. |
| `PropertyName._cloudNoise` | Cached name for the '_cloudNoise' field. |
| `PropertyName._cloudSag` | Cached name for the '_cloudSag' field. |
| `PropertyName._coverage` | Cached name for the '_coverage' field. |
| `PropertyName._falloffRange` | Cached name for the '_falloffRange' field. |
| `PropertyName._lastSmokeCount` | Cached name for the '_lastSmokeCount' field. |
| `PropertyName._mat` | Cached name for the '_mat' field. |
| `PropertyName._maxDistance` | Cached name for the '_maxDistance' field. |
| `PropertyName._noiseTiling` | Cached name for the '_noiseTiling' field. |
| `PropertyName._shadowBrightnessFloor` | Cached name for the '_shadowBrightnessFloor' field. |
| `PropertyName._shadowBrightnessFull` | Cached name for the '_shadowBrightnessFull' field. |
| `PropertyName._shadowStrength` | Cached name for the '_shadowStrength' field. |
| `PropertyName._shadowTint` | Cached name for the '_shadowTint' field. |
| `PropertyName._smokeDensityMul` | Cached name for the '_smokeDensityMul' field. |
| `PropertyName._softness` | Cached name for the '_softness' field. |
| `PropertyName._surfaceFalloff` | Cached name for the '_surfaceFalloff' field. |
| `PropertyName._windSpeed` | Cached name for the '_windSpeed' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
