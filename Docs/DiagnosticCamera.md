# DiagnosticCamera

Fixed-position diagnostic camera for isolating the world-render pipeline from viewmodel and WorldEnvironment influence, to bisect the source of a render artefact. While `DiagEnabled` it re-asserts Current=true every _Process so the player camera can't reclaim focus. `HideViewmodel` hides the viewmodel overlay; `BypassCompositor` attaches an empty Compositor (overrides WorldEnvironment compositor: no PostProcessEffect); `BypassEnvironment` attaches an empty Environment (no tonemap/glow/SSR/SSIL/SSAO/LUT/fog). If the artefact survives both, it's in the world itself.

## Fields

| Name | Summary |
|------|---------|
| `BypassCompositor` | Override the WorldEnvironment compositor with an empty one (bypasses CA/Sharpen/Vignette/Grain/MotionBlur). |
| `BypassEnvironment` | Override the WorldEnvironment environment with an empty one (bypasses tonemap/glow/SSR/SSIL/SSAO/adjustment/fog/LUT). |
| `DiagEnabled` | Toggle on/off at runtime. When true, this camera force-claims rendering every frame. |
| `HideViewmodel` | Hide the viewmodel SubViewportContainer while diagnostic mode is on; restored when it flips off. |
| `MethodName.ApplyCompositorBypass` | Cached name for the 'ApplyCompositorBypass' method. |
| `MethodName.ApplyEnvironmentBypass` | Cached name for the 'ApplyEnvironmentBypass' method. |
| `MethodName.ApplyHideViewmodel` | Cached name for the 'ApplyHideViewmodel' method. |
| `MethodName.FindViewmodelContainer` | Cached name for the 'FindViewmodelContainer' method. |
| `MethodName.RestoreViewmodel` | Cached name for the 'RestoreViewmodel' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `PropertyName.BypassCompositor` | Cached name for the 'BypassCompositor' field. |
| `PropertyName.BypassEnvironment` | Cached name for the 'BypassEnvironment' field. |
| `PropertyName.DiagEnabled` | Cached name for the 'DiagEnabled' field. |
| `PropertyName.HideViewmodel` | Cached name for the 'HideViewmodel' field. |
| `PropertyName._emptyCompositor` | Cached name for the '_emptyCompositor' field. |
| `PropertyName._emptyEnvironment` | Cached name for the '_emptyEnvironment' field. |
| `PropertyName._viewmodelCached` | Cached name for the '_viewmodelCached' field. |
| `PropertyName._viewmodelContainer` | Cached name for the '_viewmodelContainer' field. |
| `PropertyName._viewmodelOriginalVisible` | Cached name for the '_viewmodelOriginalVisible' field. |

## Methods

| Name | Summary |
|------|---------|
| `ApplyCompositorBypass()` | Toggles the override compositor per `BypassCompositor`, reusing one empty instance. |
| `ApplyEnvironmentBypass()` | Toggles the override environment per `BypassEnvironment`, reusing one empty instance (engine defaults). |
| `ApplyHideViewmodel()` | Finds the viewmodel container, caches its original Visible state, then hides it. Re-runs each frame to catch respawns. |
| `FindViewmodelContainer()` | Locates the first SubViewportContainer named "viewmodel_container" in the tree. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `RestoreViewmodel()` | Restores the viewmodel container's original visibility (typically true) when DiagEnabled flips off. |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
