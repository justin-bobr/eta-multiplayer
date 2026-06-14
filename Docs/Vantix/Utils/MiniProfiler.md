# MiniProfiler

`Vantix.Utils.MiniProfiler`

Thread-safe per-method timing profiler with three layers: _current (per-frame aggregate), _last (last `FlushFrame` snapshot for the HUD), and _window (cumulative, dumped by `WriteReport`). Use `using var _ = MiniProfiler.SampleServer/SampleClient("Name")`. Zero-cost when `ProfilingEnabled` is false (Sample returns a no-op scope).

## Fields

| Name | Summary |
|------|---------|
| `PeakHoldMs` | How long a recorded peak survives report resets (ms). Totals/counts stay per-window. |
| `ProfilingEnabled` | Master switch; when false all Sample() calls return a no-op scope. Set from cl_/sv_profiler. |
| `WindowEntry.PeakAtMs` | TickCount64 when the peak was last raised; peaks survive report resets for `PeakHoldMs`. |

## Methods

| Name | Summary |
|------|---------|
| `FlushFrame()` | Swaps _current into _last (zero-alloc) and warns on samples over `WarnThresholdMs` when `WarnEnabled`. |
| `ResetWindow()` | Clears all cumulative window data; per-frame _current/_last are untouched. |
| `Sample(string)` | Begins a timing scope that stops on disposal; no-op scope when ProfilingEnabled is false. |
| `SampleClient(string)` | Sample with the [CL] prefix for client-side code (HUD, player, puppet, FX). |
| `SampleServer(string)` | Sample with the [SV] prefix for server-side code; distinguishes origin in listen mode. |
| `TopSamples(int)` | Top-N samples by total time from the last snapshot. Returns a reused buffer; consume immediately, do not store. |
| `WriteReport(string, string, double)` | Writes a per-window report for samples matching the prefix, resetting those window slots after. Main-thread only (static reuse buffers aren't thread-safe); file I/O runs on a background thread. |
