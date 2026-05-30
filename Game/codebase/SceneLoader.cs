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
	/// Server/Listen-Mode jumps straight to LoadingWorld (no remote connection needed).
	/// PreloadingAudio scans res://audio/footsteps/ and triggers background ResourceLoader
	/// requests so the first step on each surface doesn't cold-load (~40ms hitch). PreloadingAnims
	/// is a short cosmetic delay covering the AnimationTree one-shot pre-warm that PlayerCore._Ready
	/// performs after the scene switch — typically &lt;100ms, but the user gets a visible message.</summary>
	private enum LoadPhase
	{
		Connecting,
		Handshaking,
		LoadingWorld,
		PreloadingAudio,
		PreloadingAnims,
		SwitchingScene,
	}

	private const string FootstepAudioRoot = "res://audio/footsteps";
	private const float PreloadAnimsCosmeticSec = 0.40f;

	private readonly Godot.Collections.Array _progress = new();
	private readonly System.Collections.Generic.List<string> _audioPaths = new();
	private int _audioFinalizedCount;
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
					SetPhase(LoadPhase.Handshaking, "Server-Handshake…");
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
					SetPhase(LoadPhase.PreloadingAnims, "Lade Animationen…");
				}
				return;

			case LoadPhase.PreloadingAnims:
				// Cosmetic phase — animations are bundled inside the loaded PackedScene and
				// won't actually do file-IO here. The real animation pre-warm runs in
				// PlayerCore._Ready post-scene-switch. We just show the message + ramp the
				// bar so the user sees "what's happening" before the audio queue starts.
				_targetRatio = Mathf.Clamp(_phaseTimer / PreloadAnimsCosmeticSec, 0f, 1f);
				_shownRatio = Mathf.MoveToward(_shownRatio, _targetRatio, (float)delta * BarFollowSpeed);
				_bar.Value = _shownRatio * 100.0;
				_percent.Text = $"{Mathf.RoundToInt(_shownRatio * 100f)} %";
				if (_phaseTimer >= PreloadAnimsCosmeticSec)
				{
					SetPhase(LoadPhase.PreloadingAudio, "Lade Audio…");
					BeginAudioPreload();
				}
				return;

			case LoadPhase.PreloadingAudio:
				PollAudioPreload();
				_shownRatio = Mathf.MoveToward(_shownRatio, _targetRatio, (float)delta * BarFollowSpeed);
				_bar.Value = _shownRatio * 100.0;
				int total = _audioPaths.Count;
				_percent.Text = total > 0 ? $"{_audioFinalizedCount}/{total}" : "0/0";
				if (total == 0 || _audioFinalizedCount >= total)
				{
					SetPhase(LoadPhase.SwitchingScene, "Spawne Spieler…");
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
					// Snap the WorldFadeOverlay to opaque black BEFORE switching — masks
					// the first-frame render burst (shader compile, lightmap upload, materials
					// lazy-binding). PlayerCore._Ready later calls RequestFadeOut() once
					// preloads + spawn are done.
					WorldFadeOverlay.Instance?.ShowOpaque();
					GetTree().ChangeSceneToPacked(_loadedScene);
				}
				return;
		}
	}

	/// <summary>Recursively scans res://audio/footsteps/ for .wav clips and kicks off threaded
	/// loads for each. The list is what FootstepAudio.ClipPaths references — we don't have access
	/// to that node yet (scene not switched), so we mirror the on-disk paths instead. Each load
	/// is async; finalization (= main-thread import) happens later when FootstepAudio._Process
	/// polls them, but the bulk of the I/O cost is already paid here.</summary>
	private void BeginAudioPreload()
	{
		_audioPaths.Clear();
		_audioFinalizedCount = 0;
		using var root = DirAccess.Open(FootstepAudioRoot);
		if (root == null) return;
		foreach (string subName in root.GetDirectories())
		{
			using var sub = DirAccess.Open($"{FootstepAudioRoot}/{subName}");
			if (sub == null) continue;
			foreach (string fileName in sub.GetFiles())
			{
				if (fileName.EndsWith(".wav") || fileName.EndsWith(".ogg") || fileName.EndsWith(".mp3"))
					_audioPaths.Add($"{FootstepAudioRoot}/{subName}/{fileName}");
			}
		}
		foreach (string path in _audioPaths)
			ResourceLoader.LoadThreadedRequest(path);
	}

	/// <summary>Polls the status of every queued audio path; counts the number that have reached terminal state (Loaded or Failed). Drives both the percent label and the phase-completion check.</summary>
	private void PollAudioPreload()
	{
		int finalized = 0;
		for (int i = 0; i < _audioPaths.Count; i++)
		{
			var s = ResourceLoader.LoadThreadedGetStatus(_audioPaths[i]);
			if (s == ResourceLoader.ThreadLoadStatus.Loaded
				|| s == ResourceLoader.ThreadLoadStatus.Failed
				|| s == ResourceLoader.ThreadLoadStatus.InvalidResource)
				finalized++;
		}
		_audioFinalizedCount = finalized;
		_targetRatio = _audioPaths.Count > 0 ? (float)finalized / _audioPaths.Count : 1f;
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
