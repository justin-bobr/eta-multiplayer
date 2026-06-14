# PostCanvasFx

`Vantix.Fx.PostCanvasFx`

Canvas-stage post-process layer; FSR2-compatible counterpart to `PostProcessEffect`. Runs after FSR2/TAA upscaling (Canvas stage) so it doesn't corrupt FSR2's input; reads SCREEN_TEXTURE. Supports chromatic aberration, sharpening, vignette, film grain. No depth/velocity here, so motion blur falls back to Environment.MotionBlurEnabled when FSR2 is active.

## Fields

| Name | Summary |
|------|---------|
| `AdsBlend` | Runtime value driven by LocalAnimation: 0 = no ADS, 1 = full ADS. |
| `ChromaticAberrationEnabled` | Mirror the Settings toggles; when false the matching strength is forced to zero in _Process. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.Aberration` | Cached name for the 'Aberration' field. |
| `PropertyName.AdsBlend` | Cached name for the 'AdsBlend' field. |
| `PropertyName.ChromaticAberrationEnabled` | Cached name for the 'ChromaticAberrationEnabled' field. |
| `PropertyName.FilmGrainEnabled` | Cached name for the 'FilmGrainEnabled' field. |
| `PropertyName.GrainStrength` | Cached name for the 'GrainStrength' field. |
| `PropertyName.Sharpen` | Cached name for the 'Sharpen' field. |
| `PropertyName.SharpeningEnabled` | Cached name for the 'SharpeningEnabled' field. |
| `PropertyName.VignetteAdsBoost` | Cached name for the 'VignetteAdsBoost' field. |
| `PropertyName.VignetteEnabled` | Cached name for the 'VignetteEnabled' field. |
| `PropertyName.VignetteRadius` | Cached name for the 'VignetteRadius' field. |
| `PropertyName.VignetteStrength` | Cached name for the 'VignetteStrength' field. |
| `PropertyName._mat` | Cached name for the '_mat' field. |
| `PropertyName._rect` | Cached name for the '_rect' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_ExitTree()` | Frees the singleton reference on exit so a re-init after disconnect/reconnect rebinds cleanly. |
| `_Process(double)` | Pushes the current settings and time into the shader uniforms every frame. |
| `_Ready()` | Builds the full-screen ColorRect. Layer 35 sits above the viewmodel (10) and below all HUD (>=40), so the FX wraps world + viewmodel but never the HUD. |
