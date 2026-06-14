using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Vantix.Utils;

/// <summary>
/// Thread-safe per-method timing profiler with three layers: _current (per-frame aggregate),
/// _last (last <see cref="FlushFrame"/> snapshot for the HUD), and _window (cumulative, dumped by
/// <see cref="WriteReport"/>). Use <c>using var _ = MiniProfiler.SampleServer/SampleClient("Name")</c>.
/// Zero-cost when <see cref="ProfilingEnabled"/> is false (Sample returns a no-op scope).
/// </summary>
public static class MiniProfiler
{
	/// <summary>Master switch; when false all Sample() calls return a no-op scope. Set from cl_/sv_profiler.</summary>
	public static bool ProfilingEnabled;

	public static bool WarnEnabled = false;
	public static double WarnThresholdMs = 1.0;

	private struct PerFrameEntry { public long TotalTicks; public int Count; }
	private struct WindowEntry
	{
		public long TotalTicks; public long PeakTicks; public int Count; public long TotalBytes; public long PeakBytes;
		/// <summary>TickCount64 when the peak was last raised; peaks survive report resets for <see cref="PeakHoldMs"/>.</summary>
		public long PeakAtMs;
	}

	/// <summary>How long a recorded peak survives report resets (ms). Totals/counts stay per-window.</summary>
	private const long PeakHoldMs = 60_000;

	private static Dictionary<string, PerFrameEntry> _current = new();
	private static Dictionary<string, PerFrameEntry> _last = new();
	private static readonly Dictionary<string, WindowEntry> _window = new();
	private static readonly object _lock = new();

	/// <summary>Begins a timing scope that stops on disposal; no-op scope when ProfilingEnabled is false.</summary>
	public static Scope Sample(string name) =>
		ProfilingEnabled ? new Scope(name, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;

	/// <summary>Sample with the [SV] prefix for server-side code; distinguishes origin in listen mode.</summary>
	public static Scope SampleServer(string name)
	{
		if (!ProfilingEnabled) return default;
		return Sample(GetPrefixedServer(name));
	}

	/// <summary>Sample with the [CL] prefix for client-side code (HUD, player, puppet, FX).</summary>
	public static Scope SampleClient(string name)
	{
		if (!ProfilingEnabled) return default;
		return Sample(GetPrefixedClient(name));
	}

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
			long bytes = GC.GetAllocatedBytesForCurrentThread() - _startBytes;
			lock (_lock)
			{
				_current.TryGetValue(_name, out var pf);
				pf.TotalTicks += delta;
				pf.Count++;
				_current[_name] = pf;
				_window.TryGetValue(_name, out var w);
				w.TotalTicks += delta;
				w.Count++;
				if (delta > w.PeakTicks) { w.PeakTicks = delta; w.PeakAtMs = System.Environment.TickCount64; }
				w.TotalBytes += bytes;
				if (bytes > w.PeakBytes) { w.PeakBytes = bytes; if (w.PeakAtMs == 0) w.PeakAtMs = System.Environment.TickCount64; }
				_window[_name] = w;
			}
		}
	}

	/// <summary>Swaps _current into _last (zero-alloc) and warns on samples over <see cref="WarnThresholdMs"/> when <see cref="WarnEnabled"/>.</summary>
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

	private static readonly List<(string name, double ms, int count)> _topSamplesBuf = new(64);

	/// <summary>Top-N samples by total time from the last snapshot. Returns a reused buffer; consume immediately, do not store.</summary>
	public static List<(string name, double ms, int count)> TopSamples(int n = 10)
	{
		_topSamplesBuf.Clear();
		foreach (var kv in _last)
			_topSamplesBuf.Add((kv.Key, TicksToMs(kv.Value.TotalTicks), kv.Value.Count));
		_topSamplesBuf.Sort(static (a, b) => b.ms.CompareTo(a.ms));
		if (_topSamplesBuf.Count > n) _topSamplesBuf.RemoveRange(n, _topSamplesBuf.Count - n);
		return _topSamplesBuf;
	}

	private static readonly List<(string name, WindowEntry entry)> _reportFiltered = new(128);
	private static readonly List<string> _reportToReset = new(128);
	private static readonly StringBuilder _reportSb = new(8192);

	/// <summary>Writes a per-window report for samples matching the prefix, resetting those window slots after.
	/// Main-thread only (static reuse buffers aren't thread-safe); file I/O runs on a background thread.</summary>
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
					if (kv.Value.Count == 0 && kv.Value.PeakTicks == 0) continue;
					_reportFiltered.Add((kv.Key, kv.Value));
					_reportToReset.Add(kv.Key);
				}
			}
			long now = System.Environment.TickCount64;
			for (int i = 0; i < _reportToReset.Count; i++)
			{
				var w = _window[_reportToReset[i]];
				w.TotalTicks = 0; w.Count = 0; w.TotalBytes = 0;
				if (now - w.PeakAtMs > PeakHoldMs) { w.PeakTicks = 0; w.PeakBytes = 0; w.PeakAtMs = 0; }
				_window[_reportToReset[i]] = w;
			}
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

	/// <summary>Clears all cumulative window data; per-frame _current/_last are untouched.</summary>
	public static void ResetWindow()
	{
		lock (_lock) _window.Clear();
	}

	private static double TicksToMs(long ticks) => (double)ticks / Stopwatch.Frequency * 1000.0;
}
