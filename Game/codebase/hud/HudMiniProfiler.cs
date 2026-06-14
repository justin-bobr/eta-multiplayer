using Godot;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// HUD overlay for <see cref="MiniProfiler"/>. Lists samples sorted by 5-second peak descending; values
/// over <see cref="ConVars.Cl"/>.ProfilerThresholdMs shown red. Toggle via <c>cl_profiler 1</c>.
/// The peak rises only and resets to current after 5 s, keeping a stable sort. Prints once per sample
/// when it first crosses the threshold.
/// </summary>
public partial class HudMiniProfiler : CanvasLayer
{
	[Export] public int LayerOrder = 95;
	private const long PeakWindowMs = 5000;
	private const long IdleTimeoutMs = 10000;

	private RichTextLabel _label;
	private PanelContainer _panel;

	private struct SampleState
	{
		public double PeakMs;
		public long PeakTimeMs;
		public double LastMs;
		public int LastCount;
		public long LastUpdateMs;
		public bool WasAboveThreshold;
	}
	private readonly Dictionary<string, SampleState> _samples = new();

	private readonly List<KeyValuePair<string, SampleState>> _sortedBuf = new(64);
	private readonly StringBuilder _sb = new(2048);

	public override void _Ready()
	{
		Layer = LayerOrder;
		_panel = new PanelContainer
		{
			AnchorLeft = 0f, AnchorTop = 0f,
			OffsetLeft = 10f, OffsetTop = 200f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		var sb = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.78f) };
		sb.ContentMarginLeft = 8; sb.ContentMarginRight = 8;
		sb.ContentMarginTop = 4; sb.ContentMarginBottom = 4;
		_panel.AddThemeStyleboxOverride("panel", sb);
		AddChild(_panel);

		_label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			CustomMinimumSize = new Vector2(420, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ScrollActive = false,
		};
		_label.AddThemeFontSizeOverride("normal_font_size", 11);
		_label.AddThemeColorOverride("default_color", new Color(0.85f, 0.95f, 0.85f));
		_label.AddThemeConstantOverride("line_separation", 0);
		_panel.AddChild(_label);
		_panel.Visible = false;
	}

	private double _writeAccumSec;
	private double _hudUpdateAccumSec;
	private bool _writePathPrinted;
	private const double WriteIntervalSec = 10.0;
	private const double HudUpdateIntervalSec = 0.25;

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("HudMiniProfiler._Process");
		bool enabled = ConVars.Cl.Profiler;
		MiniProfiler.ProfilingEnabled = enabled || ConVars.Sv.Profiler;
		MiniProfiler.WarnThresholdMs = ConVars.Cl.ProfilerThresholdMs;
		MiniProfiler.WarnEnabled = false;
		if (!enabled)
		{
			if (_panel.Visible) { _panel.Visible = false; _samples.Clear(); _writeAccumSec = 0; _writePathPrinted = false; }
			return;
		}

		_writeAccumSec += delta;
		if (_writeAccumSec >= WriteIntervalSec)
		{
			_writeAccumSec = 0;
			MiniProfiler.WriteReport("user://client.profile", "[CL]", ConVars.Cl.ProfilerThresholdMs);
			if (!_writePathPrinted)
			{
				_writePathPrinted = true;
				string abs = ProjectSettings.GlobalizePath("user://client.profile");
				GD.PushWarning($"[Profiler] cl_profiler ON — writing client report every 10 s to: {abs}");
			}
		}

		MiniProfiler.FlushFrame();
		long nowMs = (long)Time.GetTicksMsec();
		float threshold = ConVars.Cl.ProfilerThresholdMs;

		_hudUpdateAccumSec += delta;
		if (_hudUpdateAccumSec < HudUpdateIntervalSec) return;
		_hudUpdateAccumSec = 0;

		foreach (var (name, ms, count) in MiniProfiler.TopSamples(100))
		{
			_samples.TryGetValue(name, out var st);
			st.LastMs = ms;
			st.LastCount = count;
			st.LastUpdateMs = nowMs;
			if (ms > st.PeakMs || nowMs - st.PeakTimeMs > PeakWindowMs)
			{
				st.PeakMs = ms;
				st.PeakTimeMs = nowMs;
			}
			bool nowAbove = ms > threshold;
			if (nowAbove && !st.WasAboveThreshold)
				Dbg.Print($"[Profiler] WARN  {name}  {ms:F2}ms ×{count}  (threshold {threshold:F2}ms)");
			st.WasAboveThreshold = nowAbove;
			_samples[name] = st;
		}

		List<string> stale = null;
		foreach (var kv in _samples)
			if (nowMs - kv.Value.LastUpdateMs > IdleTimeoutMs)
				(stale ??= new List<string>()).Add(kv.Key);
		if (stale != null)
			foreach (var k in stale) _samples.Remove(k);

		if (_samples.Count == 0) { _panel.Visible = false; return; }

		_sortedBuf.Clear();
		foreach (var kv in _samples) _sortedBuf.Add(kv);
		_sortedBuf.Sort((a, b) => b.Value.PeakMs.CompareTo(a.Value.PeakMs));

		int gen0 = System.GC.CollectionCount(0);
		int gen1 = System.GC.CollectionCount(1);
		int gen2 = System.GC.CollectionCount(2);
		long heapKb = System.GC.GetTotalMemory(forceFullCollection: false) / 1024;

		_sb.Clear();
		_sb.Append($"[b][Profiler][/b]  threshold {threshold:F2}ms  peak window {PeakWindowMs / 1000}s\n");
		_sb.Append($"[color=#aaaaaa]GC gen0={gen0} gen1={gen1} gen2={gen2}  heap={heapKb}KB[/color]\n");
		foreach (var kv in _sortedBuf)
		{
			var s = kv.Value;
			bool red = s.LastMs > threshold;
			string color = red ? "ff5544" : "aacca8";
			long peakAge = nowMs - s.PeakTimeMs;
			_sb.Append($"[color=#{color}]{kv.Key,-46}  now {s.LastMs,6:F2}ms  peak {s.PeakMs,6:F2}ms ({peakAge / 1000}s)[/color]\n");
		}
		_label.Text = _sb.ToString();
		_panel.Visible = true;
	}
}
