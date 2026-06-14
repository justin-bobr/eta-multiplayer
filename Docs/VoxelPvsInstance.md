# VoxelPvsInstance

Child node of a map scene that bakes a server-side Fog-of-War visibility grid. "Bake PVS" in the inspector runs the offline raycast pass and writes a `VoxelPvsData` .tres next to the map's .tscn; at runtime `NetServer` loads it for FoW from tick 1 with no build cost. Workflow mirrors Godot's VoxelGI/LightmapGI bake nodes.

## Properties

| Name | Summary |
|------|---------|
| `BakePvsButton` | Inspector button — starts an async bake; the raycast pass runs incrementally in `_Process` to keep the editor responsive. |
| `CancelBakeButton` | Inspector button — aborts an in-flight bake; `Data` is left unchanged. No-op if none running. |
| `HasBakedData` | True when `Data` contains a valid baked PVS. |

## Fields

| Name | Summary |
|------|---------|
| `BakeRaysPerFrame` | Raycast budget per editor frame while baking. Higher = faster bake, choppier editor (8000 ≈ ~16ms hitch; crank to 100000 for walk-away one-shot bakes). |
| `BakeStatus` | Live bake status: "Idle.", "Baking: X%", or "Bake complete: ...". |
| `BakeVoxelSize` | Edge length of one cubic voxel cell, in metres. Smaller = finer occlusion at quadratic memory/bake cost. May be auto-coarsened during bake to stay within the voxel budget. |
| `Data` | Baked PVS data provided at runtime. Set by `BakeNow` or manually in the inspector. Null/empty → `NetServer` falls back to its incremental runtime build (or no FoW if `sv_fog_of_war` is off). |
| `MethodName.AddAabbWireframe` | Cached name for the 'AddAabbWireframe' method. |
| `MethodName.AddLine` | Cached name for the 'AddLine' method. |
| `MethodName.AddVoxelGrid` | Cached name for the 'AddVoxelGrid' method. |
| `MethodName.BakeNow` | Cached name for the 'BakeNow' method. |
| `MethodName.CancelBakeNow` | Cached name for the 'CancelBakeNow' method. |
| `MethodName.CountVisible` | Cached name for the 'CountVisible' method. |
| `MethodName.DefaultSavePath` | Cached name for the 'DefaultSavePath' method. |
| `MethodName.EnsureGizmoNode` | Cached name for the 'EnsureGizmoNode' method. |
| `MethodName.EnsureHeatmapNode` | Cached name for the 'EnsureHeatmapNode' method. |
| `MethodName.FinishActiveBake` | Cached name for the 'FinishActiveBake' method. |
| `MethodName.RebuildHeatmapData` | Cached name for the 'RebuildHeatmapData' method. |
| `MethodName.StepActiveBake` | Cached name for the 'StepActiveBake' method. |
| `MethodName.UpdateGizmo` | Cached name for the 'UpdateGizmo' method. |
| `MethodName.UpdateHeatmap` | Cached name for the 'UpdateHeatmap' method. |
| `MethodName.VoxelCenterFromData` | Cached name for the 'VoxelCenterFromData' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `OccluderCollisionMask` | Collision layer mask treated as occluders. Default 1 = world geometry. |
| `OverrideAabbOrigin` | Min corner of the override AABB. Only used when `UseOverrideAabb`; previewed by the gizmo. |
| `OverrideAabbSize` | Size of the override AABB. Pick the tightest box covering all standable positions; outside clamps to nearest voxel. |
| `PropertyName.BakePvsButton` | Cached name for the 'BakePvsButton' property. |
| `PropertyName.BakeRaysPerFrame` | Cached name for the 'BakeRaysPerFrame' field. |
| `PropertyName.BakeStatus` | Cached name for the 'BakeStatus' field. |
| `PropertyName.BakeVoxelSize` | Cached name for the 'BakeVoxelSize' field. |
| `PropertyName.CancelBakeButton` | Cached name for the 'CancelBakeButton' property. |
| `PropertyName.Data` | Cached name for the 'Data' field. |
| `PropertyName.HasBakedData` | Cached name for the 'HasBakedData' property. |
| `PropertyName.OccluderCollisionMask` | Cached name for the 'OccluderCollisionMask' field. |
| `PropertyName.OverrideAabbOrigin` | Cached name for the 'OverrideAabbOrigin' field. |
| `PropertyName.OverrideAabbSize` | Cached name for the 'OverrideAabbSize' field. |
| `PropertyName.ShowDensityHeatmap` | Cached name for the 'ShowDensityHeatmap' field. |
| `PropertyName.ShowGizmo` | Cached name for the 'ShowGizmo' field. |
| `PropertyName.ShowVoxelGrid` | Cached name for the 'ShowVoxelGrid' field. |
| `PropertyName.UseOverrideAabb` | Cached name for the 'UseOverrideAabb' field. |
| `PropertyName._bakeAabbSource` | Cached name for the '_bakeAabbSource' field. |
| `PropertyName._bakeLastLoggedPct` | Cached name for the '_bakeLastLoggedPct' field. |
| `PropertyName._bakeStartUsec` | Cached name for the '_bakeStartUsec' field. |
| `PropertyName._gizmoMaterial` | Cached name for the '_gizmoMaterial' field. |
| `PropertyName._gizmoMesh` | Cached name for the '_gizmoMesh' field. |
| `PropertyName._heatmapInstance` | Cached name for the '_heatmapInstance' field. |
| `PropertyName._heatmapMesh` | Cached name for the '_heatmapMesh' field. |
| `PropertyName._lastGizmoDims` | Cached name for the '_lastGizmoDims' field. |
| `PropertyName._lastGizmoShow` | Cached name for the '_lastGizmoShow' field. |
| `PropertyName._lastGizmoShowGrid` | Cached name for the '_lastGizmoShowGrid' field. |
| `PropertyName._lastGizmoVoxelSize` | Cached name for the '_lastGizmoVoxelSize' field. |
| `PropertyName._lastHeatmapData` | Cached name for the '_lastHeatmapData' field. |
| `PropertyName._lastHeatmapShown` | Cached name for the '_lastHeatmapShown' field. |
| `ShowDensityHeatmap` | Post-bake heatmap: a cube per playable voxel coloured by visibility (red = enclosed, green = open). Solid voxels are omitted. Requires `HasBakedData`. |
| `ShowGizmo` | Editor gizmo: draws the bake AABB (and optionally the voxel grid). No runtime cost. |
| `ShowVoxelGrid` | Draws every voxel cell's edges. Noisy above ~1000 voxels. |
| `UseOverrideAabb` | Use `OverrideAabbOrigin`+`OverrideAabbSize` for the grid extents instead of auto-deriving from meshes. Recommended on real maps so a skybox/decoration mesh doesn't inflate the AABB and coarsen the voxel size into uselessness. |

## Methods

| Name | Summary |
|------|---------|
| `BakeNow()` | Editor-only: starts an async bake (subsequent _Process calls drive the raycast loop). Calling mid-bake discards the previous bake and restarts. |
| `CancelBakeNow()` | Editor-only: stops the active bake next frame and resets `BakeStatus`. No-op if none running. |
| `DefaultSavePath(Node)` | Derives the default .tres save path from the parent scene's path. `res://x/y/foo.tscn` → `res://x/y/foo.pvs.tres`. Falls back to a project-root file when the scene is unsaved. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `StepActiveBake()` | Per editor frame while baking: steps the builder, updates `BakeStatus`, logs every ~5%, and finalises (saves) on completion. |
| `UpdateHeatmap()` | Rebuilds or hides the density heatmap. Lazy — real work only when `ShowDensityHeatmap` toggles on or `Data` swaps. The underlying count is O(N²) (~1s at 16k) but runs once per Data change. |
