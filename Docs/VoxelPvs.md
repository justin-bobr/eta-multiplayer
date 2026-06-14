# VoxelPvs

Server-side line-of-sight precomputation. Voxelises the map into a coarse 3D grid and bakes pairwise voxel visibility via center-to-center raycasts; `CanSee` is then an O(1) bit lookup. Built incrementally via `BeginBuild`/`StepBuild`; returns "visible" (no culling) until `Built` flips true. Storage shares the flat byte[] format of the baked `VoxelPvsData` resource. Single-ray test is intentionally optimistic — may over-reveal, never wrongly hides.

## Fields

| Name | Summary |
|------|---------|
| `DefaultMaxVoxels` | Default voxel cap for the RUNTIME incremental fallback build path. Bounds memory (N²/8 bytes) and build cost (~N²/2 raycasts). 2500 voxels ≈ 780KB + ~3.1M raycasts = ~25s wall-clock at 1000 rays/poll. Editor bakes via `VoxelPvsInstance` pass a much larger cap to `BeginBuild` since they're one-shot offline and can afford the wait. |
| `EditorBakeMaxVoxels` | Voxel cap for the editor-only bake. 16,000 voxels ≈ 32MB visibility buffer + ~128M raycasts ≈ several minutes at the per-frame budget. Higher than that pushes the per-bit-index arithmetic close to int.MaxValue (46,340² ≈ 2.15G); we cap well below that. |

## Methods

| Name | Summary |
|------|---------|
| `BeginBuild(PhysicsDirectSpaceState3D, Aabb, float, uint, int)` | Starts a fresh build. Sets up the voxel grid (auto-coarsening `voxelSize` when the requested size would exceed `maxVoxels`) and allocates the visibility byte buffer, but performs ZERO raycasts — call `StepBuild` repeatedly to do the work. `CanSee` returns true (no culling) until `Built` flips true. |
| `CanSee(Vector3, Vector3)` | Returns true if `from` and `to` have line-of-sight according to the precomputed PVS. Out-of-bounds positions clamp to the nearest voxel. While `Built` is false (build in progress or never started), returns true (no culling) so the game keeps playing with old behavior until the PVS comes online. |
| `CancelBuild()` | Signals the active build to stop at the next `StepBuild` call. The partially-filled `_visibility` buffer is discarded — `Built` stays false, `IsBuilding` becomes false. The caller can then start a fresh build, or leave the PVS unbuilt (= `CanSee` returns true = no culling). |
| `ComputeWorldAabb(Node, uint)` | Computes the playable AABB by walking `CollisionShape3D` nodes under `root` that belong to a `CollisionObject3D` on a layer matching `layerMask`. This naturally excludes skyboxes, distant decoration meshes and other render-only geometry (which have no collision) — only walls, floors, ramps and crates contribute to the bounds. Falls back to a mesh-based walk when no collision shapes are found. Each axis is capped at `MaxAabbExtentM` as a safety belt. |
| `CountVisible()` | Counts set bits in the visibility buffer. O(byteCount) — at 32MB takes ~150ms. Used only by the post-bake density log, not by any hot path. |
| `DescribeLargestColliders(Node, uint, int)` | Diagnostic — walks the scene the same way `ComputeWorldAabb` does and returns up to `topN` collision shapes ordered by max-axis extent, descending. Use this when your computed AABB is bigger than expected (= some out-of-world collider is inflating it) to find the culprit. |
| `ExportBitsAsBytes()` | Returns the internal visibility buffer for serialisation into a `VoxelPvsData` resource. Caller may keep the reference — subsequent `BeginBuild` allocates a fresh buffer, so the returned array is safely owned by the caller after this method. |
| `LoadFromData(VoxelPvsData)` | Adopts the visibility data from a baked `VoxelPvsData` resource — INSTANT, no copy and no allocation. The internal buffer is set to the resource's byte array by reference (matched format = no transformation needed). Used by the server-startup path to skip the runtime build entirely when the level was pre-baked. |
| `PrecomputeSolidVoxels()` | One-shot pre-pass that flags every voxel whose center sits inside a collision shape on the build's layer mask. Subsequent `StepBuild` calls skip all pairs involving such voxels — no player can stand inside a solid block, so any FoW query against it would return false anyway, and the raycast pass would just waste CPU. On dust2-scale maps this typically drops the ray count by 50-80% (most voxels are above the playable ceiling, below the floor, or embedded in walls). Runs in <100ms even at 16k voxels — pure point-overlap queries are much cheaper than the directional raycasts they replace. |
| `StepBuild(int)` | Processes up to `maxRays` visibility raycasts and returns true once the build is fully complete (= `Built` becomes true on the same call). Idempotent when already built or never begun. Resumes precisely where the previous call left off. |
