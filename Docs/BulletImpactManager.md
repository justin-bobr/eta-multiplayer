# BulletImpactManager

Spawns a bullet-hole Decal3D and spark/dust GPUParticles3D at each hit point. Surface material comes from the collider's Godot group (e.g. "metal"); decal is skipped when no texture is configured.

## Fields

| Name | Summary |
|------|---------|
| `DecalDistanceFade` | Distance fade hides depth-precision flicker on far decals and saves render cost. |
| `LogImpactTiming` | Logs per-impact CPU time of decal and particle spawning. |
| `MethodName.BasisFromNormal` | Cached name for the 'BasisFromNormal' method. |
| `MethodName.EnsureDecalPool` | Cached name for the 'EnsureDecalPool' method. |
| `MethodName.EnsureParticlePool` | Cached name for the 'EnsureParticlePool' method. |
| `MethodName.PrewarmDecalsAsync` | Cached name for the 'PrewarmDecalsAsync' method. |
| `MethodName.PrewarmPools` | Cached name for the 'PrewarmPools' method. |
| `MethodName.Spawn` | Cached name for the 'Spawn' method. |
| `MethodName.SpawnDecal` | Cached name for the 'SpawnDecal' method. |
| `MethodName.SpawnParticles` | Cached name for the 'SpawnParticles' method. |
| `MethodName.TryPickValid` | Cached name for the 'TryPickValid' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.ConcreteDecals` | Cached name for the 'ConcreteDecals' field. |
| `PropertyName.DecalDepth` | Cached name for the 'DecalDepth' field. |
| `PropertyName.DecalDistanceFade` | Cached name for the 'DecalDistanceFade' field. |
| `PropertyName.DecalFadeBegin` | Cached name for the 'DecalFadeBegin' field. |
| `PropertyName.DecalFadeLength` | Cached name for the 'DecalFadeLength' field. |
| `PropertyName.DecalLifetime` | Cached name for the 'DecalLifetime' field. |
| `PropertyName.DecalPoolSize` | Cached name for the 'DecalPoolSize' field. |
| `PropertyName.DecalSize` | Cached name for the 'DecalSize' field. |
| `PropertyName.DefaultDecals` | Cached name for the 'DefaultDecals' field. |
| `PropertyName.GlassDecals` | Cached name for the 'GlassDecals' field. |
| `PropertyName.LogImpactTiming` | Cached name for the 'LogImpactTiming' field. |
| `PropertyName.MetalDecals` | Cached name for the 'MetalDecals' field. |
| `PropertyName.ParticleLifetime` | Cached name for the 'ParticleLifetime' field. |
| `PropertyName.ParticlePoolSize` | Cached name for the 'ParticlePoolSize' field. |
| `PropertyName.ParticleScale` | Cached name for the 'ParticleScale' field. |
| `PropertyName.ParticleSpeedMax` | Cached name for the 'ParticleSpeedMax' field. |
| `PropertyName.ParticleSpeedMin` | Cached name for the 'ParticleSpeedMin' field. |
| `PropertyName.ParticleSpread` | Cached name for the 'ParticleSpread' field. |
| `PropertyName.RandomRotation` | Cached name for the 'RandomRotation' field. |
| `PropertyName.ScaleMax` | Cached name for the 'ScaleMax' field. |
| `PropertyName.ScaleMin` | Cached name for the 'ScaleMin' field. |
| `PropertyName.WoodDecals` | Cached name for the 'WoodDecals' field. |
| `PropertyName._decalCursor` | Cached name for the '_decalCursor' field. |
| `PropertyName._decalPool` | Cached name for the '_decalPool' field. |
| `PropertyName._particleCursor` | Cached name for the '_particleCursor' field. |
| `PropertyName._particlePool` | Cached name for the '_particlePool' field. |
| `PropertyName._particleProcMats` | Cached name for the '_particleProcMats' field. |
| `PropertyName._recycleAccum` | Cached name for the '_recycleAccum' field. |
| `PropertyName._rng` | Cached name for the '_rng' field. |

## Methods

| Name | Summary |
|------|---------|
| `BasisFromNormal(Vector3)` | Builds an orthonormal basis where local-Y points along the surface normal (decals project along -Y). |
| `GetOrBuildMaterialCache(string, BulletImpactManager.ParticleProfile)` | Returns the cached material/mesh bundle for a material key, building it on first request. |
| `GetParticleParams(StringName)` | Returns the particle profile (color, count, gravity, etc.) for a given material tag. |
| `PrewarmDecalsAsync()` | Warms all decal sets on a background thread at level load so texture packing doesn't run on the main thread on the first shot. Lazy path covers the gap until prewarm finishes (lock-guarded). |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `Spawn(Vector3, Vector3, StringName)` | Spawns an impact: decal (when a texture is available) plus material-coded particles. |
| `SpawnDecal(Vector3, Vector3, StringName)` | Spawns the decal node for the given hit using the material-specific pool with default fallback. |
| `SpawnParticles(Vector3, Vector3, StringName)` | Spawns the GPU particle burst configured by the material's `ParticleProfile`. |
| `TryPickValid(Array<BulletDecalSet>)` | Random-picks a valid set from the pool, trying others in circular order; null if none valid. |
| `_ExitTree()` | Clears the singleton and frees the pools. Pool nodes are parented to SceneTree.Root (persists across scenes), so they must be freed manually or they leak on every scene reload. |
| `_Process(double)` | Hides decals and stops particles whose expiry has elapsed, throttled to `RecycleInterval`. |
| `_Ready()` | Registers the singleton, starts async decal prewarm, and pre-allocates the Decal + Particle pools (deferred so SceneTree.Root is reachable). Avoids a first-shot hitch. |
