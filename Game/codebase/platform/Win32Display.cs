using Godot;
using System;
using System.Runtime.InteropServices;

/// <summary>Direct Win32 monitor mode-change for sub-native exclusive-fullscreen. Calls
/// <c>user32!ChangeDisplaySettingsEx</c> with <c>CDS_FULLSCREEN</c>: reprograms the monitor scanout to the
/// requested mode (the panel's hardware scaler upscales), and Windows auto-restores the desktop mode on
/// focus-loss/exit/crash. On non-Windows <see cref="IsSupported"/> is false and the caller falls back to
/// Godot's ExclusiveFullscreen at native res.</summary>
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

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern int ChangeDisplaySettingsEx(
		string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

	[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "ChangeDisplaySettingsExW")]
	private static extern int ChangeDisplaySettingsExReset(
		string lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

	[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "EnumDisplaySettingsExW")]
	private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, int dwFlags);

	private const int ENUM_CURRENT_SETTINGS = -1;

	private static string _appliedDevice;
	private static Vector2I _appliedResolution;
	/// <summary>True while a mode-override is active on a monitor (signals whether Reset() is needed on leaving ExclusiveFullscreen).</summary>
	public static bool HasAppliedMode => _appliedDevice != null;
	public static Vector2I AppliedResolution => _appliedResolution;

	/// <summary>Changes the monitor mode for the screen owning <paramref name="hwnd"/>. Two-phase: CDS_TEST first
	/// (no visible blink), then CDS_FULLSCREEN apply. Returns false on any failure — caller falls back to native fullscreen.</summary>
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

	/// <summary>Restores the original desktop mode on the last-touched monitor (for the in-session switch-away case; CDS_FULLSCREEN handles focus-loss/exit automatically).</summary>
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
	/// <paramref name="godotScreenIndex"/> via <c>EnumDisplayDevices</c> (OS enumeration order). Robust under
	/// multi-DPI, where a coordinate-based lookup would land on the wrong monitor.</summary>
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

	/// <summary>Extracts the Windows display number from a device name (<c>\\.\DISPLAY3</c> → <c>3</c>) so dropdown labels match Windows Settings → Display.</summary>
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

	/// <summary>Reads the monitor's current physical resolution in raw pixels, bypassing DPI-scaling. Needed because <c>ChangeDisplaySettingsEx</c> takes physical pixels.</summary>
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

	/// <summary>Enumerates the monitor's advertised resolutions via <c>EnumDisplaySettings</c> (unique width/height
	/// pairs), sorted by pixel count; modes above native are pruned.</summary>
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
