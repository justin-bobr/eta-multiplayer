using Godot;
using System;
using System.Runtime.InteropServices;

/// <summary>
/// Direct Win32 monitor mode-change — the equivalent of what CS2 / CoD do for exclusive-fullscreen
/// sub-native resolutions. CS2 calls DXGI's <c>IDXGISwapChain::ResizeTarget</c>; we call
/// <c>user32!ChangeDisplaySettingsEx</c> with the <c>CDS_FULLSCREEN</c> flag — same end-effect:
///
///   • Monitor scanout is reprogrammed to the requested mode (e.g. 4 K panel → 1920×1080).
///   • The 4 K panel's own hardware-scaler upscales the 1080p signal (lowest possible latency).
///   • While our app is the foreground window, Windows holds the mode.
///   • The instant our app loses focus (Alt-Tab) OR exits cleanly OR crashes, Windows restores the
///     desktop's original mode automatically — this is what CDS_FULLSCREEN promises.
///
/// Godot 4's <see cref="DisplayServer"/> deliberately doesn't expose monitor mode-change because the
/// engine targets ~12 platforms and there's no shared abstraction. So we drop to Windows-native here.
/// On non-Windows OSes <see cref="IsSupported"/> is false and the caller must fall back to Godot's
/// plain ExclusiveFullscreen (= takes screen at native res).
/// </summary>
public static class Win32Display
{
	public static bool IsSupported => OS.GetName() == "Windows";

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct DEVMODE
	{
		private const int CCHDEVICENAME = 32;
		private const int CCHFORMNAME = 32;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
		public string dmDeviceName;
		public short dmSpecVersion;
		public short dmDriverVersion;
		public short dmSize;
		public short dmDriverExtra;
		public int dmFields;
		public int dmPositionX;
		public int dmPositionY;
		public int dmDisplayOrientation;
		public int dmDisplayFixedOutput;
		public short dmColor;
		public short dmDuplex;
		public short dmYResolution;
		public short dmTTOption;
		public short dmCollate;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
		public string dmFormName;
		public short dmLogPixels;
		public int dmBitsPerPel;
		public int dmPelsWidth;
		public int dmPelsHeight;
		public int dmDisplayFlags;
		public int dmDisplayFrequency;
		public int dmICMMethod;
		public int dmICMIntent;
		public int dmMediaType;
		public int dmDitherType;
		public int dmReserved1;
		public int dmReserved2;
		public int dmPanningWidth;
		public int dmPanningHeight;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT { public int left, top, right, bottom; }

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MONITORINFOEX
	{
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string szDevice;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct DISPLAY_DEVICE
	{
		public int cb;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string DeviceName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceString;
		public uint StateFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceID;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceKey;
	}
	private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
	private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

	private const int DM_PELSWIDTH = 0x80000;
	private const int DM_PELSHEIGHT = 0x100000;
	private const int DM_DISPLAYFREQUENCY = 0x400000;

	private const int CDS_FULLSCREEN = 0x4;
	private const int CDS_TEST = 0x2;
	private const int DISP_CHANGE_SUCCESSFUL = 0;
	private const int MONITOR_DEFAULTTONEAREST = 2;

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT { public int x; public int y; }

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern int ChangeDisplaySettingsEx(
		string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

	[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "ChangeDisplaySettingsExW")]
	private static extern int ChangeDisplaySettingsExReset(
		string lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

	[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "EnumDisplaySettingsExW")]
	private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, int dwFlags);

	private const int ENUM_CURRENT_SETTINGS = -1;

	private static string _appliedDevice;
	private static Vector2I _appliedResolution;
	/// <summary>True while we have an active mode-override on a specific monitor. Used by
	/// <see cref="Settings.ApplyWindowModeAndResolution"/> to know whether a Reset() is needed
	/// when the user leaves ExclusiveFullscreen.</summary>
	public static bool HasAppliedMode => _appliedDevice != null;
	public static Vector2I AppliedResolution => _appliedResolution;

	/// <summary>Attempt to change the monitor mode for the screen that owns <paramref name="hwnd"/>.
	/// Two-phase: CDS_TEST first to detect unsupported modes without the visible blink, then real
	/// CDS_FULLSCREEN apply if the test passed. Returns false on any failure (monitor doesn't list
	/// the mode, driver refuses, multi-GPU edge case) — caller should fall back to plain native
	/// fullscreen and inform the user.</summary>
	public static bool TrySetMode(IntPtr hwnd, int width, int height, int refreshHz = 0)
	{
		if (!IsSupported) return false;

		string device = GetDeviceNameForWindow(hwnd);
		if (string.IsNullOrEmpty(device))
		{
			GD.PrintErr($"[Win32Display] could not resolve monitor device name for hwnd {hwnd}");
			return false;
		}

		var dm = new DEVMODE
		{
			dmSize = (short)Marshal.SizeOf<DEVMODE>(),
			dmPelsWidth = width,
			dmPelsHeight = height,
			dmFields = DM_PELSWIDTH | DM_PELSHEIGHT,
		};
		if (refreshHz > 0)
		{
			dm.dmDisplayFrequency = refreshHz;
			dm.dmFields |= DM_DISPLAYFREQUENCY;
		}

		int test = ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
		if (test != DISP_CHANGE_SUCCESSFUL)
		{
			GD.PrintErr($"[Win32Display] mode {width}×{height}@{refreshHz}Hz NOT supported on {device.Trim()} (test={test})");
			return false;
		}

		int result = ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, CDS_FULLSCREEN, IntPtr.Zero);
		if (result == DISP_CHANGE_SUCCESSFUL)
		{
			_appliedDevice = device;
			_appliedResolution = new Vector2I(width, height);
			GD.Print($"[Win32Display] mode-change OK: {device.Trim()} → {width}×{height}@{(refreshHz > 0 ? refreshHz + "Hz" : "auto")}");
			return true;
		}
		GD.PrintErr($"[Win32Display] mode-change FAIL: {device.Trim()} → {width}×{height} (code={result})");
		return false;
	}

	/// <summary>Manually restore the original desktop mode on whichever monitor we last touched.
	/// CDS_FULLSCREEN auto-restores on focus-loss and process-exit, but we still need an explicit
	/// path for the in-session "user switched away from ExclusiveFullscreen" case.</summary>
	public static void Reset()
	{
		if (!IsSupported || _appliedDevice == null) return;
		ChangeDisplaySettingsExReset(_appliedDevice, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
		GD.Print($"[Win32Display] mode-restored: {_appliedDevice.Trim()}");
		_appliedDevice = null;
		_appliedResolution = Vector2I.Zero;
	}

	private static string GetDeviceNameForWindow(IntPtr hwnd)
	{
		IntPtr hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
		if (hmon == IntPtr.Zero) return null;
		var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
		return GetMonitorInfo(hmon, ref mi) ? mi.szDevice : null;
	}

	/// <summary>Resolves the Win32 device name (e.g. <c>\\.\DISPLAY1</c>) for the monitor at
	/// <paramref name="godotScreenIndex"/>. Uses <c>EnumDisplayDevices</c> in Windows enumeration
	/// order (= the same order the OS uses for "Display 1, Display 2, Display 3" in the Settings
	/// app). Robust against multi-DPI setups where the older <c>MonitorFromPoint</c> approach
	/// landed in the wrong monitor because Godot's position coords are DPI-scaled but the Win32
	/// desktop coordinate space is physical pixels.</summary>
	public static string GetDeviceNameForMonitor(int godotScreenIndex)
	{
		if (!IsSupported) return null;
		int counted = 0;
		for (uint i = 0; i < 32; i++)
		{
			var dev = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
			if (!EnumDisplayDevices(null, i, ref dev, 0)) break;
			if ((dev.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
			if (counted == godotScreenIndex) return dev.DeviceName;
			counted++;
		}
		return null;
	}

	/// <summary>Extracts the Windows-visible display number from a device name like
	/// <c>\\.\DISPLAY3</c> → <c>3</c>. Used by <see cref="SettingsMenu"/> so the dropdown labels
	/// match what the user sees in Windows Settings → Display ("Display 1 / 2 / 3 / ...") instead
	/// of an internal 0-based Godot index that doesn't line up with the OS.</summary>
	public static int GetWindowsDisplayNumber(int godotScreenIndex)
	{
		string device = GetDeviceNameForMonitor(godotScreenIndex);
		if (string.IsNullOrEmpty(device)) return godotScreenIndex + 1;
		int dot = device.LastIndexOf("DISPLAY", System.StringComparison.OrdinalIgnoreCase);
		if (dot < 0) return godotScreenIndex + 1;
		string numStr = device.Substring(dot + "DISPLAY".Length);
		return int.TryParse(numStr, out int n) ? n : godotScreenIndex + 1;
	}

	/// <summary>True if the monitor at the given Godot screen index is Windows' primary display.</summary>
	public static bool IsPrimaryMonitor(int godotScreenIndex)
	{
		if (!IsSupported) return false;
		int counted = 0;
		for (uint i = 0; i < 32; i++)
		{
			var dev = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
			if (!EnumDisplayDevices(null, i, ref dev, 0)) break;
			if ((dev.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
			if (counted == godotScreenIndex)
				return (dev.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
			counted++;
		}
		return false;
	}

	/// <summary>Reads the monitor's CURRENT (= the desktop mode it's running right now) PHYSICAL
	/// resolution in raw pixels. Bypasses any Windows DPI-scaling that <see cref="DisplayServer.ScreenGetSize"/>
	/// might apply — necessary for sub-native mode-change calculations because
	/// <c>ChangeDisplaySettingsEx</c> takes physical pixels, not scaled coords.</summary>
	public static Vector2I GetNativeResolution(int godotScreenIndex)
	{
		if (!IsSupported) return Vector2I.Zero;
		string device = GetDeviceNameForMonitor(godotScreenIndex);
		if (device == null) return Vector2I.Zero;
		var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
		if (!EnumDisplaySettingsEx(device, ENUM_CURRENT_SETTINGS, ref dm, 0))
			return Vector2I.Zero;
		return new Vector2I(dm.dmPelsWidth, dm.dmPelsHeight);
	}

	/// <summary>Enumerates every resolution the monitor advertises via <c>EnumDisplaySettings</c>
	/// (refresh-rate / colour-depth variants collapsed to unique (width, height) pairs). Returns
	/// the list sorted by total pixel count ascending — this is the CS2-style "show every mode the
	/// hardware actually supports across all aspect ratios" data source. Modes above the current
	/// native are pruned because they would scale internally (rare and pointless).</summary>
	public static Vector2I[] EnumModes(int godotScreenIndex)
	{
		if (!IsSupported) return System.Array.Empty<Vector2I>();
		string device = GetDeviceNameForMonitor(godotScreenIndex);
		if (device == null) return System.Array.Empty<Vector2I>();
		Vector2I native = GetNativeResolution(godotScreenIndex);
		var seen = new System.Collections.Generic.HashSet<Vector2I>();
		var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
		int i = 0;
		while (EnumDisplaySettingsEx(device, i++, ref dm, 0))
		{
			Vector2I r = new Vector2I(dm.dmPelsWidth, dm.dmPelsHeight);
			if (native.X > 0 && (r.X > native.X || r.Y > native.Y)) continue;
			seen.Add(r);
		}
		var list = new System.Collections.Generic.List<Vector2I>(seen);
		list.Sort((a, b) => (a.X * a.Y).CompareTo(b.X * b.Y));
		return list.ToArray();
	}
}
