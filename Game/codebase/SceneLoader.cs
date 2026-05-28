using Godot;

/// <summary>
/// Loading screen and project startup scene. Loads world.tscn in the background
/// (ResourceLoader threaded) and shows a progress bar while loading; once the
/// scene is ready the bar fills to 100% and the tree switches over.
///
/// Look: red ETA-style background (same red as the boot splash for a seamless
/// transition) with a white bar. The UI is built in code (like HudCs2) — the
/// .tscn only holds the root node.
/// </summary>
public partial class SceneLoader : Control
{
	private const string TargetScene = "res://world.tscn";
	private const float BarFollowSpeed = 1.6f;
	private const float ConnectTimeoutSec = 15f;

	private static readonly Color EtaRed = new(0.7529412f, 0.007843138f, 0.003921569f);

	private ProgressBar _bar;
	private Label _percent;
	private Label _statusLabel;

	/// <summary>Loading phases the player sees. Client steps through ALL phases sequentially,
	/// Server/Listen-Mode jumps straight to LoadingWorld (no remote connection needed).</summary>
	private enum LoadPhase
	{
		Connecting,
		Handshaking,
		LoadingWorld,
		SwitchingScene,
	}

	private readonly Godot.Collections.Array _progress = new();
	private float _targetRatio;
	private float _shownRatio;
	private bool _loaded;
	private bool _switched;
	private bool _failed;
	private float _phaseTimer;
	private LoadPhase _phase = LoadPhase.LoadingWorld;
	private PackedScene _loadedScene;

	/// <summary>Builds the UI and decides whether to start connecting (client) or loading directly (listen/server).</summary>
	public override void _Ready()
	{
		BuildUi();
		var mode = NetMain.Instance?.Cli?.Mode ?? NetMode.Listen;
		if (mode == NetMode.Client)
		{
			SetPhase(LoadPhase.Connecting, "Verbinde zum Server…");
		}
		else
		{
			SetPhase(LoadPhase.LoadingWorld, "Lade Welt…");
			BeginWorldLoad();
		}
	}

	/// <summary>Switches to the given phase and resets per-phase progress state.</summary>
	private void SetPhase(LoadPhase p, string status)
	{
		_phase = p;
		_phaseTimer = 0f;
		_statusLabel.Text = status;
		_targetRatio = 0f;
		_shownRatio = 0f;
	}

	/// <summary>Kicks off the threaded world load.</summary>
	private void BeginWorldLoad()
	{
		Error err = ResourceLoader.LoadThreadedRequest(TargetScene);
		if (err != Error.Ok)
		{
			GD.PrintErr($"[SceneLoader] LoadThreadedRequest({TargetScene}) → {err}");
			SetProcess(false);
		}
	}

	/// <summary>Per-frame phase driver: polls load progress, animates pulsing bars and triggers the scene switch.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("SceneLoader._Process");
		if (_failed) return;
		_phaseTimer += (float)delta;

		switch (_phase)
		{
			case LoadPhase.Connecting:
				if (NetMain.Instance?.Client?.Connected == true)
				{
					SetPhase(LoadPhase.Handshaking, "Erhalte Spawn-Daten vom Server…");
				}
				else if (_phaseTimer > ConnectTimeoutSec)
				{
					_failed = true;
					_statusLabel.Text = "Verbindung zum Server fehlgeschlagen.\nBitte Server prüfen und neu starten.";
					_bar.Visible = false;
					_percent.Visible = false;
					SetProcess(false);
					return;
				}
				else
				{
					_shownRatio = 0.5f + 0.4f * Mathf.Sin(_phaseTimer * 3f);
					_bar.Value = _shownRatio * 100.0;
					_percent.Text = $"{(int)_phaseTimer}s";
				}
				return;

			case LoadPhase.Handshaking:
				if (NetMain.Instance?.Client?.Spawned == true)
				{
					SetPhase(LoadPhase.LoadingWorld, "Lade Welt…");
					BeginWorldLoad();
				}
				else if (_phaseTimer > ConnectTimeoutSec)
				{
					_failed = true;
					_statusLabel.Text = "Server akzeptiert keine Spawn-Anfrage.\nVerbindung wird abgebrochen.";
					_bar.Visible = false;
					_percent.Visible = false;
					SetProcess(false);
					return;
				}
				else
				{
					_shownRatio = 0.5f + 0.4f * Mathf.Sin(_phaseTimer * 4f);
					_bar.Value = _shownRatio * 100.0;
					_percent.Text = $"{(int)_phaseTimer}s";
				}
				return;

			case LoadPhase.LoadingWorld:
				if (!_loaded) PollLoad();
				_shownRatio = Mathf.MoveToward(_shownRatio, _targetRatio, (float)delta * BarFollowSpeed);
				_bar.Value = _shownRatio * 100.0;
				_percent.Text = $"{Mathf.RoundToInt(_shownRatio * 100f)} %";
				if (_loaded && _shownRatio >= 0.999f)
				{
					SetPhase(LoadPhase.SwitchingScene, "Welt geladen — spawne Spieler…");
					_targetRatio = 1f;
					_shownRatio = 1f;
					_bar.Value = 100.0;
					_percent.Text = "100 %";
				}
				return;

			case LoadPhase.SwitchingScene:
				if (!_switched && _phaseTimer > 0.25f)
				{
					_switched = true;
					GetTree().ChangeSceneToPacked(_loadedScene);
				}
				return;
		}
	}

	/// <summary>Polls the background load and updates target progress/status.</summary>
	private void PollLoad()
	{
		switch (ResourceLoader.LoadThreadedGetStatus(TargetScene, _progress))
		{
			case ResourceLoader.ThreadLoadStatus.InProgress:
				_targetRatio = _progress.Count > 0 ? _progress[0].AsSingle() : 0f;
				break;
			case ResourceLoader.ThreadLoadStatus.Loaded:
				_loaded = true;
				_targetRatio = 1f;
				_loadedScene = (PackedScene)ResourceLoader.LoadThreadedGet(TargetScene);
				break;
			case ResourceLoader.ThreadLoadStatus.Failed:
			case ResourceLoader.ThreadLoadStatus.InvalidResource:
				GD.PrintErr($"[SceneLoader] Laden von {TargetScene} fehlgeschlagen.");
				SetProcess(false);
				break;
		}
	}

	/// <summary>Builds the red loading screen with a centered white bar (code-driven UI).</summary>
	private void BuildUi()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;

		var bg = new ColorRect { Color = EtaRed, MouseFilter = MouseFilterEnum.Ignore };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		col.AddThemeConstantOverride("separation", 14);
		center.AddChild(col);

		var title = new Label { Text = "LADEN", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 24);
		title.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.95f));
		col.AddChild(title);

		_statusLabel = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		_statusLabel.AddThemeFontSizeOverride("font_size", 15);
		_statusLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
		col.AddChild(_statusLabel);

		_bar = new ProgressBar
		{
			CustomMinimumSize = new Vector2(460f, 10f),
			MinValue = 0.0,
			MaxValue = 100.0,
			Value = 0.0,
			ShowPercentage = false,
		};
		StyleBar(_bar);
		col.AddChild(_bar);

		_percent = new Label { Text = "0 %", HorizontalAlignment = HorizontalAlignment.Center };
		_percent.AddThemeFontSizeOverride("font_size", 13);
		_percent.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.7f));
		col.AddChild(_percent);
	}

	/// <summary>White bar: subtle track, opaque white fill — both rounded.</summary>
	private static void StyleBar(ProgressBar bar)
	{
		var track = new StyleBoxFlat
		{
			BgColor = new Color(1f, 1f, 1f, 0.16f),
			CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
			CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
		};
		var fill = new StyleBoxFlat
		{
			BgColor = new Color(1f, 1f, 1f, 1f),
			CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
			CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
		};
		bar.AddThemeStyleboxOverride("background", track);
		bar.AddThemeStyleboxOverride("fill", fill);
	}
}
