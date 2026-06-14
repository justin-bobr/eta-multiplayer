# ViewmodelLightSampler

Drives the viewmodel DirectionalLight to match world lighting in real time by mixing three raycast samples: a sun check (open sky adds `SunInfluence`, geometry = in shadow), an upward sky sample for outdoor brightness, and left/right/forward ambient samples that tint by hit-material albedo. Transitions are smoothed by `SmoothingSpeed`; `DebugLog` prints state every 0.5 s.

## Fields

| Name | Summary |
|------|---------|
| `FillColor` | Fill-light colour (slightly cool by default). |
| `FillLight` | Optional fill light opposite the key (dim, fixed energy). |
| `MethodName.ApplySmoothing` | Cached name for the 'ApplySmoothing' method. |
| `MethodName.CreateProbe` | Cached name for the 'CreateProbe' method. |
| `MethodName.EnsureCastNodes` | Cached name for the 'EnsureCastNodes' method. |
| `MethodName.FindFirstMesh` | Cached name for the 'FindFirstMesh' method. |
| `MethodName.FindWorldEnvironment` | Cached name for the 'FindWorldEnvironment' method. |
| `MethodName.FindWorldSun` | Cached name for the 'FindWorldSun' method. |
| `MethodName.SampleColliderColorCached` | Cached name for the 'SampleColliderColorCached' method. |
| `MethodName.SampleMaterialColor` | Cached name for the 'SampleMaterialColor' method. |
| `MethodName._PhysicsProcess` | Cached name for the '_PhysicsProcess' method. |
| `PropertyName.DebugLog` | Cached name for the 'DebugLog' field. |
| `PropertyName.FillColor` | Cached name for the 'FillColor' field. |
| `PropertyName.FillEnergy` | Cached name for the 'FillEnergy' field. |
| `PropertyName.FillLight` | Cached name for the 'FillLight' field. |
| `PropertyName.Intensity` | Cached name for the 'Intensity' field. |
| `PropertyName.MainCamera` | Cached name for the 'MainCamera' field. |
| `PropertyName.MaxEnergy` | Cached name for the 'MaxEnergy' field. |
| `PropertyName.MinEnergy` | Cached name for the 'MinEnergy' field. |
| `PropertyName.RimFallbackColor` | Cached name for the 'RimFallbackColor' field. |
| `PropertyName.RimLight` | Cached name for the 'RimLight' field. |
| `PropertyName.RimMaxEnergy` | Cached name for the 'RimMaxEnergy' field. |
| `PropertyName.SampleDistance` | Cached name for the 'SampleDistance' field. |
| `PropertyName.SkyFallbackColor` | Cached name for the 'SkyFallbackColor' field. |
| `PropertyName.SkyFallbackEnergy` | Cached name for the 'SkyFallbackEnergy' field. |
| `PropertyName.SmoothingSpeed` | Cached name for the 'SmoothingSpeed' field. |
| `PropertyName.SunDirectionWorld` | Cached name for the 'SunDirectionWorld' field. |
| `PropertyName.SunInfluence` | Cached name for the 'SunInfluence' field. |
| `PropertyName.SunRayDistance` | Cached name for the 'SunRayDistance' field. |
| `PropertyName.ViewmodelLight` | Cached name for the 'ViewmodelLight' field. |
| `PropertyName.WorldAmbientWeight` | Cached name for the 'WorldAmbientWeight' field. |
| `PropertyName.WorldEnv` | Cached name for the 'WorldEnv' field. |
| `PropertyName.WorldSun` | Cached name for the 'WorldSun' field. |
| `PropertyName._ambientCasts` | Cached name for the '_ambientCasts' field. |
| `PropertyName._capturedDefault` | Cached name for the '_capturedDefault' field. |
| `PropertyName._currentColor` | Cached name for the '_currentColor' field. |
| `PropertyName._currentEnergy` | Cached name for the '_currentEnergy' field. |
| `PropertyName._currentLightBasis` | Cached name for the '_currentLightBasis' field. |
| `PropertyName._defaultColor` | Cached name for the '_defaultColor' field. |
| `PropertyName._defaultEnergy` | Cached name for the '_defaultEnergy' field. |
| `PropertyName._disabledApplied` | Cached name for the '_disabledApplied' field. |
| `PropertyName._lightBasisInitialised` | Cached name for the '_lightBasisInitialised' field. |
| `PropertyName._nextDebugAt` | Cached name for the '_nextDebugAt' field. |
| `PropertyName._nextSunRescanAt` | Cached name for the '_nextSunRescanAt' field. |
| `PropertyName._nextWorldEnvRescanAt` | Cached name for the '_nextWorldEnvRescanAt' field. |
| `PropertyName._sampleAccum` | Cached name for the '_sampleAccum' field. |
| `PropertyName._sunConeCasts` | Cached name for the '_sunConeCasts' field. |
| `PropertyName._sunUpCast` | Cached name for the '_sunUpCast' field. |
| `PropertyName._targetColor` | Cached name for the '_targetColor' field. |
| `PropertyName._targetEnergy` | Cached name for the '_targetEnergy' field. |
| `PropertyName._targetLightBasis` | Cached name for the '_targetLightBasis' field. |
| `RimFallbackColor` | Rim-light colour when the sun is occluded (sky-fallback edge). |
| `RimLight` | Optional rim light; only active when WorldSun is visible. |
| `RimMaxEnergy` | Rim-light energy at full sun visibility. |
| `WorldCollisionMask` | Spawns a world-space RayCast3D probe. Mask covers world layers 1 + 20 only; player/hitbox layers stay blind so puppets don't block sun-visibility or pollute ambient samples. |
| `WorldEnv` | Optional world-scene WorldEnvironment (not the viewmodel's own) whose ambient colour is blended in. Auto-discovered if null. |
| `_currentLightBasis` | Smoothed light orientation, slerped toward _targetLightBasis so the specular doesn't snap. |

## Methods

| Name | Summary |
|------|---------|
| `ApplySmoothing(double)` | Interpolates _current* toward _target* and applies it to ViewmodelLight. Runs every tick even though sampling is 10 Hz. |
| `EnsureCastNodes()` | Lazy-allocates the 11 RayCast3D probes, adding the player's colliders as exceptions so sun-cone rays don't self-intersect. |
| `FindFirstMesh(Node)` | Locates the first MeshInstance3D for a collider, iterating by child index to stay allocation-free on the per-ray hot path. |
| `FindWorldEnvironment(Node)` | Finds the world WorldEnvironment (the one with a Compositor), else the first found. |
| `FindWorldSun(Node, World3D)` | Finds the world sun: the first DirectionalLight3D (not the viewmodel/fill/rim lights) in the camera's World3D. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SampleColliderColorCached(Node)` | Caches `SampleMaterialColor` by collider InstanceId so material reads happen only on the first hit. |
| `SampleMaterialColor(Node)` | Reads the hit mesh's albedo as an environment hint (StandardMaterial3D, or shader `albedo` / `global_tint`x`model_tint`); gray if none. |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_PhysicsProcess(double)` | Samples world lighting at 20 Hz while smoothing the result every tick, and applies it to the viewmodel light. |
