using Godot;

namespace Vantix;

/// <summary>Autoload CanvasLayer owning a full-screen black ColorRect that masks the hard cut when SceneLoader
/// switches into world.tscn (hides the first-frame render burst). SceneLoader calls <see cref="ShowOpaque"/>
/// before the switch; NetworkPlayer._Ready calls <see cref="RequestFadeOut"/> once preloads + spawn are done.
/// Survives scene switches as an autoload.</summary>
public partial class WorldFadeOverlay : CanvasLayer
{
	[Export]
	public int LayerOrder = 1000;

	/// <summary>Default fade-out duration (s) when <see cref="RequestFadeOut"/> is called without a value.</summary>
	public const float DefaultFadeDurationSec = 0.15f;

	/// <summary>Singleton, set in <see cref="_Ready"/>.</summary>
	public static WorldFadeOverlay Instance { get; private set; }

	private ColorRect _rect;
	private float _fadeRemaining;
	private float _fadeTotal;
	private bool _fading;

	public override void _Ready()
	{
		Instance = this;
		Layer = LayerOrder;
		_rect = new ColorRect { Color = new Color(0f, 0f, 0f, 1f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_rect);
		_rect.Visible = false;
		ProcessMode = ProcessModeEnum.Always;
		SetProcess(false);
	}

	/// <summary>Snaps the overlay to opaque black immediately.</summary>
	public void ShowOpaque()
	{
		if (_rect == null)
			return;
		_rect.Color = new Color(0f, 0f, 0f, 1f);
		_rect.Visible = true;
		_fading = false;
		SetProcess(false);
	}

	/// <summary>Begins a smooth alpha fade-out over <paramref name="duration"/> seconds.</summary>
	public void RequestFadeOut(float duration = DefaultFadeDurationSec)
	{
		if (_rect == null || !_rect.Visible)
			return;
		_fadeTotal = Mathf.Max(0.01f, duration);
		_fadeRemaining = _fadeTotal;
		_fading = true;
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (!_fading || _rect == null)
		{
			SetProcess(false);
			return;
		}
		_fadeRemaining -= (float)delta;
		float alpha = Mathf.Clamp(_fadeRemaining / _fadeTotal, 0f, 1f);
		Color c = _rect.Color;
		c.A = alpha;
		_rect.Color = c;
		if (_fadeRemaining <= 0f)
		{
			_rect.Visible = false;
			_fading = false;
			SetProcess(false);
		}
	}
}
