using Godot;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// HUD-Overlay für <see cref="MiniProfiler"/>. Zeigt ALLE bekannten Samples, sortiert nach
/// 5-Sekunden-Peak descending (= stabile Reihenfolge, größter Übeltäter immer oben). Werte über
/// <see cref="ConVars.Cl"/>.ProfilerThresholdMs sind ROT. Toggle via `cl_profiler 1`.
///
/// 5s-Peak: pro Sample wird PeakMs nur upwards updated; wenn Peak älter als 5s, resettet er auf
/// aktuelles Sample. Dadurch ist die Sort-Order STABIL über mehrere Sekunden statt jeden Frame zu
/// springen — User kann lesen statt zu jagen.
///
/// Auto-Print: bei neuen Warnings (Sample war unter Threshold, ist jetzt drüber) wird ein GD.Print
/// einmalig getriggert. Verhindert Spam.
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

	// Wiederverwendet pro Render-Tick um per-Frame Allocs des HUD-Overlays zu vermeiden.
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
	// HUD-Refresh nur alle ~250ms (4Hz) — Text-Update auf RichTextLabel ist relativ teuer
	// (bbcode parse + reflow), das jeden Frame zu machen hat den Frame um ~5ms inflated.
	// User kann live trotzdem mitlesen weil Werte hochfrequent eh nur peak/age zeigen.
	private const double HudUpdateIntervalSec = 0.25;

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("HudMiniProfiler._Process");
		bool enabled = ConVars.Cl.Profiler;
		MiniProfiler.ProfilingEnabled = enabled || ConVars.Sv.Profiler;
		MiniProfiler.WarnThresholdMs = ConVars.Cl.ProfilerThresholdMs;
		MiniProfiler.WarnEnabled = false; // GD.Print machen wir hier selbst (gated auf "neu warning")
		if (!enabled)
		{
			if (_panel.Visible) { _panel.Visible = false; _samples.Clear(); _writeAccumSec = 0; _writePathPrinted = false; }
			return;
		}

		// Periodischer Client-Report alle 10s nach user://client.profile (gleiche Location wie
		// settings.cfg, also %APPDATA%/Godot/app_userdata/<Project>/ auf Windows).
		_writeAccumSec += delta;
		if (_writeAccumSec >= WriteIntervalSec)
		{
			_writeAccumSec = 0;
			MiniProfiler.WriteReport("user://client.profile", "[CL]", ConVars.Cl.ProfilerThresholdMs);
			if (!_writePathPrinted)
			{
				_writePathPrinted = true;
				string abs = ProjectSettings.GlobalizePath("user://client.profile");
				GD.PushWarning($"[Profiler] cl_profiler ON — client report wird alle 10s nach: {abs}");
			}
		}

		MiniProfiler.FlushFrame();
		long nowMs = (long)Time.GetTicksMsec();
		float threshold = ConVars.Cl.ProfilerThresholdMs;

		// Sample-Processing + Rendering BEIDE im 4Hz-Gate. Vorher: TopSamples(100) (= List-Alloc)
		// + foreach lief jeden Frame = ~2KB GC pro Frame × 60Hz = 120KB/sec garbage. Jetzt nur 4×/sec.
		// Trade-off: Warning-Print (Dbg.Print bei threshold-Edge) feuert auch nur alle 250ms; ok
		// weil HUD-Status-Updates eh max 4Hz reagieren.
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

		// Render sortiert nach Peak descending. Peak ändert sich nur upwards (oder reset nach 5s),
		// daher ist die Sortierung über Sekunden STABIL — kein Hochspringen pro Frame.
		_sortedBuf.Clear();
		foreach (var kv in _samples) _sortedBuf.Add(kv);
		_sortedBuf.Sort((a, b) => b.Value.PeakMs.CompareTo(a.Value.PeakMs));

		// GC-Stats — zeigt Generation 0/1/2 Collection Count + Heap-Size. Spikes von 30ms beim
		// "nur stehen" sind meist GC-Pausen. Wenn Counter zwischen zwei HUD-Updates hochgeht =
		// GC ist gelaufen → das war wahrscheinlich der Spike.
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
