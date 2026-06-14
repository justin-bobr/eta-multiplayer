# Win32Display

`Vantix.Platform.Win32Display`

Direct Win32 monitor mode-change for sub-native exclusive-fullscreen. Calls `user32!ChangeDisplaySettingsEx` with `CDS_FULLSCREEN`: reprograms the monitor scanout to the requested mode (the panel's hardware scaler upscales), and Windows auto-restores the desktop mode on focus-loss/exit/crash. On non-Windows `IsSupported` is false and the caller falls back to Godot's ExclusiveFullscreen at native res.

## Properties

| Name | Summary |
|------|---------|
| `HasAppliedMode` | True while a mode-override is active on a monitor (signals whether Reset() is needed on leaving ExclusiveFullscreen). |

## Methods

| Name | Summary |
|------|---------|
| `EnumModes(int)` | Enumerates the monitor's advertised resolutions via `EnumDisplaySettings` (unique width/height pairs), sorted by pixel count; modes above native are pruned. |
| `GetDeviceNameForMonitor(int)` | Resolves the Win32 device name (e.g. `\\.\DISPLAY1`) for the monitor at `godotScreenIndex` via `EnumDisplayDevices` (OS enumeration order). Robust under multi-DPI, where a coordinate-based lookup would land on the wrong monitor. |
| `GetNativeResolution(int)` | Reads the monitor's current physical resolution in raw pixels, bypassing DPI-scaling. Needed because `ChangeDisplaySettingsEx` takes physical pixels. |
| `GetWindowsDisplayNumber(int)` | Extracts the Windows display number from a device name (`\\.\DISPLAY3` → `3`) so dropdown labels match Windows Settings → Display. |
| `IsPrimaryMonitor(int)` | True if the monitor at the given Godot screen index is Windows' primary display. |
| `Reset()` | Restores the original desktop mode on the last-touched monitor (for the in-session switch-away case; CDS_FULLSCREEN handles focus-loss/exit automatically). |
| `TrySetMode(IntPtr, int, int, int)` | Changes the monitor mode for the screen owning `hwnd`. Two-phase: CDS_TEST first (no visible blink), then CDS_FULLSCREEN apply. Returns false on any failure — caller falls back to native fullscreen. |
