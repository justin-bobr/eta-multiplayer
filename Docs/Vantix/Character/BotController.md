# BotController

`Vantix.Character.BotController`

Drives a bot's `NetInputSource` so it walks the map via `NavigationServer3D`. Queries `MapGetPath` to a randomly chosen target (Zone/BombSpot centre from `targetCandidates`); picks a new target on arrival. Short-range steering is reactive: probes the desired heading and offset headings each tick and takes the first clear lane, handling dynamic obstacles the static NavMesh ignores. Stuck-detection re-requests a path. Bots walk only (no sprint). Pure logic class, ticked by `Poll` before `FeedInputsToAgents`. Raycast query/result are pooled for zero per-tick allocation.

## Fields

| Name | Summary |
|------|---------|
| `ArriveRadius` | Distance below which the current target counts as "reached". |
| `EyeHeight` | Standing eye height; must match NetworkPlayer.StandEyeHeight so the LOS ray matches hitscan. |
| `LowProbeHeight` | Knee-level probe height for low obstacles (crates, fences). |
| `MaxEngagementDistance` | Max engagement distance; beyond this, bots ignore targets. |
| `MaxTicksPerTarget` | Hard timeout per target (~5s at 128 Hz); after this the path is assumed unreachable. |
| `MaxYawTurnPerTick` | Max body-yaw turn per tick (radians). 0.05 ≈ 366°/sec. |
| `MinTargetDistanceSquared` | Picks a fresh target from `targetCandidates` and queries `MapGetPath` for the waypoint sequence to reach it. Falls back to a single-waypoint path (the candidate) when the NavMesh returns nothing. |
| `ProbeDistance` | Forward avoidance probe distance (~1s reaction window at walk speed). |
| `ProbeHalfWidth` | Half-width of the body capsule footprint, used for edge probes (~capsule radius). |
| `ProbeHeight` | Chest-level probe height for wall detection. |
| `ProbeOffsets` | Yaw offsets (radians) tried in order when the heading is blocked; symmetric pairs of increasing magnitude. Capped at ±1.4 rad (~80°); wider angles caused spinning in tight corners. |
| `StuckCheckTicks` | Window for the stuck check: if the bot moved less than `StuckMinMovedMeters` in this many ticks, re-pick the target. |

## Methods

| Name | Summary |
|------|---------|
| `ComputeAimPoint(Vector3, int)` | Aim point above enemy feet. Lower difficulty aims lower; higher aims at the head. Diff 2 alternates body/head per acquire via `_aimAtHead`. |
| `DetectBestEnemy(Vector3, PhysicsDirectSpaceState3D, Vantix.Character.BotCombatContext)` | Returns the closest hostile, alive, in-range peer with clear LOS; null if none. Skips self, same-team, dead, and unspawned peers. |
| `FindClearYaw(PhysicsDirectSpaceState3D, Vector3, float, uint)` | Returns the first yaw (desired heading or a wider offset) whose full capsule footprint has a clear lane. Keeps last tick's offset while still clear (hysteresis) to avoid oscillation. Returns desiredYaw if all offsets are blocked; stuck-detection then re-requests a path. |
| `GetCombatYawTurnPerTick(int, int)` | Max yaw turn per tick during combat. Higher difficulty = faster snap-to-aim. Equivalent rates: diff0 ≈ 170°/s, diff1 ≈ 290°/s, diff2 ≈ 460°/s, diff3 ≈ 800°/s. |
| `GetReactionTicks(int, int)` | Reaction delay before first shot, in ticks. Applied on every fresh acquire and re-acquire. Derived from a per-difficulty ms target via the tick rate so it's identical at 64 vs 128 Hz. |
| `HasFullLineOfSight(PhysicsDirectSpaceState3D, Vector3, Vector3)` | True iff rays from eye to enemy head, chest and waist are all clear of world geometry. Mask=1 (world only) excludes server-agent bodies, which would otherwise self-block the target. |
| `Init(Vector3, float, uint)` | Ties this controller to a body. Idempotent — re-calling resets state (respawn). |
| `IsLaneClear(PhysicsDirectSpaceState3D, Vector3, float, uint)` | True iff six probes (left/centre/right × chest/knee) along `yaw` are all clear. Forward = (-sin yaw, 0, -cos yaw) per `AimDirection`. |
| `ProbeRay(PhysicsDirectSpaceState3D, Vector3, Vector3)` | Single-ray helper for `IsLaneClear`; true if the segment is clear. |
| `Tick(uint, Vector3, Rid, IReadOnlyList<Vector3>, PhysicsDirectSpaceState3D, uint, Vantix.Character.BotCombatContext)` | Produces the `InputPacket` for this tick. If a visible enemy is in range, turn toward it and fire once aim is aligned and the reaction delay has elapsed; otherwise wander the NavMesh path, requesting a new path on arrival or when stuck. |
