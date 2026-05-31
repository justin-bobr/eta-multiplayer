using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Linux equivalent of <see cref="Win32Display"/> — uses the <c>xrandr</c> CLI to programme the
/// monitor scanout mode on X11. Wayland does NOT allow application-level mode-change (by design;
/// the compositor owns display modes), so this backend returns <c>IsSupported=false</c> there and
/// the caller falls back to Godot's native ExclusiveFullscreen.
///
/// On X11 we shell out to <c>xrandr</c> instead of P/Invoking <c>libXrandr</c> because:
///   • <c>xrandr</c> ships pre-installed on every X11 distro (Debian/Ubuntu/Arch/Fedora/Mint).
///   • Avoids brittle native bindings + .so version matching headaches across distros.
///   • Single-shot mode-change at settings-apply time — CLI startup cost (~30 ms) is fine.
///
/// Restoration: unlike Win32's <c>CDS_FULLSCREEN</c>, X11 does NOT auto-restore on focus loss.
/// We track the original mode and explicitly run <c>xrandr --output X --mode WxH</c> back on
/// <see cref="Reset"/>. Caller is responsible for invoking Reset on game exit / mode change.
/// </summary>
public static class LinuxDisplay
{
	public static bool IsSupported
	{
		get
		{
			if (OS.GetName() != "Linux") return false;
			string sessionType = System.Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
			if (sessionType == "wayland") return false;
			return FindXrandr() != null;
		}
	}

	private static string FindXrandr()
	{
		foreach (string candidate in new[] { "/usr/bin/xrandr", "/usr/local/bin/xrandr", "/bin/xrandr" })
			if (File.Exists(candidate)) return candidate;
		return null;
	}

	private static string _appliedOutput;
	private static Vector2I _originalMode;
	private static Vector2I _appliedResolution;
	public static bool HasAppliedMode => _appliedOutput != null;
	/// <summary>Resolution this backend currently holds an override at. Zero when no override held.
	/// Mirrors <see cref="Win32Display.AppliedResolution"/> so <c>Settings</c> can do a single
	/// already-correct check across both backends.</summary>
	public static Vector2I AppliedResolution => _appliedResolution;

	/// <summary>Programme the monitor's scanout mode. Best-effort: succeeds if xrandr is on PATH,
	/// the output advertises the mode (parsed from <c>xrandr --query</c>), and the X server accepts
	/// the change. Returns false otherwise — caller falls back to native fullscreen.</summary>
	public static bool TrySetMode(int monitorIndex, int width, int height, int refreshHz = 0)
	{
		if (!IsSupported) return false;
		string output = ResolveOutputForMonitor(monitorIndex, out Vector2I currentMode);
		if (output == null)
		{
			GD.PrintErr($"[LinuxDisplay] could not resolve xrandr output for monitor index {monitorIndex}");
			return false;
		}

		string args = $"--output {output} --mode {width}x{height}";
		if (refreshHz > 0) args += $" --rate {refreshHz}";
		if (RunXrandr(args, out string stderr))
		{
			_appliedOutput = output;
			_originalMode = currentMode;
			_appliedResolution = new Vector2I(width, height);
			GD.Print($"[LinuxDisplay] mode-change OK: {output} → {width}×{height}@{(refreshHz > 0 ? refreshHz + "Hz" : "auto")}");
			return true;
		}
		GD.PrintErr($"[LinuxDisplay] xrandr failed: {stderr}");
		return false;
	}

	/// <summary>Restore the original mode that was active before <see cref="TrySetMode"/> was called.
	/// X11 has no auto-restore on focus-loss; if the user Alt-Tabs out of an ExclusiveFullscreen
	/// game without us catching the focus event, the monitor stays at the sub-native mode.</summary>
	public static void Reset()
	{
		if (!IsSupported || _appliedOutput == null) return;
		string args = _originalMode != Vector2I.Zero
			? $"--output {_appliedOutput} --mode {_originalMode.X}x{_originalMode.Y}"
			: $"--output {_appliedOutput} --auto";
		RunXrandr(args, out _);
		GD.Print($"[LinuxDisplay] mode-restored: {_appliedOutput}");
		_appliedOutput = null;
		_originalMode = Vector2I.Zero;
		_appliedResolution = Vector2I.Zero;
	}

	/// <summary>Maps a Godot screen index to an xrandr output name (e.g. "DP-1", "HDMI-2",
	/// "eDP-1"). Strategy: parse <c>xrandr --query</c>, match each output's geometry (offset+size)
	/// against Godot's <see cref="DisplayServer.ScreenGetPosition"/> + <see cref="DisplayServer.ScreenGetSize"/>.
	/// Falls back to the primary output on mismatch.</summary>
	private static string ResolveOutputForMonitor(int monitorIndex, out Vector2I currentMode)
	{
		currentMode = Vector2I.Zero;
		if (!RunXrandrCapture("--query", out string output)) return null;

		Vector2I targetPos = DisplayServer.ScreenGetPosition(monitorIndex);
		Vector2I targetSize = DisplayServer.ScreenGetSize(monitorIndex);

		// xrandr line for an active output looks like:
		//   DP-1 connected primary 2560x1440+0+0 (normal left ...) 597mm x 336mm
		// or for a secondary:
		//   HDMI-2 connected 1920x1080+2560+360 (normal ...) 510mm x 290mm
		Regex line = new Regex(@"^(\S+) connected (?:primary )?(\d+)x(\d+)\+(-?\d+)\+(-?\d+)", RegexOptions.Multiline);
		string primary = null;
		foreach (Match m in line.Matches(output))
		{
			string name = m.Groups[1].Value;
			int w = int.Parse(m.Groups[2].Value);
			int h = int.Parse(m.Groups[3].Value);
			int x = int.Parse(m.Groups[4].Value);
			int y = int.Parse(m.Groups[5].Value);
			if (m.Value.Contains(" primary ")) primary = name;
			if (x == targetPos.X && y == targetPos.Y && w == targetSize.X && h == targetSize.Y)
			{
				currentMode = new Vector2I(w, h);
				return name;
			}
		}
		return primary;
	}

	private static bool RunXrandr(string args, out string stderr)
	{
		stderr = string.Empty;
		string xrandr = FindXrandr();
		if (xrandr == null) return false;
		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = xrandr,
				Arguments = args,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			using var p = Process.Start(psi);
			stderr = p.StandardError.ReadToEnd();
			p.WaitForExit(2000);
			return p.ExitCode == 0;
		}
		catch (Exception ex)
		{
			stderr = ex.Message;
			return false;
		}
	}

	/// <summary>Parses <c>xrandr --query</c> to produce the list of physical modes the monitor
	/// supports — same data CS2 reads on Linux. Returns sorted by pixel count, modes above the
	/// monitor's current native pruned.</summary>
	public static Vector2I[] EnumModes(int godotScreenIndex)
	{
		if (!IsSupported) return System.Array.Empty<Vector2I>();
		if (!RunXrandrCapture("--query", out string output)) return System.Array.Empty<Vector2I>();

		Vector2I targetPos = DisplayServer.ScreenGetPosition(godotScreenIndex);
		Vector2I targetSize = DisplayServer.ScreenGetSize(godotScreenIndex);
		Vector2I native = Vector2I.Zero;
		var modes = new System.Collections.Generic.HashSet<Vector2I>();

		bool inTargetOutput = false;
		foreach (string line in output.Split('\n'))
		{
			// Output header: "DP-1 connected primary 2560x1440+0+0 (...) 597mm x 336mm"
			Match h = Regex.Match(line, @"^(\S+) connected (?:primary )?(\d+)x(\d+)\+(-?\d+)\+(-?\d+)");
			if (h.Success)
			{
				int w = int.Parse(h.Groups[2].Value);
				int hgt = int.Parse(h.Groups[3].Value);
				int x = int.Parse(h.Groups[4].Value);
				int y = int.Parse(h.Groups[5].Value);
				inTargetOutput = x == targetPos.X && y == targetPos.Y && w == targetSize.X && hgt == targetSize.Y;
				if (inTargetOutput) native = new Vector2I(w, hgt);
				continue;
			}
			if (!inTargetOutput) continue;
			// Mode line: "   1920x1080     60.00*+ 144.00   59.94"
			Match m = Regex.Match(line, @"^\s+(\d+)x(\d+)\s+");
			if (m.Success)
			{
				Vector2I r = new Vector2I(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
				if (native.X > 0 && (r.X > native.X || r.Y > native.Y)) continue;
				modes.Add(r);
			}
		}
		var list = new System.Collections.Generic.List<Vector2I>(modes);
		list.Sort((a, b) => (a.X * a.Y).CompareTo(b.X * b.Y));
		return list.ToArray();
	}

	/// <summary>Returns the monitor's current physical resolution by re-parsing the xrandr query
	/// output. Falls back to Godot's <see cref="DisplayServer.ScreenGetSize"/> on parse failure.</summary>
	public static Vector2I GetNativeResolution(int godotScreenIndex)
	{
		if (!IsSupported) return DisplayServer.ScreenGetSize(godotScreenIndex);
		if (!RunXrandrCapture("--query", out string output)) return DisplayServer.ScreenGetSize(godotScreenIndex);
		Vector2I targetPos = DisplayServer.ScreenGetPosition(godotScreenIndex);
		Vector2I targetSize = DisplayServer.ScreenGetSize(godotScreenIndex);
		foreach (Match h in Regex.Matches(output, @"^(\S+) connected (?:primary )?(\d+)x(\d+)\+(-?\d+)\+(-?\d+)", RegexOptions.Multiline))
		{
			int w = int.Parse(h.Groups[2].Value);
			int hgt = int.Parse(h.Groups[3].Value);
			int x = int.Parse(h.Groups[4].Value);
			int y = int.Parse(h.Groups[5].Value);
			if (x == targetPos.X && y == targetPos.Y && w == targetSize.X && hgt == targetSize.Y)
				return new Vector2I(w, hgt);
		}
		return DisplayServer.ScreenGetSize(godotScreenIndex);
	}

	private static bool RunXrandrCapture(string args, out string stdout)
	{
		stdout = string.Empty;
		string xrandr = FindXrandr();
		if (xrandr == null) return false;
		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = xrandr,
				Arguments = args,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			using var p = Process.Start(psi);
			stdout = p.StandardOutput.ReadToEnd();
			p.WaitForExit(2000);
			return p.ExitCode == 0;
		}
		catch { return false; }
	}
}
