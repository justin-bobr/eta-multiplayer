# CharacterSetup

Stateless body-setup helpers shared by the simulating drivers (LocalPlayer / ServerPlayer) for identical capsule/crouch behaviour without a common base class. Each driver owns the resulting objects and passes them back in per call. PuppetPlayer doesn't move and uses none of this.

## Methods

| Name | Summary |
|------|---------|
| `ApplyCrouchHeight(CapsuleShape3D, CollisionShape3D, float, float, float)` | Live capsule resize from the crouch blend. Skips sub-0.1mm deltas (resize re-cooks the shape and is not visible). Eye-height adjustment is the driver's job (it owns the head pivot). |
| `SetupCapsule(CharacterBody3D, CollisionShape3D, float, float, float, float)` | Duplicates the scene-shared capsule per instance (so crouch resize doesn't shrink every player), configures floor behaviour, and returns it for the caller to keep. Null if no usable capsule. |
