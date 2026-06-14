using Godot;

namespace Vantix.Debug;

/// <summary>
/// Horizontal debug bar at the top of the screen. Visibility controlled via Settings.ShowDebugBar
/// (default F3). Required: assign <see cref="Player"/> in the Inspector.
/// </summary>
public partial class DebugOverlay : Node
{
	[Export] public NetworkPlayer Player;
	[Export] public int FontSize = 12;
	[Export] public float UpdateInterval = 0.1f;

	private CanvasLayer _layer;
	private PanelContainer _panel;
	private Label _label;
	private float _refreshTimer;
	private double _smoothedFrameMs;

	private float _minFpsCurrent = float.MaxValue;
	private float _minFpsLast = float.MaxValue;
	private float _minFpsWindowTimer;
	private const float MinFpsHalfWindow = 1.0f;

	private double _smoothedProcMs;
	private double _smoothedPhysMs;
	private double _smoothedGpuMs;
	private double _smoothedRenderCpuMs;
	private double _frameMaxWindow;
	private double _procMaxWindow, _physMaxWindow;
	private float _maxWindowTimer;
	private const float MaxWindowSec = 1.0f;

	/// <summary>Builds the overlay layer, panel, and label; toggles visibility per Settings.</summary>
	public override void _Ready()
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server) { QueueFree(); return; }
		_layer = new CanvasLayer { Layer = 100 };
		AddChild(_layer);
		HudGate.Register(_layer);

		_panel = new PanelContainer
		{
			AnchorLeft = 0f,
			AnchorRight = 1f,
			AnchorTop = 0f,
			AnchorBottom = 0f,
			OffsetTop = 0f,
			OffsetBottom = 0f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};

		var style = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0.55f),
			BorderWidthBottom = 1,
			BorderColor = new Color(0.4f, 0.6f, 0.4f, 0.3f),
		};
		style.ContentMarginLeft = 8f;
		style.ContentMarginRight = 8f;
		style.ContentMarginTop = 4f;
		style.ContentMarginBottom = 4f;
		_panel.AddThemeStyleboxOverride("panel", style);
		_layer.AddChild(_panel);

		_label = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.Off,
			ClipText = true,
		};
		_label.AddThemeColorOverride("font_color", new Color(0.85f, 1f, 0.85f));
		_label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
		_label.AddThemeConstantOverride("outline_size", 3);
		_label.AddThemeConstantOverride("line_spacing", 0);
		_label.AddThemeFontSizeOverride("font_size", FontSize);
		_panel.AddChild(_label);

		_panel.Visible = Settings.ShowDebugBar;
	}

	/// <summary>Per-frame: keeps smoothed metrics current and refreshes the label at UpdateInterval.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("DebugOverlay._Process");
		if (_panel.Visible != Settings.ShowDebugBar) _panel.Visible = Settings.ShowDebugBar;
		if (!_panel.Visible) return;

		if (Player == null) Player = NetMain.Instance?.FindLocalPlayer();

		_smoothedFrameMs = _smoothedFrameMs * 0.9 + (delta * 1000.0) * 0.1;

		double procNow = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
		double physNow = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
		_smoothedProcMs = _smoothedProcMs * 0.9 + procNow * 0.1;
		_smoothedPhysMs = _smoothedPhysMs * 0.9 + physNow * 0.1;
		if (procNow > _procMaxWindow) _procMaxWindow = procNow;
		if (physNow > _physMaxWindow) _physMaxWindow = physNow;
		_maxWindowTimer += (float)delta;
		if (_maxWindowTimer >= MaxWindowSec)
		{
			_procMaxWindow = procNow;
			_physMaxWindow = physNow;
			_frameMaxWindow = _smoothedFrameMs;
			_maxWindowTimer = 0f;
		}

		float currentFps = (float)Engine.GetFramesPerSecond();
		if (currentFps > 0f && currentFps < _minFpsCurrent) _minFpsCurrent = currentFps;
		_minFpsWindowTimer += (float)delta;
		if (_minFpsWindowTimer >= MinFpsHalfWindow)
		{
			_minFpsLast = _minFpsCurrent;
			_minFpsCurrent = float.MaxValue;
			_minFpsWindowTimer = 0f;
		}

		_refreshTimer -= (float)delta;
		if (_refreshTimer > 0f) return;
		_refreshTimer = UpdateInterval;

		_label.Text = BuildText();
	}

	/// <summary>Composes the single-line overlay text from engine, performance and player stats.</summary>
	private string BuildText()
	{
		double fps = Engine.GetFramesPerSecond();
		float minFps = Mathf.Min(_minFpsCurrent, _minFpsLast);
		string minStr = (minFps < float.MaxValue && minFps < fps * 0.95f) ? $" ▼{minFps:F0}" : "";
		int physicsTps = Engine.PhysicsTicksPerSecond;
		double interp = Engine.GetPhysicsInterpolationFraction();
		float ramMb = OS.GetStaticMemoryUsage() / (1024f * 1024f);
		float vramMb = (float)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024f * 1024f);
		int drawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
		int objects = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
		long primitives = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
		double gpuNow = NetMain.Instance?.MeasuredGpuMs() ?? 0.0;
		double rcpuNow = NetMain.Instance?.MeasuredRenderCpuMs() ?? 0.0;
		_smoothedGpuMs = _smoothedGpuMs * 0.9 + gpuNow * 0.1;
		_smoothedRenderCpuMs = _smoothedRenderCpuMs * 0.9 + rcpuNow * 0.1;
		if (_smoothedFrameMs > _frameMaxWindow) _frameMaxWindow = _smoothedFrameMs;
		string bound = _smoothedGpuMs >= _smoothedFrameMs * 0.9 ? "GPU-bound" : "CPU-bound";
		string engine = $"[Frame {_smoothedFrameMs:F2}ms↑{_frameMaxWindow:F1} RCpu {_smoothedRenderCpuMs:F2} GPU {_smoothedGpuMs:F2} Phys {_smoothedPhysMs:F2} {bound} @ {physicsTps}Hz] │ FPS {fps:F0}{minStr} f={interp:F2} │ RAM {ramMb:F0}MB │ VRAM {vramMb:F0}MB │ Draw {drawCalls} Tri {primitives/1000}k Obj {objects}";

		if (Player == null) return engine + " │ (no Player)";

		var mc = Player.Movement;
		Vector3 pos = Player.GlobalPosition;
		int staminaPct = Mathf.RoundToInt(mc.Stamina / Mathf.Max(1f, ConVars.Sv.MaxStamina) * 100f);
		string stateTag = mc.IsSliding ? "SLIDE " : mc.ActuallySprinting ? "SPRINT " : (mc.SprintExhausted ? "exh " : "");

		string adsStr = mc.AdsBlend > 0.01f ? $" │ ADS {mc.AdsBlend:F1}" : "";
		string player = $"Pos ({pos.X:F1},{pos.Y:F1},{pos.Z:F1}) │ {stateTag}{staminaPct}% │ Spd {mc.HorizontalSpeed:F1} m/s │ Cr {mc.CrouchBlend:F1} Raise {mc.WeaponRaiseBlend:F1}{adsStr} │ netId {Player.NetId}";

		return engine + " │ " + player;
	}
}
