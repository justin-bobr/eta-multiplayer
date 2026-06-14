# SvConVars

Server-authoritative ConVars (sv_*). Gameplay-relevant, must match on server and client.

## Fields

| Name | Summary |
|------|---------|
| `AntiCheatAutoKick` | Auto-disconnect peers exceeding `AntiCheatKickThreshold` violations inside `AntiCheatViolationWindowMs`. Off = violations are only logged and counted. |
| `AntiCheatEnabled` | Master toggle for all anti-cheat detection. False = no detection, no violations, no kicks. |
| `AntiCheatKickThreshold` | Violations-within-window threshold that triggers a kick. |
| `AntiCheatViolationWindowMs` | Sliding window (ms) for grouping violations; older ones age out. |
| `BotDifficulty` | Bot combat skill (0-3, clamped). Higher = faster reaction + better aim point. 0 = ~500ms, aims at feet; 1 = ~350ms, body; 2 = ~200ms, body/head; 3 = ~80ms, head. |
| `DebugAimRay` | Clients render a yellow ray from camera to the server aim endpoint (uses Snapshot.Yaw/Pitch + AimPunch). |
| `DebugBullets` | Red markers (5s) at server-authoritative hit positions of own shots; compare vs client decals to find drift. |
| `DebugCapsule` | Clients render a red body capsule at the last server position per puppet (uses Snapshot.Pos). |
| `DebugHitboxes` | Broadcast hitbox transforms at 10 Hz; clients render red capsules/spheres at server hitbox positions. |
| `FogOfWar` | Fog of War: server strips enemies with no line-of-sight (and their position-leaking events) from receivers via a precomputed voxel-PVS; teammates and self always visible. Falls back to `PvsCutoffDistance` when off. Default off — the blocking build freezes the server 10-30s on first map load until made incremental. Opt-in via `sv_fog_of_war 1`. |
| `FowVoxelSize` | Voxel cell size (m) for `VoxelPvs`. Smaller = finer occlusion at N² memory/build cost. 4m de_dust2 sweet spot; 2.5m for tight maps, 6m for large open ones. |
| `MaxClientPacketsPerServerTick` | Per-peer cap on InputPackets processed in one server tick. 8 covers legit jitter/batch bursts; excess is dropped and counted as a violation. |
| `MaxClientPositionDeltaMps` | Max plausible position-delta per server-tick (m/s); sustained motion above is a bug or bypassed clamp. |
| `MaxClientTickAheadOfServer` | Max ticks the client's `TickIndex` may run ahead of the server (≈500ms RTT at 128 Hz); beyond this = spoof or clock-attack. |
| `MaxClientYawRateRadPerSec` | Max plausible yaw rate (rad/s). 250 ≈ 14000°/s — above pro flick peaks, flags snap-aim bots. |
| `NoRewind` | Disables lag-comp bone rewind — casts use live hitbox positions. Isolates rewind vs handoff misses. |
| `Profiler` | Server-side profiler: periodic warning for [SV] samples over `ProfilerThresholdMs`. In listen-mode reads the HUD-flushed snapshot to avoid a double clear. |
| `ProfilerThresholdMs` | Warning threshold (ms) for sv_profiler. ~25% of the 128 Hz tick budget. |
| `PvsCutoffDistance` | Distance-based PVS cutoff (Manhattan metres) for snapshot broadcasting; teammates always kept. 0 disables PVS (broadcast everything). |
| `StepUpSpeedPenalty` | Fraction of horizontal speed bled off on each step-up, scaled by step height. 0 = no penalty. |
| `WallClingPostJumpGrace` | Grace window (s) after a regular jump during which wall-cling cannot trigger. |
