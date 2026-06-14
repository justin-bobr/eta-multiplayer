# BonePoseRewindBuffer

Per-tick bone pose history per `NetworkPlayer`, storing hitbox-node GlobalTransforms. Lag-comp rewinds animated bone positions too, so headshots land when server/client animation differ by a frame or two. Buffers 32 ticks (~250ms at 128Hz).

## Fields

| Name | Summary |
|------|---------|
| `_fractionalResult` | Reusable result buffer for `QueryFractional` to avoid per-shot allocation. Consume synchronously — do not cache the returned reference across frames or pass to async code. |

## Methods

| Name | Summary |
|------|---------|
| `Init(int)` | Initializes the per-slot Transform3D arrays. Call once after HitboxRig.Build(). |
| `Push(uint, IReadOnlyList<CollisionShape3D>)` | Snapshots all CollisionShape3D GlobalTransforms into the ring buffer at the current tick. Uses the shape transform (not hitbox), since auto-orient offsets the shape from the hitbox origin. |
| `Query(uint)` | Returns the Transform3D[] snapshot from the tick closest to and not newer than `tick`. Returns null when no history is available (freshly spawned). |
| `QueryFractional(float)` | Interpolates each bone transform between the two stored ticks bracketing `fractionalTick` via `InterpolateWith`; clamps to the nearest endpoint when out of range. Returns null only when the buffer is empty. Returns the shared `_fractionalResult` buffer — consume synchronously, never cache or pass to async code. |
