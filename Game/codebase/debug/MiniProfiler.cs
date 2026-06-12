using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

/// <summary>
/// Thread-safe Mini-Profiler für Per-Method Timing. Drei Layer:
/// <list type="bullet">
/// <item><b>_current</b> = per-frame Aggregate, von HUD via <see cref="FlushFrame"/> alle paar
///       Frames konsumiert (= live Top-N Anzeige).</item>
/// <item><b>_last</b> = letzter Flush-Snapshot, für HUD-Rendering.</item>
/// <item><b>_window</b> = CUMULATIVE seit Game-Start (Total/Peak/Count pro Sample). Wird von
///       <see cref="WriteReport"/> periodisch nach Datei gedumpt (server.profile / client.profile).</item>
/// </list>
///
/// Usage:
/// <code>
///   using var _ = MiniProfiler.SampleServer("NetServer.Poll");   // → [SV] NetServer.Poll
///   using var _ = MiniProfiler.SampleClient("Crosshair._Process"); // → [CL] Crosshair._Process
/// </code>
///
/// Zero-cost wenn <see cref="ProfilingEnabled"/> false: Sample() returnt default scope, Dispose no-op.
/// </summary>
public static class MiniProfiler
{
	/// <summary>Master-Switch — wenn false, alle Sample() Calls returnen no-op scope (zero cost).
	/// Wird vom HUD (cl_profiler) + Server-Dumper (sv_profiler) je nach ConVar-State gesetzt.</summary>
	public static bool ProfilingEnabled;

	public static bool WarnEnabled = false;
	public static double WarnThresholdMs = 1.0;

	private struct PerFrameEntry { public long TotalTicks; public int Count; }
	private struct WindowEntry { public long TotalTicks; public long PeakTicks; public int Count; public long TotalBytes; public long PeakBytes; }

	// Zwei vor-allokierte Dicts die per FlushFrame SWAPPED werden (Pointer-Swap = zero alloc) statt
	// ein neues Dict zu allocaten. Vorher: `new Dictionary(_current)` jeden Frame = 60+ Dict-Allocs/sec
	// = HAUPTURSACHE für GC-Pressure → 50-150ms Spikes alle ~10s.
	private static Dictionary<string, PerFrameEntry> _current = new();
	private static Dictionary<string, PerFrameEntry> _last = new();
	private static readonly Dictionary<string, WindowEntry> _window = new();
	private static readonly object _lock = new();

	/// <summary>Begin-Scope. Mit `using var _ = MiniProfiler.Sample("Name")` automatisch gestoppt.
	/// Bei ProfilingEnabled=false: no-op (returnt default scope).</summary>
	public static Scope Sample(string name) =>
		ProfilingEnabled ? new Scope(name, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;

	/// <summary>Sample mit Server-Prefix [SV]. Für server-side Code (NetServer, ServerAgent's hot paths).
	/// In Listen-Mode wo Server + Client im selben Prozess laufen → sofort klar von welcher Seite.
	///
	/// KRITISCH: ProfilingEnabled-Check VOR dem String-Concat. Sonst allokiert `"[SV] " + name` jeden
	/// Call (= 3000+ String-Allocs/sec wenn 50+ Sample-Sites × 60Hz). Mit Check: zero alloc when off.</summary>
	public static Scope SampleServer(string name)
	{
		if (!ProfilingEnabled) return default;
		return Sample(GetPrefixedServer(name));
	}

	/// <summary>Sample mit Client-Prefix [CL]. Für client-side Code (HUD, LocalPlayer, Puppet, FX).</summary>
	public static Scope SampleClient(string name)
	{
		if (!ProfilingEnabled) return default;
		return Sample(GetPrefixedClient(name));
	}

	// String-Cache für Prefixed-Names. Erste Call pro unique name allokiert "[SV] name" / "[CL] name",
	// danach aus Cache → zero alloc. Hält die prefixed strings auch alive als Dict-Keys.
	private static readonly Dictionary<string, string> _serverPrefixCache = new();
	private static readonly Dictionary<string, string> _clientPrefixCache = new();
	private static string GetPrefixedServer(string name)
	{
		if (_serverPrefixCache.TryGetValue(name, out var cached)) return cached;
		cached = "[SV] " + name;
		_serverPrefixCache[name] = cached;
		return cached;
	}
	private static string GetPrefixedClient(string name)
	{
		if (_clientPrefixCache.TryGetValue(name, out var cached)) return cached;
		cached = "[CL] " + name;
		_clientPrefixCache[name] = cached;
		return cached;
	}

	public ref struct Scope
	{
		private readonly string _name;
		private readonly long _startTicks;
		private readonly long _startBytes;

		internal Scope(string name, long startTicks, long startBytes)
		{
			_name = name;
			_startTicks = startTicks;
			_startBytes = startBytes;
		}

		public void Dispose()
		{
			if (_name == null) return;
			long delta = Stopwatch.GetTimestamp() - _startTicks;
			// Managed bytes allocated during this scope (incl. nested calls). Reveals the GC-pressure driver
			// that pure timing misses — a GC pause gets blamed on whatever ran during it, not the allocator.
			long bytes = GC.GetAllocatedBytesForCurrentThread() - _startBytes;
			lock (_lock)
			{
				// Per-Frame Aggregat (für HUD live-Anzeige)
				_current.TryGetValue(_name, out var pf);
				pf.TotalTicks += delta;
				pf.Count++;
				_current[_name] = pf;
				// Cumulative Window (für periodische File-Reports)
				_window.TryGetValue(_name, out var w);
				w.TotalTicks += delta;
				w.Count++;
				if (delta > w.PeakTicks) w.PeakTicks = delta;
				w.TotalBytes += bytes;
				if (bytes > w.PeakBytes) w.PeakBytes = bytes;
				_window[_name] = w;
			}
		}
	}

	/// <summary>Vom HUD pro Frame gerufen. SWAP current ↔ last (Pointer-Swap = zero alloc) statt
	/// Dict-Copy. Auto-Warning wenn ein Sample über <see cref="WarnThresholdMs"/> liegt UND
	/// <see cref="WarnEnabled"/> = true.</summary>
	public static void FlushFrame()
	{
		lock (_lock)
		{
			var tmp = _last;
			_last = _current;
			_current = tmp;
			_current.Clear();
		}

		if (!WarnEnabled || !ProfilingEnabled) return;
		foreach (var kv in _last)
		{
			double ms = TicksToMs(kv.Value.TotalTicks);
			if (ms > WarnThresholdMs)
				Godot.GD.PushWarning($"[Profiler] {kv.Key} took {ms:F2}ms ({kv.Value.Count}x calls) in last frame");
		}
	}

	/// <summary>Top-N samples by total time from LAST FlushFrame snapshot. Für HUD-Live-Anzeige.</summary>
	public static List<(string name, double ms, int count)> TopSamples(int n = 10)
	{
		var list = new List<(string, double, int)>(_last.Count);
		foreach (var kv in _last)
			list.Add((kv.Key, TicksToMs(kv.Value.TotalTicks), kv.Value.Count));
		list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
		if (list.Count > n) list.RemoveRange(n, list.Count - n);
		return list;
	}

	// Reused-Buffers für WriteReport — ohne diese allokierte jeder Write ~10KB+ garbage (List<>,
	// List<string>, StringBuilder, Strings für jeden Sample-Row, Task-Closure-Capture). Das triggert
	// alle 10s eine Gen1/2 GC-Collection = 50-150ms Stall = USERS 140ms-Spikes.
	private static readonly List<(string name, WindowEntry entry)> _reportFiltered = new(128);
	private static readonly List<string> _reportToReset = new(128);
	private static readonly StringBuilder _reportSb = new(8192);

	/// <summary>Schreibt einen Per-WINDOW-Report (= seit letztem Write, typisch 10s) aller Samples
	/// mit dem gegebenen Prefix. Resettet die gefilterten Window-Slots nach Snapshot damit der nächste
	/// Report frische Daten zeigt (= aktuelle Hotspots, nicht stale Peaks von vor Stunden).
	///
	/// I/O läuft auf BACKGROUND-Thread — Snapshot + String-Format synchron im Main-Thread (fast +
	/// lock-protected), File-I/O off-thread.
	///
	/// REUSED BUFFERS: zero allocation pro Aufruf (außer dem finalen String und Task.Run closure).
	/// Vor dem Fix war jeder 10s-Tact ein GC-Spike. Methoden-Body MUSS single-threaded sein —
	/// die statischen Buffers sind nicht thread-safe und WriteReport wird per Konvention nur vom
	/// Main-Thread gerufen (Client von HudMiniProfiler._Process, Server von NetServer.Poll).</summary>
	public static void WriteReport(string path, string filterPrefix, double warnThresholdMs)
	{
		_reportFiltered.Clear();
		_reportToReset.Clear();
		lock (_lock)
		{
			foreach (var kv in _window)
			{
				if (string.IsNullOrEmpty(filterPrefix) || kv.Key.StartsWith(filterPrefix))
				{
					_reportFiltered.Add((kv.Key, kv.Value));
					_reportToReset.Add(kv.Key);
				}
			}
			for (int i = 0; i < _reportToReset.Count; i++) _window.Remove(_reportToReset[i]);
		}
		_reportFiltered.Sort((a, b) => b.Item2.PeakTicks.CompareTo(a.Item2.PeakTicks));

		_reportSb.Clear();
		_reportSb.Append("# MiniProfiler Report — ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
		_reportSb.Append("# Filter: ").Append(filterPrefix ?? "(all)").Append("    WarnThreshold: ").Append(warnThresholdMs.ToString("F2")).Append("ms\n");
		_reportSb.Append("# Sorted by peak descending. \"!!\" = peak > threshold.\n\n");
		_reportSb.Append("Name                                                 Total          Peak       Avg     Count   AllocTot   AllocPk\n");
		for (int i = 0; i < _reportFiltered.Count; i++)
		{
			var (name, e) = _reportFiltered[i];
			double totalMs = TicksToMs(e.TotalTicks);
			double peakMs = TicksToMs(e.PeakTicks);
			double avgMs = e.Count > 0 ? totalMs / e.Count : 0;
			// Manual padding statt String-Interpolation (= jeder $"..." Call = neue String-Alloc).
			AppendPaddedRight(_reportSb, name, 52);
			_reportSb.Append(' ');
			AppendPaddedLeft(_reportSb, totalMs.ToString("F2"), 9); _reportSb.Append("ms ");
			AppendPaddedLeft(_reportSb, peakMs.ToString("F2"), 7); _reportSb.Append("ms ");
			AppendPaddedLeft(_reportSb, avgMs.ToString("F3"), 7); _reportSb.Append("ms ");
			AppendPaddedLeft(_reportSb, e.Count.ToString(), 8);
			_reportSb.Append("  ");
			AppendPaddedLeft(_reportSb, (e.TotalBytes / 1024).ToString(), 8); _reportSb.Append("KB ");
			AppendPaddedLeft(_reportSb, (e.PeakBytes / 1024).ToString(), 6); _reportSb.Append("KB");
			if (peakMs > warnThresholdMs) _reportSb.Append(" !!");
			_reportSb.Append('\n');
		}
		string text = _reportSb.ToString();

		// File-I/O off-thread. Resolve the path via Godot's GlobalizePath here on the main
		// thread (Godot APIs are not guaranteed thread-safe) and pass the absolute path
		// into the worker — that uses native .NET System.IO.File which is actually thread-
		// safe and doesn't take any engine-internal locks.
		string absPath = ProjectSettings.GlobalizePath(path);
		System.Threading.Tasks.Task.Run(() =>
		{
			try { System.IO.File.WriteAllText(absPath, text); }
			catch { }
		});
	}

	private static void AppendPaddedRight(StringBuilder sb, string s, int width)
	{
		sb.Append(s);
		for (int p = s.Length; p < width; p++) sb.Append(' ');
	}
	private static void AppendPaddedLeft(StringBuilder sb, string s, int width)
	{
		for (int p = s.Length; p < width; p++) sb.Append(' ');
		sb.Append(s);
	}

	/// <summary>Vergisst alle Cumulative-Window-Daten. Sinnvoll nach einer Test-Session damit der
	/// nächste Report frisch ist. Per-Frame _current / _last bleiben unberührt.</summary>
	public static void ResetWindow()
	{
		lock (_lock) _window.Clear();
	}

	private static double TicksToMs(long ticks) => (double)ticks / Stopwatch.Frequency * 1000.0;
}
