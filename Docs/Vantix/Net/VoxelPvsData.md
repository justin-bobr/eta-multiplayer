# VoxelPvsData

`Vantix.Net.VoxelPvsData`

Serialisable Fog-of-War PVS data — output of `VoxelPvsInstance`'s editor bake. Ships as a .tres next to the map .tscn; `NetServer` loads it via `LoadFromData` for FoW from tick 1 with no runtime build. Layout: flat `VisibilityBytes` packing N² bits (N = `TotalVoxels`); bit (a×N + b) = 1 means voxel a sees b. Symmetric (both (a,b) and (b,a) set), so query order is irrelevant.

## Properties

| Name | Summary |
|------|---------|
| `Dims` | Number of cells per axis. Total voxel count = X × Y × Z. |
| `Origin` | World-space min corner of the voxel grid AABB. World position `p` maps to voxel index `floor((p - Origin) / VoxelSize)`. |
| `VoxelSize` | Edge length of one cubic voxel cell in metres. Matches the value used at bake time (possibly auto-coarsened from the requested size to fit the voxel budget). |

## Fields

| Name | Summary |
|------|---------|
| `MethodName.ComputePerVoxelVisibleCounts` | Cached name for the 'ComputePerVoxelVisibleCounts' method. |
| `PropertyName.Dims` | Cached name for the 'Dims' property. |
| `PropertyName.HasData` | Cached name for the 'HasData' property. |
| `PropertyName.Origin` | Cached name for the 'Origin' property. |
| `PropertyName.TotalVoxels` | Cached name for the 'TotalVoxels' property. |
| `PropertyName.VisibilityBytes` | Cached name for the 'VisibilityBytes' property. |
| `PropertyName.VoxelSize` | Cached name for the 'VoxelSize' property. |

## Methods

| Name | Summary |
|------|---------|
| `ComputePerVoxelVisibleCounts()` | Per voxel, the count of voxels it can see (incl. itself). O(N²) bit-scan (~1s at N=16k) — cache if queried repeatedly. Feeds the density-heatmap gizmo. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
