# Dbg

Central debug-logging gate driven by the "global/debug" project setting. `Enabled` is read once and cached; real errors still go through GD.PrintErr ungated. `Print` uses an interpolated-string handler so `Dbg.Print($"hp={hp}")` allocates nothing when disabled.

## Properties

| Name | Summary |
|------|---------|
| `Enabled` | True when the project setting "global/debug" is active. |

## Methods

| Name | Summary |
|------|---------|
| `Print(PrintInterpolatedStringHandler)` | Like GD.Print but only emits when `Enabled`; zero-cost when disabled via the handler. |
| `Print(string)` | Plain-string overload for non-interpolated literals. |
