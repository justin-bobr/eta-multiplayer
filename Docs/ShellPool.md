# ShellPool

MultiMesh shell-ejection pool: all cartridge cases render in one draw call. Per-shell physics is velocity + gravity + tumble with a down-raycast floor bounce that settles at low impact speed; dead slots are reused via swap-and-pop. Assign to LocalAnimation's Shell Pool export.

## Fields

| Name | Summary |
|------|---------|
| `Instance` | Singleton; character scenes call `ShellPool.Instance?.Emit(...)`. |
| `MethodName.AddExcludedBody` | Cached name for the 'AddExcludedBody' method. |
| `MethodName.Emit` | Cached name for the 'Emit' method. |
| `MethodName.RemoveExcludedBody` | Cached name for the 'RemoveExcludedBody' method. |
| `MethodName.WriteInstance` | Cached name for the 'WriteInstance' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._PhysicsProcess` | Cached name for the '_PhysicsProcess' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.BounceRestitution` | Cached name for the 'BounceRestitution' field. |
| `PropertyName.Camera` | Cached name for the 'Camera' field. |
| `PropertyName.DefaultLifetime` | Cached name for the 'DefaultLifetime' field. |
| `PropertyName.FloorNormalThreshold` | Cached name for the 'FloorNormalThreshold' field. |
| `PropertyName.FloorSnapExtraBuffer` | Cached name for the 'FloorSnapExtraBuffer' field. |
| `PropertyName.Gravity` | Cached name for the 'Gravity' field. |
| `PropertyName.HorizontalDamping` | Cached name for the 'HorizontalDamping' field. |
| `PropertyName.LodDistance` | Cached name for the 'LodDistance' field. |
| `PropertyName.MaxShells` | Cached name for the 'MaxShells' field. |
| `PropertyName.MinBounceSpeed` | Cached name for the 'MinBounceSpeed' field. |
| `PropertyName.NearClipDistance` | Cached name for the 'NearClipDistance' field. |
| `PropertyName.OffscreenDespawnTime` | Cached name for the 'OffscreenDespawnTime' field. |
| `PropertyName.ShellMesh` | Cached name for the 'ShellMesh' field. |
| `PropertyName.ShellScale` | Cached name for the 'ShellScale' field. |
| `PropertyName.SpawnGracePeriod` | Cached name for the 'SpawnGracePeriod' field. |
| `PropertyName._activeCount` | Cached name for the '_activeCount' field. |
| `PropertyName._autoFloorOffset` | Cached name for the '_autoFloorOffset' field. |
| `PropertyName._excludedColliders` | Cached name for the '_excludedColliders' field. |
| `PropertyName._floorRayQuery` | Cached name for the '_floorRayQuery' field. |
| `PropertyName._floorRayResult` | Cached name for the '_floorRayResult' field. |
| `PropertyName._mm` | Cached name for the '_mm' field. |
| `PropertyName._mmi` | Cached name for the '_mmi' field. |
| `PropertyName._overflowCursor` | Cached name for the '_overflowCursor' field. |
| `_excludedColliders` | Collider RIDs excluded from the floor raycast (player bodies). Populate via `AddExcludedBody`. |

## Methods

| Name | Summary |
|------|---------|
| `AddExcludedBody(CollisionObject3D)` | Adds a CollisionObject3D to the raycast-exclude list (idempotent). |
| `Emit(Transform3D, Vector3, Vector3, float)` | Spawns a new shell with the given transform/velocity/tumble. Overflow recycles the oldest slot round-robin. |
| `RemoveExcludedBody(CollisionObject3D)` | Removes a CollisionObject3D from the raycast-exclude list. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `WriteInstance(int)` | Writes a single shell's transform into the MultiMesh, applying near-clip culling. |
| `_ExitTree()` | Clears the singleton reference when this pool leaves the tree. |
| `_PhysicsProcess(double)` | Steps every active shell: applies gravity, raycasts, bounces, off-screen culling and lifetime expiry. |
| `_Ready()` | Initialises the MultiMesh, derives the auto floor offset from the shell AABB, and registers the singleton. |
