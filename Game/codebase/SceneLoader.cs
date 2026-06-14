using Godot;

/// <summary>Loading screen and startup scene. Loads world.tscn in the background (threaded ResourceLoader) with a
/// progress bar, then switches the tree once ready. Code-driven UI; the .tscn only holds the root node.</summary>
public partial class SceneLoader : Control
{
	private const string TargetScene = "res://world.tscn";
	private const float BarFollowSpeed = 1.6f;
	// 30s: a freshly-launched server can take this long to finish its world load before accepting spawn requests.
	private const float ConnectTimeoutSec = 30f;

	private const string LogoPath = "res://logo.svg";
	private const float LogoSize = 96f;
	private const float LogoMargin = 32f;

	private ProgressBar _bar;
	private Label _percent;
	private Label _statusLabel;
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
		// Post-disconnect re-entry: DisconnectScreen is up on top; just show a clean background, no fresh connect.
		// _failed disables _Process and hides the bar (status text would be covered by the overlay anyway).
		if (NetMain.PostDisconnectIdle)
		{
			_failed = true;
			_bar.Visible = false;
			_percent.Visible = false;
			_statusLabel.Text = "";
			SetProcess(false);
			return;
		}
		var mode = NetMain.Instance?.Cli?.Mode ?? NetMode.Listen;
		if (mode == NetMode.Client)
		{
			SetPhase(LoadPhase.Connecting, "Connecting to server…");
		}
		else
		{
			SetPhase(LoadPhase.LoadingWorld, "Loading world…");
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

	private ulong _worldLoadStartMs;

	/// <summary>Kicks off the threaded world load.</summary>
	private void BeginWorldLoad()
	{
		_worldLoadStartMs = Time.GetTicksMsec();
		var mode = NetMain.Instance?.Cli?.Mode ?? NetMode.Listen;
		GD.Print($"[SceneLoader] ({mode}) Loading map {TargetScene} …");
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
		if (_failed)
			return;
		_phaseTimer += (float)delta;

		switch (_phase)
		{
			case LoadPhase.Connecting:
				if (NetMain.Instance?.Client?.Connected == true)
				{
					SetPhase(LoadPhase.Handshaking, "Server handshake…");
				}
				else if (_phaseTimer > ConnectTimeoutSec)
				{
					_failed = true;
					_statusLabel.Text = "Connection to server failed.\nCheck the server and try again.";
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
					SetPhase(LoadPhase.LoadingWorld, "Loading world…");
					BeginWorldLoad();
				}
				else if (_phaseTimer > ConnectTimeoutSec)
				{
					_failed = true;
					_statusLabel.Text = "Server is not accepting the spawn request.\nDisconnecting.";
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
				if (!_loaded)
					PollLoad();
				_shownRatio = Mathf.MoveToward(_shownRatio, _targetRatio, (float)delta * BarFollowSpeed);
				_bar.Value = _shownRatio * 100.0;
				_percent.Text = $"{Mathf.RoundToInt(_shownRatio * 100f)} %";
				if (_loaded && _shownRatio >= 0.999f)
				{
					SetPhase(LoadPhase.PreloadingAnims, "Loading animations…");
				}
				return;

			case LoadPhase.PreloadingAnims:
				// Cosmetic phase — anims are bundled in the PackedScene (no file-IO here); the real pre-warm
				// runs in NetworkPlayer._Ready post-switch. Just shows a message + ramps the bar.
				_targetRatio = Mathf.Clamp(_phaseTimer / PreloadAnimsCosmeticSec, 0f, 1f);
				_shownRatio = Mathf.MoveToward(_shownRatio, _targetRatio, (float)delta * BarFollowSpeed);
				_bar.Value = _shownRatio * 100.0;
				_percent.Text = $"{Mathf.RoundToInt(_shownRatio * 100f)} %";
				if (_phaseTimer >= PreloadAnimsCosmeticSec)
				{
					SetPhase(LoadPhase.PreloadingAudio, "Loading audio…");
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
					SetPhase(LoadPhase.SwitchingScene, "Spawning player…");
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
					var mode = NetMain.Instance?.Cli?.Mode ?? NetMode.Listen;
					float secs = (Time.GetTicksMsec() - _worldLoadStartMs) / 1000f;
					GD.Print($"[SceneLoader] ({mode}) Map loaded in {secs:0.0}s → switching scene"
						+ (mode != NetMode.Client ? "  —  SERVER READY, accepting players" : ""));
					// Snap the overlay to opaque black before switching to mask the first-frame render burst;
					// NetworkPlayer._Ready later calls RequestFadeOut() once preloads + spawn are done.
					WorldFadeOverlay.Instance?.ShowOpaque();
					GetTree().ChangeSceneToPacked(_loadedScene);
				}
				return;
		}
	}

	/// <summary>Scans res://audio/footsteps/ and threaded-loads only the surface groups actually referenced by
	/// colliders in the loaded PackedScene (recursively across sub-scenes), plus <see cref="DefaultFootstepGroup"/>
	/// as the fallback. Skipped entirely on a dedicated server (never plays footstep audio).</summary>
	private void BeginAudioPreload()
	{
		_audioPaths.Clear();
		_audioFinalizedCount = 0;

		// Dedicated server has no audio output — preloading would only waste startup time and bandwidth.
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server)
			return;

		using var root = DirAccess.Open(FootstepAudioRoot);
		if (root == null)
			return;

		var usedGroups = ExtractSceneGroups(_loadedScene);
		// Always include the fallback material; must stay in sync with FootstepAudio.DefaultGroup.
		usedGroups.Add(DefaultFootstepGroup);
		foreach (string subName in root.GetDirectories())
		{
			// Floor colliders join a Godot group named after their audio/footsteps/<surface>/ folder.
			if (!usedGroups.Contains(subName))
				continue;
			using var sub = DirAccess.Open($"{FootstepAudioRoot}/{subName}");
			if (sub == null)
				continue;
			foreach (string fileName in sub.GetFiles())
			{
				if (fileName.EndsWith(".wav") || fileName.EndsWith(".ogg") || fileName.EndsWith(".mp3"))
					_audioPaths.Add($"{FootstepAudioRoot}/{subName}/{fileName}");
			}
		}
		foreach (string path in _audioPaths)
			ResourceLoader.LoadThreadedRequest(path);
	}

	/// <summary>Fallback footstep material; mirrors FootstepAudio.DefaultGroup so its clips preload even when the map has no "dirt" nodes.</summary>
	private const string DefaultFootstepGroup = "dirt";

	/// <summary>Recursively walks a PackedScene's SceneState (and instanced sub-scenes) to collect every group
	/// name without instantiating; callers match these against res://audio/footsteps/ folders. Recursion is
	/// needed because sub-scenes are opaque instance pointers in the parent state. Cycle-guarded via a visited
	/// set of PackedScene instance ids.</summary>
	private static System.Collections.Generic.HashSet<string> ExtractSceneGroups(PackedScene scene)
	{
		var groups = new System.Collections.Generic.HashSet<string>();
		var visited = new System.Collections.Generic.HashSet<ulong>();
		WalkScene(scene, groups, visited);
		return groups;
	}

	private static void WalkScene(PackedScene scene,
		System.Collections.Generic.HashSet<string> groups,
		System.Collections.Generic.HashSet<ulong> visited)
	{
		if (scene == null)
			return;
		if (!visited.Add(scene.GetInstanceId()))
			return;
		var state = scene.GetState();
		if (state == null)
			return;
		int nodeCount = state.GetNodeCount();
		for (int i = 0; i < nodeCount; i++)
		{
			var nodeGroups = state.GetNodeGroups(i);
			for (int g = 0; g < nodeGroups.Length; g++)
				groups.Add(nodeGroups[g].ToString());
			// Recurse into instanced sub-scenes — their surface groups are invisible from the parent state.
			var sub = state.GetNodeInstance(i);
			if (sub != null)
				WalkScene(sub, groups, visited);
		}
	}

	/// <summary>Counts queued audio paths that have reached a terminal state; drives the percent label and phase completion.</summary>
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
				GD.PrintErr($"[SceneLoader] Failed to load {TargetScene}.");
				SetProcess(false);
				break;
		}
	}

	/// <summary>Builds the black loading screen with the logo top-right and a centered white bar (code-driven UI).</summary>
	private void BuildUi()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;

		var bg = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var logo = new TextureRect
		{
			Texture = GD.Load<Texture2D>(LogoPath),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspect,
			CustomMinimumSize = new Vector2(LogoSize, LogoSize),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		logo.SetAnchorsPreset(LayoutPreset.TopRight);
		logo.OffsetLeft = -LogoSize - LogoMargin;
		logo.OffsetTop = LogoMargin;
		logo.OffsetRight = -LogoMargin;
		logo.OffsetBottom = LogoMargin + LogoSize;
		AddChild(logo);

		var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		col.AddThemeConstantOverride("separation", 14);
		center.AddChild(col);

		var title = new Label { Text = "LOADING", HorizontalAlignment = HorizontalAlignment.Center };
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
			CornerRadiusTopLeft = 5,
			CornerRadiusTopRight = 5,
			CornerRadiusBottomLeft = 5,
			CornerRadiusBottomRight = 5,
		};
		var fill = new StyleBoxFlat
		{
			BgColor = new Color(1f, 1f, 1f, 1f),
			CornerRadiusTopLeft = 5,
			CornerRadiusTopRight = 5,
			CornerRadiusBottomLeft = 5,
			CornerRadiusBottomRight = 5,
		};
		bar.AddThemeStyleboxOverride("background", track);
		bar.AddThemeStyleboxOverride("fill", fill);
	}
}
