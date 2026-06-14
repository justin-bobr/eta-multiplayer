using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>Linux equivalent of <see cref="Win32Display"/> — uses the <c>xrandr</c> CLI to programme the X11
/// monitor scanout mode. Wayland disallows app-level mode-change, so <c>IsSupported</c> is false there and the
/// caller falls back to Godot's ExclusiveFullscreen. Unlike Win32's CDS_FULLSCREEN, X11 does not auto-restore
/// on focus loss — the original mode is tracked and restored explicitly on <see cref="Reset"/>, which the
/// caller must invoke on exit / mode change.</summary>
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
	/// <summary>Resolution this backend currently holds an override at; zero when none.</summary>
	public static Vector2I AppliedResolution => _appliedResolution;

	/// <summary>Programmes the monitor scanout mode (best-effort). Returns false if xrandr is missing, the output
	/// doesn't advertise the mode, or the X server rejects it — caller falls back to native fullscreen.</summary>
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

	/// <summary>Restores the original mode active before <see cref="TrySetMode"/>. X11 has no auto-restore on focus-loss.</summary>
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

	/// <summary>Maps a Godot screen index to an xrandr output name by matching each output's geometry against
	/// <see cref="DisplayServer.ScreenGetPosition"/>/<see cref="DisplayServer.ScreenGetSize"/>; falls back to primary.</summary>
	private static string ResolveOutputForMonitor(int monitorIndex, out Vector2I currentMode)
	{
		currentMode = Vector2I.Zero;
		if (!RunXrandrCapture("--query", out string output)) return null;

		Vector2I targetPos = DisplayServer.ScreenGetPosition(monitorIndex);
		Vector2I targetSize = DisplayServer.ScreenGetSize(monitorIndex);

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

	/// <summary>Lists the monitor's supported physical modes (parsed from <c>xrandr --query</c>), sorted by pixel count; modes above native are pruned.</summary>
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

	/// <summary>Returns the monitor's current physical resolution from xrandr; falls back to <see cref="DisplayServer.ScreenGetSize"/>.</summary>
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
