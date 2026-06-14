# MovementController

Pure movement logic. Tuning is injected via `Sv` (default `Sv`). Deterministic and free of Node3D/physics calls, so it runs inside the server tick and in replay.

## Properties

| Name | Summary |
|------|---------|
| `AdsBlendVisual` | Smoothstepped variant of AdsBlend for visual use (pose, FOV, sensitivity). Gameplay code uses the linear AdsBlend. |
| `BreathBreathingMul` | Multiplier for the breathing oscillation only — less damped during a hold than the rest, amplified during recover. |
| `BreathSwayMul` | Sway multiplier for mouse inertia/lean/bob/tilt (all except breathing). 1 = neutral, <1 during an active hold, >1 during recover. |
| `DidDryFireThisFrame` | True on the tick the player clicked on an empty magazine (dry-fire). One-tick edge. |
| `DidReloadThisFrame` | True on the tick any reload started. One-tick edge; drives the networked mag-drop. |
| `HorizontalSpeed` | Horizontal speed magnitude (X/Z only). Inlined sqrt to avoid a per-call Vector3 ctor in hot paths. |
| `IsAirborne` | Public read-only access for external systems (e.g. the mantle check in NetworkPlayer). |
| `LastReloadMoved` | Public read-only — last reload bullet count moved (used by SFX/HUD/replay). |
| `LastShotDirection` | World direction of the last shot including aim punch, pattern and spread. Unit vector. |
| `LastShotOrigin` | World origin of the last shot (server truth, lag-comp replayable). Camera/eye position. |
| `LastWishDir` | Last frame's body-local WishDir (used by the mantle intent check). |

## Fields

| Name | Summary |
|------|---------|
| `AdsBlend` | 0 = hipfire, 1 = full ADS. Lerps with WeaponStats.AdsBlendTime. |
| `AimPunch` | Gameplay aim shift (server-authoritative). |
| `BreathCooldownTimer` | Greater than zero during the post-recover cooldown when no new hold can be started. |
| `BreathHoldActiveNow` | Computed each tick: true while the hold is effectively active (drives the sway multiplier). |
| `BreathHoldTimer` | Remaining hold stamina in seconds — drains during active hold, regenerates otherwise. |
| `BreathRecoverTimer` | Greater than zero while in the shaky recover phase (sway amplified). |
| `FireMode` | 0 = automatic, 1 = single-shot. |
| `InspectTimer` | Greater than zero while the inspect animation is playing, ADS blocked. |
| `PreMoveHorizSpeed` | Horizontal speed captured before MoveAndSlide. Set by NetworkPlayer and used by the wall-jump check so that wall-absorbed velocity does not kill the speed gate. |
| `ReloadTimer` | Greater than zero means a reload is in progress and fire is blocked. |
| `SlideStopAccuracyTimer` | Greater than zero while still inside the slide-stop accuracy window. The first shot fired in this window benefits from the SlideStopAccuracySpreadMul multiplier. |
| `SubtickFireViewYaw` | Sub-tick fire-press view yaw, captured at the Fire-bit rising edge inside an event. When `SubtickFireViewValid`, the fire input uses this instead of the tick-end view so a mid-flick shot aims at the press moment. Reset to invalid at the top of every `Step`. |
| `Sv` | Tuning reference. Default = global `Sv`. Replaceable for tests. |
| `UnlimitedAmmo` | Test mode: bullets are not decremented and reserves are refilled forever. |
| `WallClingChargesRemaining` | Negative one means uninitialized — lazy-initialized from Sv.WallClingChargesPerSpawn on first tick. |
| `WallClingEntrySpeed` | Horizontal speed at cling entry, saved so the cling-exit jump can bypass the wall-jump speed floor. |
| `WallJumpAvailable` | One wall jump per airtime, reset on landing. |
| `WeaponRaiseBlend` | 1 = fully raised (fire-ready), 0 = fully lowered (sprint). |

## Methods

| Name | Summary |
|------|---------|
| `ApplyGravity(Vector3, bool, float)` | Applies gravity to vertical velocity. Includes an apex-hang reduction for floaty jumps. |
| `ApplyHorizontalMovement(Vector3, MovementInput, float, float)` | Source/CS-style horizontal movement: ground friction (with a stopspeed floor) then wish-direction acceleration up to wishspeed; counter-strafe falls out of the addspeed formula. Air branch uses Quake-style strafe accel independent of ground tuning. |
| `ComputeTargetSpeed(MovementInput)` | Computes the desired horizontal speed for this tick based on sprint / shift / walk plus the crouch and ADS multipliers. |
| `DoFire(FireInput)` | Performs the actual shot: computes pattern + spread + bloom and updates AimPunch and the LastShot* outputs. |
| `FireStep(FireInput)` | Server-replayable fire step. Updates cooldown, ShotIndex, AimPunch and the computed outputs. |
| `InitializeAmmo(WeaponStats)` | Initializes ammo from weapon stats (full mag plus full reserve). Called by NetworkPlayer on spawn and on weapon switch. Server-authoritative — the client replicates this. |
| `ResetSpawnConsumables()` | Resets per-spawn consumables (wall-cling charges, breath-hold stamina, etc.). Called by NetworkPlayer on respawn. |
| `Restore(MovementSnapshot)` | Restores the full state from a `MovementSnapshot` (reconciliation rollback). The caller then replays the ticks after the snapshot. |
| `RunSubStep(MovementInput)` | One inner physics segment over `Dt` (a tick fraction for subtick callers, or the full tick legacy). Does not reset the once-per-tick flags managed by `Step`. |
| `Snapshot()` | Builds a full state snapshot (see `MovementSnapshot`). Value-type return — no allocation, safe to call every tick. |
| `Step(MovementInput)` | Server-replayable movement step. With non-empty `Events`, the tick is split into segments at each event's TFraction and the inner step runs per segment. Tick-level once-per-step flags (`DidJumpThisFrame`, `DidWallJumpThisFrame`, `LastWishDir`) are written here, not in the inner step. Empty Events runs a single legacy segment over the full Dt. |
| `TryCrouchCancelJump(Vector3, MovementInput)` | Crouch-cancel jump: a crouch press inside the narrow apex window grants a small vertical boost. One-shot per airtime, reset on landing or a new jump. |
| `TryJump(Vector3, MovementInput)` | Handles regular jumps, wall jumps, and the coyote-time / jump-buffer / crouch-buffer windows. |
| `UpdateAdsBlend(MovementInput, Vector3, float)` | Tracks airborne state from `OnFloor` (deterministic) and lerps the ADS blend toward its target with the per-weapon blend time. |
| `UpdateBreathHold(MovementInput, float)` | Breath-hold three-phase state machine (hold → recover → cooldown → idle/regen). Initial values come from `ResetSpawnConsumables`. |
| `UpdateCrouchBlend(bool, float)` | Moves the crouch blend toward 1 (crouched) or 0 (standing). |
| `UpdateSlide(Vector3, MovementInput, float)` | Slide state machine. Initiate on crouch-press during a fast sprint, apply friction over time, end on slow speed, timeout, crouch release, or air; open the slide-stop accuracy window on end. |
| `UpdateStamina(MovementInput)` | Drives sprint/stamina state including drain, regen, exhaustion and auto-resume. |
| `UpdateWallCling(Vector3, MovementInput, float)` | Wall-cling state machine. Freezes the player against a wall after a sprint+jump approach; a re-jump triggers a regular wall jump with the saved entry speed bypassing the speed floor. Limited per spawn by `WallClingChargesRemaining`. |
| `UpdateWeaponRaiseBlend(float)` | Lerps the weapon raise blend toward 0 while sprinting (lowered) or 1 otherwise. Reload overrides sprint-lower so the weapon stays raised while reloading. |
| `WishDirFromBits(InputBits)` | WASD bitmask → unit local-space wish direction. X = strafe right positive, Z = back positive (matches `WishDir` convention). Returns Vector3.Zero if no movement bits. |
