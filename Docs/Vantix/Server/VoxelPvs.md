# VoxelPvs

`Vantix.Server.VoxelPvs`

Server-side line-of-sight precompute. Voxelises the map and bakes pairwise visibility so `CanSee` is a bit lookup. Built incrementally; returns "visible" until `Built`. Optimistic: may over-reveal, never wrongly hides.

## Fields

| Name | Summary |
|------|---------|
| `DefaultMaxVoxels` | Voxel cap for the runtime incremental fallback build. Editor bakes pass a larger cap via `BeginBuild` since they run offline. |
| `EditorBakeMaxVoxels` | Voxel cap for the editor-only bake. Kept well below the point where the per-bit-index arithmetic overflows int.MaxValue. |

## Methods

| Name | Summary |
|------|---------|
| `BeginBuild(PhysicsDirectSpaceState3D, Aabb, float, uint, int)` | Starts a fresh build. Sets up the voxel grid (auto-coarsening `voxelSize` when the requested size would exceed `maxVoxels`) and allocates the visibility byte buffer, but performs no raycasts — call `StepBuild` repeatedly to do the work. `CanSee` returns true (no culling) until `Built` flips true. |
| `CanSee(Vector3, Vector3)` | Returns true if `from` and `to` have line-of-sight according to the precomputed PVS. Out-of-bounds positions clamp to the nearest voxel. While `Built` is false (build in progress or never started), returns true (no culling) so the game keeps playing with old behavior until the PVS comes online. |
| `CancelBuild()` | Signals the active build to stop at the next `StepBuild` call. The partially-filled `_visibility` buffer is discarded — `Built` stays false, `IsBuilding` becomes false. The caller can then start a fresh build, or leave the PVS unbuilt (= `CanSee` returns true = no culling). |
| `ComputeWorldAabb(Node, uint)` | Computes the playable AABB by walking `CollisionShape3D` nodes under `root` that belong to a `CollisionObject3D` on a layer matching `layerMask`. This naturally excludes skyboxes, distant decoration meshes and other render-only geometry (which have no collision) — only walls, floors, ramps and crates contribute to the bounds. Falls back to a mesh-based walk when no collision shapes are found. Each axis is capped at `MaxAabbExtentM` as a safety belt. |
| `CountVisible()` | Counts set bits in the visibility buffer. O(byteCount) — at 32MB takes ~150ms. Used only by the post-bake density log, not by any hot path. |
| `DescribeLargestColliders(Node, uint, int)` | Diagnostic — walks the scene the same way `ComputeWorldAabb` does and returns up to `topN` collision shapes ordered by max-axis extent, descending. Use this when your computed AABB is bigger than expected (= some out-of-world collider is inflating it) to find the culprit. |
| `ExportBitsAsBytes()` | Returns the internal visibility buffer for serialisation into a `VoxelPvsData` resource. Caller may keep the reference — subsequent `BeginBuild` allocates a fresh buffer, so the returned array is safely owned by the caller after this method. |
| `LoadFromData(Vantix.Net.VoxelPvsData)` | Adopts the visibility data from a baked `VoxelPvsData` resource — no copy, no allocation. The internal buffer is set to the resource's byte array by reference (matched format = no transformation needed). Used by the server-startup path to skip the runtime build entirely when the level was pre-baked. |
| `PrecomputeSolidVoxels()` | One-shot pre-pass that flags every voxel whose center sits inside a collision shape on the build's layer mask. Subsequent `StepBuild` calls skip all pairs involving such voxels — no player can stand inside a solid block, so any FoW query against it would return false anyway, and the raycast pass would just waste CPU. On dust2-scale maps this typically drops the ray count by 50-80% (most voxels are above the playable ceiling, below the floor, or embedded in walls). Runs in <100ms even at 16k voxels — pure point-overlap queries are much cheaper than the directional raycasts they replace. |
| `StepBuild(int)` | Processes up to `maxRays` visibility raycasts and returns true once the build is fully complete (= `Built` becomes true on the same call). Idempotent when already built or never begun. Resumes precisely where the previous call left off. |
