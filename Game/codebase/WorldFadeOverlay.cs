using Godot;

/// <summary>
/// Autoload CanvasLayer that owns a full-screen black ColorRect. Used to mask the
/// "hard cut" when SceneLoader.ChangeSceneToPacked switches into world.tscn —
/// without it the loading screen vanishes and the world appears instantly, often
/// during the worst-case first-frame render burst (shaders compile, lightmap
/// uploads, materials lazy-load).
///
/// Workflow:
/// 1. SceneLoader, just before the scene switch, calls <see cref="ShowOpaque"/>
///    → overlay turns fully black, sits on top of everything (LayerOrder = 1000).
/// 2. After the switch, world.tscn is instantiated, players spawn, NetworkPlayer._Ready
///    runs the animation pre-warm. Player camera renders into the now-active
///    viewport but the player sees only the black overlay.
/// 3. NetworkPlayer (or other "ready" trigger) calls <see cref="RequestFadeOut"/> with
///    a duration — the overlay alpha lerps to zero, then sets Visible=false.
///
/// As an autoload the node survives every scene switch and is reachable via the
/// generated autoload property `WorldFadeOverlay`, so anybody can call ShowOpaque
/// / RequestFadeOut without lookup gymnastics.
/// </summary>
public partial class WorldFadeOverlay : CanvasLayer
{
	[Export] public int LayerOrder = 1000;
	/// <summary>Default fade-out duration (seconds) if <see cref="RequestFadeOut"/> is called without a custom value.</summary>
	public const float DefaultFadeDurationSec = 0.15f;

	/// <summary>Singleton reference — set in <see cref="_Ready"/>. Used by callers (SceneLoader, NetworkPlayer) that need to drive the overlay across scene switches without going through GetTree().GetFirstNodeInGroup() every time.</summary>
	public static WorldFadeOverlay Instance { get; private set; }

	private ColorRect _rect;
	private float _fadeRemaining;
	private float _fadeTotal;
	private bool _fading;

	public override void _Ready()
	{
		Instance = this;
		Layer = LayerOrder;
		_rect = new ColorRect
		{
			Color = new Color(0f, 0f, 0f, 1f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_rect);
		_rect.Visible = false;
		ProcessMode = ProcessModeEnum.Always;
		SetProcess(false);
	}

	/// <summary>Snap overlay to opaque black immediately. Used by SceneLoader before switching scenes to mask the first-frame render burst.</summary>
	public void ShowOpaque()
	{
		if (_rect == null) return;
		_rect.Color = new Color(0f, 0f, 0f, 1f);
		_rect.Visible = true;
		_fading = false;
		SetProcess(false);
	}

	/// <summary>Begin a smooth alpha fade-out over <paramref name="duration"/> seconds. Caller is typically NetworkPlayer._Ready once all preloads complete and the player is spawned.</summary>
	public void RequestFadeOut(float duration = DefaultFadeDurationSec)
	{
		if (_rect == null || !_rect.Visible) return;
		_fadeTotal = Mathf.Max(0.01f, duration);
		_fadeRemaining = _fadeTotal;
		_fading = true;
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (!_fading || _rect == null) { SetProcess(false); return; }
		_fadeRemaining -= (float)delta;
		float alpha = Mathf.Clamp(_fadeRemaining / _fadeTotal, 0f, 1f);
		Color c = _rect.Color; c.A = alpha; _rect.Color = c;
		if (_fadeRemaining <= 0f)
		{
			_rect.Visible = false;
			_fading = false;
			SetProcess(false);
		}
	}
}
