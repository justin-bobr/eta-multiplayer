# LinuxDisplay

`Vantix.Platform.LinuxDisplay`

Linux equivalent of `Win32Display` — uses the `xrandr` CLI to programme the X11 monitor scanout mode. Wayland disallows app-level mode-change, so `IsSupported` is false there and the caller falls back to Godot's ExclusiveFullscreen. Unlike Win32's CDS_FULLSCREEN, X11 does not auto-restore on focus loss — the original mode is tracked and restored explicitly on `Reset`, which the caller must invoke on exit / mode change.

## Properties

| Name | Summary |
|------|---------|
| `AppliedResolution` | Resolution this backend currently holds an override at; zero when none. |

## Methods

| Name | Summary |
|------|---------|
| `EnumModes(int)` | Lists the monitor's supported physical modes (parsed from `xrandr --query`), sorted by pixel count; modes above native are pruned. |
| `GetNativeResolution(int)` | Returns the monitor's current physical resolution from xrandr; falls back to `ScreenGetSize`. |
| `Reset()` | Restores the original mode active before `TrySetMode`. X11 has no auto-restore on focus-loss. |
| `ResolveOutputForMonitor(int, Vector2I)` | Maps a Godot screen index to an xrandr output name by matching each output's geometry against `ScreenGetPosition`/`ScreenGetSize`; falls back to primary. |
| `TrySetMode(int, int, int, int)` | Programmes the monitor scanout mode (best-effort). Returns false if xrandr is missing, the output doesn't advertise the mode, or the X server rejects it — caller falls back to native fullscreen. |
