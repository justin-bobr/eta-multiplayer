# FireInput

`Vantix.Character.FireInput`

Per-tick weapon input (fire/reload/ads plus shooter state) consumed by the fire logic.

## Properties

| Name | Summary |
|------|---------|
| `AimDirection` | Forward unit vector derived from yaw and pitch — the server raycasts from ShooterPosition along this. |

## Fields

| Name | Summary |
|------|---------|
| `AdsHeld` | Held state for aim-down-sights. |
| `CanFire` | Gameplay flag (e.g. Dead). |
| `FirePressed` | True from any source (Mouse1 or fire key). |
| `InspectPressed` | Held state - MovementController detects the press edge itself. |
| `ReloadPressed` | Held state - MovementController detects the press edge itself. |
| `ShooterPosition` | Shooter position at this tick - used by server-side lag compensation. |
| `Speed` | Horizontal speed for spread scaling. |
| `TickIndex` | Sequence number - the server rewinds the world snapshot to this tick. |
| `ViewPitch` | Aim pitch - server raycast direction. |
| `ViewYaw` | Aim yaw - server raycast direction. |
