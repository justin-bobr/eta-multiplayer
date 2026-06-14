# GrenadeTrajectory

Deterministic projectile simulation for thrown grenades, shared by `SmokeGrenade` and the aim guide (`GrenadeAimGuide`) so the preview matches the real throw. Fixed `FixedDt`, raycast-only, no RigidBody/randomness: same inputs land identically on every client.

## Fields

| Name | Summary |
|------|---------|
| `Gravity` | Effective gravity (range knob), set by NetworkPlayer from GrenadeRangeScale; smaller = travels farther. |
| `_predictQuery` | Simulates from origin/vel until rest (or `MaxFlyTime`), filling `pathOut` with world-space points and the landing point. |

## Methods

| Name | Summary |
|------|---------|
| `Advance(PhysicsDirectSpaceState3D, PhysicsRayQueryParameters3D, Vector3, Vector3)` | One projectile step (gravity, raycast move with bounce, ground drag). Returns whether grounded after the step. |
| `IsGrounded(PhysicsDirectSpaceState3D, PhysicsRayQueryParameters3D, Vector3, Vector3)` | Down-raycast: returns true if the grenade is (just barely) standing on a surface. |
