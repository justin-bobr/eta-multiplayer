# MovementInput

`Vantix.Character.MovementInput`

Per-tick movement input (wishdir, view angles, buttons) for the deterministic movement step.

## Properties

| Name | Summary |
|------|---------|
| `AimDirection` | Aim direction computed from ViewYaw and ViewPitch (unit forward vector used by server hitscan). |
| `BodyBasis` | Body basis derived from ViewYaw, used to transform WishDir into world space. |

## Fields

| Name | Summary |
|------|---------|
| `AdsHeld` | Right-mouse hold. Blocks sprint and enables the ADS blend. |
| `BreathHoldHeld` | Hold to dampen sway while in ADS for a few seconds before a shaky recover phase begins. |
| `CrouchPressed` | Press-edge of the crouch key — used to initiate slides. |
| `Events` | Subtick events ordered by `TFraction` ascending, or null/empty for the legacy single-segment path. See struct header for routing. |
| `InitialBits` | Held-input bitmask at the START of the tick (t=0). Used by the subtick path for the first segment before any event applies. Ignored on the legacy path. |
| `InitialViewPitch` | View pitch at the start of the tick. Ignored on the legacy path. |
| `InitialViewYaw` | View yaw at the start of the tick. Ignored on the legacy path. |
| `JumpPressed` | Press-edge of the jump key (not held). Used for regular jumps so bunny-hopping is impossible. |
| `OnFloor` | Used for gravity and regular jumps. Server-derived physics truth. |
| `TickIndex` | Sequence number used by replay and reconciliation. |
| `TouchingWall` | Used for wall jumps. Server-derived physics truth. |
| `ViewPitch` | Head pitch in radians, used for the aim direction. |
| `ViewYaw` | Body yaw in radians. |
| `WallNormal` | World-space wall normal. Server-derived physics truth. |
| `Weapon` | Currently selected weapon — required for ADS speed multiplier and ADS blend time. |
| `WishDir` | Local-space input vector (X = strafe right positive, Z = back positive). |
