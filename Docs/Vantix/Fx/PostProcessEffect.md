# PostProcessEffect

`Vantix.Fx.PostProcessEffect`

Post-transparency CompositorEffect combining chromatic aberration, sharpening, vignette, film grain and motion blur into one compute pass. Hooked via the WorldEnvironment compositor (compositor.tres). Requires a non-multisampled color buffer (TAA, not MSAA).

## Properties

| Name | Summary |
|------|---------|
| `AnyEffectActive` | True when at least one sub-effect contributes; lets `_RenderCallback` skip the whole dispatch when all are off. |

## Fields

| Name | Summary |
|------|---------|
| `AdsBlend` | Runtime value driven by LocalAnimation: 0 = no ADS, 1 = full ADS boost. |
| `MethodName.GetOrCreateSet` | Cached name for the 'GetOrCreateSet' method. |
| `MethodName.InitializeCompute` | Cached name for the 'InitializeCompute' method. |
| `MethodName.IsHeadless` | Cached name for the 'IsHeadless' method. |
| `MethodName.RunBothPasses` | Cached name for the 'RunBothPasses' method. |
| `MethodName._Notification` | Cached name for the '_Notification' method. |
| `MethodName._RenderCallback` | Cached name for the '_RenderCallback' method. |
| `PropertyName.Aberration` | Cached name for the 'Aberration' field. |
| `PropertyName.AdsBlend` | Cached name for the 'AdsBlend' field. |
| `PropertyName.AnyEffectActive` | Cached name for the 'AnyEffectActive' property. |
| `PropertyName.ChromaticAberration` | Cached name for the 'ChromaticAberration' field. |
| `PropertyName.FilmGrain` | Cached name for the 'FilmGrain' field. |
| `PropertyName.GrainMode` | Cached name for the 'GrainMode' field. |
| `PropertyName.GrainStrength` | Cached name for the 'GrainStrength' field. |
| `PropertyName.MotionBlur` | Cached name for the 'MotionBlur' field. |
| `PropertyName.MotionBlurStrength` | Cached name for the 'MotionBlurStrength' field. |
| `PropertyName.Sharpen` | Cached name for the 'Sharpen' field. |
| `PropertyName.Sharpening` | Cached name for the 'Sharpening' field. |
| `PropertyName.Vignette` | Cached name for the 'Vignette' field. |
| `PropertyName.VignetteAdsBoost` | Cached name for the 'VignetteAdsBoost' field. |
| `PropertyName.VignetteRadius` | Cached name for the 'VignetteRadius' field. |
| `PropertyName.VignetteStrength` | Cached name for the 'VignetteStrength' field. |
| `PropertyName._context` | Cached name for the '_context' field. |
| `PropertyName._depthUniform` | Cached name for the '_depthUniform' field. |
| `PropertyName._dstUniform` | Cached name for the '_dstUniform' field. |
| `PropertyName._firstRenderLogged` | Cached name for the '_firstRenderLogged' field. |
| `PropertyName._linearSampler` | Cached name for the '_linearSampler' field. |
| `PropertyName._mbDiagLogged` | Cached name for the '_mbDiagLogged' field. |
| `PropertyName._pipeline` | Cached name for the '_pipeline' field. |
| `PropertyName._pushBytes1` | Cached name for the '_pushBytes1' field. |
| `PropertyName._pushBytes2` | Cached name for the '_pushBytes2' field. |
| `PropertyName._pushFloats` | Cached name for the '_pushFloats' field. |
| `PropertyName._rd` | Cached name for the '_rd' field. |
| `PropertyName._sampler` | Cached name for the '_sampler' field. |
| `PropertyName._shader` | Cached name for the '_shader' field. |
| `PropertyName._srcUniform` | Cached name for the '_srcUniform' field. |
| `PropertyName._tempName` | Cached name for the '_tempName' field. |
| `PropertyName._uniformList` | Cached name for the '_uniformList' field. |
| `PropertyName._velocityUniform` | Cached name for the '_velocityUniform' field. |

## Methods

| Name | Summary |
|------|---------|
| `#ctor()` | Configures the effect callback slot, requests required render targets, and queues compute init. |
| `GetOrCreateSet(Rid, Rid, Rid, Rid)` | Returns the cached UniformSet for the (src, dst) pair, creating one on first use. Stale entries (UniformSetIsValid) are dropped and rebuilt. |
| `InitializeCompute()` | Loads the compute shader and creates the pipeline plus samplers on the render thread. |
| `IsHeadless()` | True under the dummy renderer (--headless / dedicated server) where no RenderingDevice exists. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `RunBothPasses(Rid, Rid, Rid, Rid, Projection, Vector3, Vector2I, uint, uint, float, float)` | Dispatches both passes (mode 0 colour->temp, mode 1 temp->colour) in one ComputeListBegin/End with a barrier between. Each pass uses its own pre-allocated push-constant byte buffer so the command list captures the right snapshot. |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Notification(int)` | Frees GPU resources (shader, pipeline, samplers) on destruction. |
| `_RenderCallback(int, RenderData)` | Per-view render entry point: copies scene colour to a temp buffer then runs the effects pass back to colour. |
