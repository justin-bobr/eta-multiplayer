# SmokeVoxelField

`Vantix.Fx.SmokeVoxelField`

Voxel smoke with a grid advection sim. A one-time flood fill (BFS + raycasts) marks which cell faces walls block; each physics tick runs emission + buoyancy/wind advection + diffusion + dissipation, then bakes the density grid into a 3D texture rendered via a FogVolume. Fully deterministic (fixed timestep/wind, no randomness), so every client gets the same field.

## Properties

| Name | Summary |
|------|---------|
| `DensityTexture` | 3D density texture of the cloud — cloud_shadows samples it for the shadow mask. |
| `GridMin` | World-space minimum corner of the density texture. |
| `GridSize` | World-space size (edge lengths) of the density texture. |

## Fields

| Name | Summary |
|------|---------|
| `Active` | Active fields — the hitscan calls `DisturbAll` over this list. |
| `MethodName.Bake` | Cached name for the 'Bake' method. |
| `MethodName.BuildShapeMask` | Cached name for the 'BuildShapeMask' method. |
| `MethodName.BuildVolume` | Cached name for the 'BuildVolume' method. |
| `MethodName.CellWorld` | Cached name for the 'CellWorld' method. |
| `MethodName.DisturbAll` | Cached name for the 'DisturbAll' method. |
| `MethodName.DisturbRay` | Cached name for the 'DisturbRay' method. |
| `MethodName.EdgeClear` | Cached name for the 'EdgeClear' method. |
| `MethodName.EnsureVolumetricFog` | Cached name for the 'EnsureVolumetricFog' method. |
| `MethodName.FloodNeighbor` | Cached name for the 'FloodNeighbor' method. |
| `MethodName.Idx` | Cached name for the 'Idx' method. |
| `MethodName.RayHits` | Cached name for the 'RayHits' method. |
| `MethodName.SegDistSq` | Cached name for the 'SegDistSq' method. |
| `MethodName.Spawn` | Cached name for the 'Spawn' method. |
| `MethodName.StepFlood` | Cached name for the 'StepFlood' method. |
| `MethodName.StepSim` | Cached name for the 'StepSim' method. |
| `MethodName._EnterTree` | Cached name for the '_EnterTree' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._PhysicsProcess` | Cached name for the '_PhysicsProcess' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.Buoyancy` | Cached name for the 'Buoyancy' field. |
| `PropertyName.BurnTime` | Cached name for the 'BurnTime' field. |
| `PropertyName.ChannelDuration` | Cached name for the 'ChannelDuration' field. |
| `PropertyName.ChannelRadius` | Cached name for the 'ChannelRadius' field. |
| `PropertyName.DensityMul` | Cached name for the 'DensityMul' field. |
| `PropertyName.DensityTexture` | Cached name for the 'DensityTexture' property. |
| `PropertyName.Diffusion` | Cached name for the 'Diffusion' field. |
| `PropertyName.Dissipation` | Cached name for the 'Dissipation' field. |
| `PropertyName.DomainHeight` | Cached name for the 'DomainHeight' field. |
| `PropertyName.DomainWidth` | Cached name for the 'DomainWidth' field. |
| `PropertyName.EmissionStrength` | Cached name for the 'EmissionStrength' field. |
| `PropertyName.EmitRate` | Cached name for the 'EmitRate' field. |
| `PropertyName.FadeRate` | Cached name for the 'FadeRate' field. |
| `PropertyName.FadeRise` | Cached name for the 'FadeRise' field. |
| `PropertyName.GridMin` | Cached name for the 'GridMin' property. |
| `PropertyName.GridSize` | Cached name for the 'GridSize' property. |
| `PropertyName.MapMask` | Cached name for the 'MapMask' field. |
| `PropertyName.MaxDensity` | Cached name for the 'MaxDensity' field. |
| `PropertyName.SkyFade` | Cached name for the 'SkyFade' field. |
| `PropertyName.SmokeCore` | Cached name for the 'SmokeCore' field. |
| `PropertyName.VoxelSize` | Cached name for the 'VoxelSize' field. |
| `PropertyName.WallHeight` | Cached name for the 'WallHeight' field. |
| `PropertyName.Wind` | Cached name for the 'Wind' field. |
| `PropertyName._age` | Cached name for the '_age' field. |
| `PropertyName._bakeAccum` | Cached name for the '_bakeAccum' field. |
| `PropertyName._built` | Cached name for the '_built' field. |
| `PropertyName._cell` | Cached name for the '_cell' field. |
| `PropertyName._chanA` | Cached name for the '_chanA' field. |
| `PropertyName._chanB` | Cached name for the '_chanB' field. |
| `PropertyName._chanTimer` | Cached name for the '_chanTimer' field. |
| `PropertyName._delta` | Cached name for the '_delta' field. |
| `PropertyName._density` | Cached name for the '_density' field. |
| `PropertyName._floodBudget` | Cached name for the '_floodBudget' field. |
| `PropertyName._floodDone` | Cached name for the '_floodDone' field. |
| `PropertyName._floodQuery` | Cached name for the '_floodQuery' field. |
| `PropertyName._floodResult` | Cached name for the '_floodResult' field. |
| `PropertyName._gridMin` | Cached name for the '_gridMin' field. |
| `PropertyName._images` | Cached name for the '_images' field. |
| `PropertyName._mat` | Cached name for the '_mat' field. |
| `PropertyName._n` | Cached name for the '_n' field. |
| `PropertyName._nx` | Cached name for the '_nx' field. |
| `PropertyName._ny` | Cached name for the '_ny' field. |
| `PropertyName._nz` | Cached name for the '_nz' field. |
| `PropertyName._originSet` | Cached name for the '_originSet' field. |
| `PropertyName._shapeMask` | Cached name for the '_shapeMask' field. |
| `PropertyName._sliceBuf` | Cached name for the '_sliceBuf' field. |
| `PropertyName._srcIdx` | Cached name for the '_srcIdx' field. |
| `PropertyName._tex` | Cached name for the '_tex' field. |
| `PropertyName._wallMaxY` | Cached name for the '_wallMaxY' field. |

## Methods

| Name | Summary |
|------|---------|
| `Bake()` | Copies the density grid into per-slice images, uploads the 3D texture, and frees the field once fully dissolved. |
| `BuildShapeMask()` | Builds the static shape mask once: a grounded, noise-distorted ellipsoid multiplied into the density in `Bake`. |
| `BuildVolume()` | Allocates the 3D density texture and creates the FogVolume + shader material that render the smoke. |
| `CellWorld(int, int, int)` | Returns the world-space position of the centre of cell (x,y,z). |
| `DisturbAll(Vector3, Vector3, float)` | Called by the hitscan — clears a channel in all active smoke fields. |
| `DisturbRay(Vector3, Vector3, float)` | Marks the shot line for clearing — the sim deterministically empties and refills the channel. |
| `EdgeClear(PhysicsDirectSpaceState3D, Vector3, Vector3)` | Edge clear? Casts in both directions — single-sided trimesh walls only hit from the front. |
| `EnsureVolumetricFog()` | Enables Volumetric Fog on the world Environment — without it no FogVolume renders. |
| `FloodNeighbor(PhysicsDirectSpaceState3D, int, int, int, Vector3, int, int, int)` | Checks one neighbour direction, raycasts when below WallHeight, and records the open face plus frontier entry. |
| `Idx(int, int, int)` | Flattens (x,y,z) cell coordinates into a linear array index. |
| `RayHits(PhysicsDirectSpaceState3D, Vector3, Vector3)` | Returns true if a raycast from `from` to `to` hits map geometry. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SegDistSq(Vector3, Vector3, Vector3)` | Squared distance from point to segment. |
| `Spawn(Node, Vector3)` | Spawns a voxel smoke field. `origin` is the detonation point. |
| `StepFlood()` | Processes a budget of frontier cells per tick, raycasting cell-to-cell to mark open faces. |
| `StepSim(float)` | Runs one deterministic advection + diffusion + dissipation step on the density grid. |
| `_EnterTree()` | Registers this field with the global active list on tree entry. |
| `_ExitTree()` | Removes this field from the global active list on tree exit. |
| `_PhysicsProcess(double)` | Per-tick driver: completes flood-fill, builds the volume once, then runs the sim and periodic bakes. |
| `_Ready()` | Allocates grid arrays, computes the shape mask, and seeds the flood-fill frontier. |
