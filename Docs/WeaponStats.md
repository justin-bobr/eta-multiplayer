# WeaponStats

Immutable gameplay + visual stats for one weapon; one instance per weapon in `Weapons`.

## Properties

| Name | Summary |
|------|---------|
| `AdsAmbientMul` | Ambient animations (bob/sway/lean/breath) multiplier at full ADS — subtle ones still pass through. |
| `AdsBlendTime` | Transition time hip ↔ ADS. |
| `AdsBloomMul` | Bloom multiplier while ADS — almost off so the deterministic pattern dominates. |
| `AdsCamShakeMul` | Reduces high-frequency cam shake while ADS (typical ~40 %, scope is stabilized). |
| `AdsCameraKickMul` | Reduces aim-punch climb while aiming down sights (typical 50-65 % of hipfire amplitude). |
| `AdsCrouchPosAdd` | Additive position correction scaled with CrouchBlend to keep iron sights aligned while crouching. |
| `AdsFov` | Target FOV during ADS (smaller = more zoom). |
| `AdsKickMul` | Visual kick multiplier at full ADS for ROTATION (pitch/yaw/roll = muzzle). Small = scope stays calm. |
| `AdsKickPosMul` | Visual kick multiplier at full ADS for position (back/up = stock). |
| `AdsMovementSpreadMul` | Extra multiplier on the movement spread portion while ADS. |
| `AdsPosOffset` | Local-space ADS position offset, lerped additively. |
| `AdsRotOffset` | Local-space ADS rotation offset in degrees, lerped additively. |
| `AdsSensitivityMul` | Mouse sensitivity multiplier during ADS. |
| `AdsSpeedMul` | Movement speed multiplier during ADS (mix between hip-walk and tactical pace). |
| `AdsSpreadAirMul` | ADS spread multiplier while airborne (replaces AdsSpreadMul). ADS accuracy bonus is mostly lost mid-jump. |
| `AdsSpreadMul` | Base + movement spread multiplier while ADS (laser-tight first shot). |
| `AirborneSpreadMul` | Spread multiplier while airborne (jumping is very inaccurate). |
| `CamShakeAmount` | Per-weapon multiplier on cam-shake impulses (e.g. LMG=1.5, Sniper=2.0, SMG=0.7). |
| `CameraAimPunchMul` | 0 = camera stays calm, 1 = camera punches up — hipfire baseline. |
| `Damages` | Server-authoritative damage per `HitboxGroup`; look up via `DamageFor`. |
| `DistantCrossoverM` | Above this listener distance, switch from body to distant layer. |
| `DryFireClips` | Empty-magazine click. |
| `FingerKickZ` | Trigger-finger pull-back (Marker3D Z offset, depends on the trigger geometry of the weapon). |
| `HipfireBaseSpread` | Always-on hipfire spread (even while standing). ADS reduces it via AdsSpreadMul. |
| `HipfireBloomCurve` | 1.0 = linear (first shots already get bloom). >1 for an exponential ramp. |
| `HipfireBloomMax` | Additive random spread cone that grows with ShotIndex (curved by HipfireBloomCurve). |
| `HipfireBloomShots` | Shot count until max bloom is reached. |
| `InspectTime` | Inspect animation duration in seconds (gates ADS). |
| `MagazineSize` | Shots per magazine (e.g. 30 for AR, 7 for AWP). |
| `MaxReserveAmmo` | Maximum reserve ammo outside the magazine (e.g. 90 for AR, 30 for AWP). |
| `MoveSpeedMul` | Base hip-fire move-speed multiplier on top of Walk/Sprint speed; stacks with `AdsSpeedMul` when aiming. |
| `MovementSpread` | Maximum movement spread at sprint speed (additive on HipfireBaseSpread). |
| `ReloadTime` | Reload duration in seconds (hardcoded per weapon, gates fire while reloading). |
| `ShootBodyClips` | Main muzzle "boom" layer. |
| `ShootDistantClips` | Distance variant ("boom" instead of "crack") for remote shooters. |
| `ShootMechClips` | Mechanical layer (bolt/action) — dry. |
| `ShootTailClips` | Optional reverb tail sample. |
| `ShootVolumeDb` | Per-weapon level offset on the shot sound. |
| `SprintSpeedMul` | Move-speed multiplier while sprinting (separate sprint penalty from `MoveSpeedMul`). |

## Methods

| Name | Summary |
|------|---------|
| `DamageFor(HitboxGroup)` | Damage lookup by hitbox group. Falls back to `Body`, then 25 if neither is set. |
