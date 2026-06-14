# SmokeGrenade

Deterministic smoke grenade using `GrenadeTrajectory`; spawns a `SmokeVoxelField` on impact. Owner mode runs physics and broadcasts ProjectileState/Despawn; puppet mode lerps from snapshots and deploys on Despawn.

## Fields

| Name | Summary |
|------|---------|
| `IsPuppet` | True = puppet (no physics, only position lerp from owner updates). |
| `MethodName.ApplyRemoteDespawn` | Cached name for the 'ApplyRemoteDespawn' method. |
| `MethodName.ApplyRemoteState` | Cached name for the 'ApplyRemoteState' method. |
| `MethodName.Deploy` | Cached name for the 'Deploy' method. |
| `MethodName.Spawn` | Cached name for the 'Spawn' method. |
| `MethodName.StepProjectile` | Cached name for the 'StepProjectile' method. |
| `MethodName._PhysicsProcess` | Cached name for the '_PhysicsProcess' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `OwnerNetId` | NetId of the thrower; 0 = non-replicated test spawn. |
| `ProjectileId` | Projectile id unique per owner. Together with OwnerNetId it is globally unique. |
| `PropertyName.IsPuppet` | Cached name for the 'IsPuppet' field. |
| `PropertyName.OwnerNetId` | Cached name for the 'OwnerNetId' field. |
| `PropertyName.ProjectileId` | Cached name for the 'ProjectileId' field. |
| `PropertyName._body` | Cached name for the '_body' field. |
| `PropertyName._deployed` | Cached name for the '_deployed' field. |
| `PropertyName._field` | Cached name for the '_field' field. |
| `PropertyName._flyTimer` | Cached name for the '_flyTimer' field. |
| `PropertyName._ownerExclude` | Cached name for the '_ownerExclude' field. |
| `PropertyName._puppetTargetPos` | Cached name for the '_puppetTargetPos' field. |
| `PropertyName._puppetTargetVel` | Cached name for the '_puppetTargetVel' field. |
| `PropertyName._query` | Cached name for the '_query' field. |
| `PropertyName._restTimer` | Cached name for the '_restTimer' field. |
| `PropertyName._stateBroadcastCounter` | Cached name for the '_stateBroadcastCounter' field. |
| `PropertyName._vel` | Cached name for the '_vel' field. |

## Methods

| Name | Summary |
|------|---------|
| `ApplyRemoteDespawn(Vector3)` | Called by the NetClient on incoming ProjectileDespawn — snaps the puppet to `finalPos` and spawns the smoke. No-op when already deployed. |
| `ApplyRemoteState(Vector3, Vector3)` | Called by the NetClient on incoming ProjectileState — sets the lerp target in puppet mode. |
| `Deploy()` | Spawns the voxel smoke field, marks the grenade as deployed, and handles owner/puppet replication. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `Spawn(Node, Vector3, Vector3, Rid, byte, uint, bool)` | Creates a grenade. Replication is optional via `ownerNetId`/`projectileId`; puppet mode follows owner ProjectileState. |
| `StepProjectile()` | Advances one deterministic physics step and triggers deployment on rest or timeout. |
| `_PhysicsProcess(double)` | Advances physics (owner) or lerps the position (puppet); periodically broadcasts ProjectileState while flying. |
| `_Ready()` | Builds the visible can, prepares the reusable raycast query, and registers with the NetClient when replicated. |
