# ClConVars

Client-side ConVars (cl_*). Local only, cosmetic, each player has own values.

## Fields

| Name | Summary |
|------|---------|
| `InterpLockTicks` | Puppet interpolation delay. 0 = adaptive, tracking `JitterDownMs` within [`InterpMinTicks`, `InterpMaxTicks`]. >0 = lock to this tick count; competitive play wants `cl_interp_lock 6` to match the server's 6-tick lag-comp rewind. |
| `InterpMaxTicks` | Upper bound for adaptive interp delay (ticks). 12 ≈ 94ms at 128Hz; past this the puppet feels delayed. |
| `InterpMinTicks` | Lower bound for adaptive interp delay (ticks). 3 ≈ 23ms at 128Hz. |
| `JumpMinFallHeight` | Local jump/fall detection: an air cycle counts only on a jump press or a fall past this height (m). Height, not speed, separates descending stairs from a genuine drop. |
| `JumpMinFallSpeed` | Impact-speed gate for the puppet land sound (only impact is broadcast, not fall height). |
| `LocoAdsBobScale` | Locomotion bob multiplier while ADS (0..1). |
| `LocoShiftBobScale` | Shift-walk locomotion bob magnitude (0..1). |
| `LocoSpeedSmoothRate` | Low-pass rate for the speed driving the pose blend (lower = smoother). Cosmetic; real velocity unaffected. |
| `LocoSprintBobScale` | Sprint head-bob scale (0..1); 1 = full baked sprint bob. |
| `LocoWalkBobScale` | Normal-walk locomotion bob magnitude (0..1); 1 = full baked bob. |
| `MPitch` | Per-axis pitch multiplier on raw mouse Relative.Y. Defaults to `MYaw`; lower for a slower vertical curve. |
| `MYaw` | Per-axis yaw multiplier on raw mouse Relative.X. Source-derived 0.022 default. Tune via `cl_m_yaw`. |
| `Profiler` | Toggles the `HudMiniProfiler` overlay — shows only samples above `ProfilerThresholdMs`. |
| `ProfilerThresholdMs` | Threshold (ms) for profiler warnings shown in the HUD and logged. Raise if too spammy. |
| `ReconBleedLarge` | Visual-bleed rate (1/s) for large drifts; lower than `ReconBleedNormal` so big recoveries stay smooth. 3.0 ≈ 333ms. |
| `ReconBleedLargeThresholdM` | Drift (m) above which `ReconBleedLarge` replaces `ReconBleedNormal`. |
| `ReconBleedNormal` | Visual-bleed rate (1/s) for the post-reconcile offset on small drift (≤ `ReconBleedLargeThresholdM`). 6.5 ≈ 154ms. |
| `ReconSnapThresholdM` | Drift (m) above which the visual offset hard-snaps to zero instead of bleeding. Set high to disable. |
| `SprintBlurBlendSpeed` | Blend speed for the peripheral sprint-blur fade (separate from FovBlendSpeed). Lower = gentler. |
| `StepSmoothEnabled` | Cosmetic local-only camera step-smoothing; rate = catch-up speed. |
