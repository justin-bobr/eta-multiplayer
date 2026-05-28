using Godot;

/// <summary>
/// Compact net_graph in a separate mini-box top-right, beneath the main
/// DebugOverlay bar. 3 rows x 3 columns grid of fixed-width cells (pixel-aligned)
/// plus 2 jitter line-graphs below. Sichtbarkeit via Settings.ShowNetGraph.
/// </summary>
public partial class NetGraphOverlay : Node
{
	[Export] public bool ShowOnStart = true;
	[Export] public int FontSize = 11;
	/// <summary>Pixel offset to the top edge — should sit beneath the DebugOverlay bar.</summary>
	[Export] public int OffsetTopPx = 32;
	[Export] public int OffsetRightPx = 8;
	[Export] public float UpdateInterval = 0.1f;
	/// <summary>Y-axis max of the jitter graphs (in ms). Values above are clamped.</summary>
	[Export] public float JitterGraphYMaxMs = 20f;
	/// <summary>Number of samples retained in the ring buffer of the graphs.</summary>
	[Export] public int JitterGraphSamples = 120;

	private CanvasLayer _layer;
	private PanelContainer _panel;

	private const int Cols = 3;
	private readonly Label[] _cells = new Label[9];

	private Label _downHeader;
	private Label _upHeader;
	private JitterGraph _downGraph;
	private JitterGraph _upGraph;

	private Label _reconcileLabel;

	private float _refreshTimer;

	private double _frameMsSmoothed;
	private double _frameMsVarSmoothed;
	private double _smoothedFps;

	private float _pingAvg;
	private float _downJitterMs;
	private float _upJitterMs;

	/// <summary>Builds the overlay UI (panel, grid, separator and jitter graphs).</summary>
	public override void _Ready()
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server) { QueueFree(); return; }
		_layer = new CanvasLayer { Layer = 100 };
		AddChild(_layer);

		_panel = new PanelContainer
		{
			AnchorLeft = 1f,
			AnchorRight = 1f,
			AnchorTop = 0f,
			AnchorBottom = 0f,
			GrowHorizontal = Control.GrowDirection.Begin,
			GrowVertical = Control.GrowDirection.End,
			OffsetTop = OffsetTopPx,
			OffsetRight = -OffsetRightPx,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};

		var style = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0.65f),
			BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
			BorderColor = new Color(0.3f, 0.5f, 0.3f, 0.4f),
			CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
		};
		style.ContentMarginLeft = 8f;
		style.ContentMarginRight = 8f;
		style.ContentMarginTop = 4f;
		style.ContentMarginBottom = 4f;
		_panel.AddThemeStyleboxOverride("panel", style);
		_layer.AddChild(_panel);

		var vbox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		vbox.AddThemeConstantOverride("separation", 4);
		_panel.AddChild(vbox);

		var grid = new GridContainer { Columns = Cols, MouseFilter = Control.MouseFilterEnum.Ignore };
		grid.AddThemeConstantOverride("h_separation", 12);
		grid.AddThemeConstantOverride("v_separation", 2);
		vbox.AddChild(grid);

		int[] colWidths = { 70, 80, 72 };
		for (int i = 0; i < _cells.Length; i++)
		{
			var lbl = new Label
			{
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				AutowrapMode = TextServer.AutowrapMode.Off,
				CustomMinimumSize = new Vector2(colWidths[i % Cols], 0f),
			};
			lbl.AddThemeColorOverride("font_color", new Color(0.85f, 1f, 0.85f));
			lbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
			lbl.AddThemeConstantOverride("outline_size", 2);
			lbl.AddThemeFontSizeOverride("font_size", FontSize);
			grid.AddChild(lbl);
			_cells[i] = lbl;
		}

		_reconcileLabel = new Label { Text = "reconcile 0.0cm  0/s" };
		_reconcileLabel.AddThemeColorOverride("font_color", new Color(0.6f, 1f, 0.6f));
		_reconcileLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
		_reconcileLabel.AddThemeConstantOverride("outline_size", 2);
		_reconcileLabel.AddThemeFontSizeOverride("font_size", FontSize);
		vbox.AddChild(_reconcileLabel);

		var sep = new HSeparator();
		vbox.AddChild(sep);

		_downHeader = MakeGraphHeader("↓ Jitter (max 20ms)         jitter: 0.0 ms");
		vbox.AddChild(_downHeader);
		_downGraph = new JitterGraph
		{
			CustomMinimumSize = new Vector2(0f, 32f),
			YMaxMs = JitterGraphYMaxMs,
			SampleCount = JitterGraphSamples,
			ThresholdMs = 10f,
			LineColor = new Color(0.6f, 1f, 0.6f),
		};
		_downGraph.InitBuffer();
		vbox.AddChild(_downGraph);

		_upHeader = MakeGraphHeader("↑ Jitter (max 20ms)         jitter: 0.0 ms");
		vbox.AddChild(_upHeader);
		_upGraph = new JitterGraph
		{
			CustomMinimumSize = new Vector2(0f, 32f),
			YMaxMs = JitterGraphYMaxMs,
			SampleCount = JitterGraphSamples,
			ThresholdMs = 10f,
			LineColor = new Color(0.6f, 0.85f, 1f),
		};
		_upGraph.InitBuffer();
		vbox.AddChild(_upGraph);

		_panel.Visible = Settings.ShowNetGraph;
	}

	/// <summary>Creates a styled small label used as a header above each jitter graph.</summary>
	private Label MakeGraphHeader(string text)
	{
		var lbl = new Label { Text = text };
		lbl.AddThemeColorOverride("font_color", new Color(0.85f, 1f, 0.85f));
		lbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
		lbl.AddThemeConstantOverride("outline_size", 2);
		lbl.AddThemeFontSizeOverride("font_size", FontSize - 1);
		return lbl;
	}

	/// <summary>Per-frame: updates smoothed metrics and triggers grid + graph refresh on the configured interval.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("NetGraphOverlay._Process");
		if (_panel.Visible != Settings.ShowNetGraph) _panel.Visible = Settings.ShowNetGraph;
		if (!_panel.Visible) return;

		double frameMs = delta * 1000.0;
		double diff = frameMs - _frameMsSmoothed;
		_frameMsSmoothed = _frameMsSmoothed * 0.95 + frameMs * 0.05;
		_frameMsVarSmoothed = _frameMsVarSmoothed * 0.95 + System.Math.Abs(diff) * 0.05;
		_smoothedFps = _smoothedFps * 0.9 + Engine.GetFramesPerSecond() * 0.1;

		_refreshTimer -= (float)delta;
		if (_refreshTimer > 0f) return;
		_refreshTimer = UpdateInterval;

		_downJitterMs = NetStats.JitterDownMs;
		_upJitterMs = NetStats.JitterUpMs;
		_downGraph?.Push(_downJitterMs);
		_upGraph?.Push(_upJitterMs);

		Refresh();
	}

	/// <summary>Updates the cell labels, reconcile indicator and graph headers from current NetStats.</summary>
	private void Refresh()
	{
		int ping = NetStats.PingMs;
		float upPktPerSec = NetStats.Mode == NetMode.Server ? 0f : (float)Engine.PhysicsTicksPerSecond;
		float cmdPktPerSec = (float)Engine.PhysicsTicksPerSecond;
		float lossUp = NetStats.PacketLossUpPct;
		float tickRate = Engine.PhysicsTicksPerSecond;
		string serverType = NetStats.Mode switch
		{
			NetMode.Server => "Dedicated",
			NetMode.Listen => "Listen",
			NetMode.Client => NetStats.ClientConnected ? "Online" : "Offline",
			_ => "?",
		};

		// NetStats.BytesPerSecUp/Down sind raw bytes/s — auf KB/s umrechnen + auch in/out bps für
		// die letzte Zeile. CS2-NetGraph-Style: down/up bandwidth direkt in der Mitte sichtbar.
		float downKBs = NetStats.BytesPerSecDown / 1024f;
		float upKBs   = NetStats.BytesPerSecUp / 1024f;

		Set(0, 0, $"ping {ping}ms");
		Set(0, 1, $"loss {lossUp:F1}%");
		Set(0, 2, $"up {upPktPerSec:F0}/s");

		Set(1, 0, $"tick {tickRate:F0}");
		Set(1, 1, "choke 0%");
		Set(1, 2, $"cmd {cmdPktPerSec:F0}/s");

		Set(2, 0, $"in {downKBs:F1}KB/s");
		Set(2, 1, $"out {upKBs:F1}KB/s");
		Set(2, 2, serverType);

		float driftCm = NetStats.LastReconcileDriftM * 100f;
		int rps = NetStats.ReconcilesPerSec;
		double sinceLast = (Time.GetTicksMsec() / 1000.0) - NetStats.LastReconcileTimeSec;
		Color reconcileCol;
		if (driftCm < 5f && rps < 3)             reconcileCol = new Color(0.6f, 1f, 0.6f);
		else if (driftCm < 30f && rps < 10)      reconcileCol = new Color(1f, 0.95f, 0.55f);
		else                                     reconcileCol = new Color(1f, 0.4f, 0.4f);
		if (sinceLast < 0.3) reconcileCol = reconcileCol.Lerp(new Color(1f, 1f, 1f), 0.5f);
		_reconcileLabel.Text = $"reconcile {driftCm:F1}cm  {rps}/s";
		_reconcileLabel.AddThemeColorOverride("font_color", reconcileCol);

		_downHeader.Text = $"↓Jit {_downJitterMs:F1}ms";
		_upHeader.Text = $"↑Jit {_upJitterMs:F1}ms";
	}

	/// <summary>Writes text into the cell at (row, col) of the 3x3 grid.</summary>
	private void Set(int row, int col, string text)
	{
		int idx = row * Cols + col;
		if (idx >= 0 && idx < _cells.Length) _cells[idx].Text = text;
	}
}

/// <summary>Compact line-graph control: draws a ring buffer of ms values as a continuous line
/// plus a threshold line. Instantiated by <see cref="NetGraphOverlay"/> (no .tscn needed).</summary>
public partial class JitterGraph : Control
{
	public int SampleCount = 120;
	public float YMaxMs = 20f;
	public float ThresholdMs = 10f;
	public Color LineColor = new(0.85f, 1f, 0.85f);
	public Color ThresholdColor = new(1f, 0.2f, 0.2f, 0.5f);
	public Color BgColor = new(0f, 0f, 0f, 0.3f);

	private float[] _samples;
	private int _writeIdx;

	/// <summary>Allocates the sample ring buffer with <see cref="SampleCount"/> entries.</summary>
	public void InitBuffer()
	{
		_samples = new float[SampleCount];
	}

	/// <summary>Appends a new sample (in ms) to the ring buffer and queues a redraw.</summary>
	public void Push(float sampleMs)
	{
		if (_samples == null) return;
		_samples[_writeIdx] = sampleMs;
		_writeIdx = (_writeIdx + 1) % _samples.Length;
		QueueRedraw();
	}

	/// <summary>Draws background, threshold line and the connected sample polyline.</summary>
	public override void _Draw()
	{
		if (_samples == null) return;
		Vector2 size = Size;
		if (size.X <= 0 || size.Y <= 0) return;

		DrawRect(new Rect2(Vector2.Zero, size), BgColor);

		float threshY = size.Y * (1f - Mathf.Clamp(ThresholdMs / YMaxMs, 0f, 1f));
		DrawLine(new Vector2(0f, threshY), new Vector2(size.X, threshY), ThresholdColor, 1f);

		float xStep = size.X / (float)(_samples.Length - 1);
		Vector2 prev = Vector2.Zero;
		bool hasPrev = false;
		for (int i = 0; i < _samples.Length; i++)
		{
			int idx = (_writeIdx + i) % _samples.Length;
			float v = Mathf.Clamp(_samples[idx], 0f, YMaxMs);
			float y = size.Y * (1f - v / YMaxMs);
			Vector2 p = new(i * xStep, y);
			if (hasPrev) DrawLine(prev, p, LineColor, 1f);
			prev = p;
			hasPrev = true;
		}
	}
}
