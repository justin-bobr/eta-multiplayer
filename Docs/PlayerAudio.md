# PlayerAudio

Aggregates the FootstepAudio + WeaponAudio banks and exposes a wrapper API so callers don't touch the audio nodes directly. Server scenes lack these nodes (null banks); all PlayX methods are null-safe.

## Methods

| Name | Summary |
|------|---------|
| `#ctor(FootstepAudio, WeaponAudio)` | Stores the two audio banks for later wrapper calls. |
| `Configure(bool, WeaponStats)` | Forwards the IsLocalPlayer flag plus an initial weapon preload to both banks. |
| `PlayDryFire(WeaponStats, Vector3)` | Plays the empty-magazine dry-fire click. |
| `PlayJump(Vector3, StringName, float, bool)` | Plays a jump-takeoff footstep sound. |
| `PlayLand(Vector3, StringName, float, bool)` | Plays a landing footstep sound scaled by impact intensity. |
| `PlayReload(WeaponStats, Vector3)` | Plays the weapon reload sound. |
| `PlayShoot(WeaponStats, Vector3, ReverbEnv)` | Plays the weapon shoot sound at the muzzle position with the given reverb environment. |
| `PlayStep(Vector3, StringName, float, bool, bool)` | Plays a footstep sound at the given position. |
