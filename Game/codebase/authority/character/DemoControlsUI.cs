using Godot;

[Tool, GlobalClass]
public partial class DemoControlsUI : Control
{
	[Export] public NodePath AnimationPlayerPath;
	[Export] public NodePath InputPath;
	[Export] public NodePath CharacterPath;
	[Export] public Key ToggleKey = Key.Tab;
	[Export] public bool ControlsVisibleOnStart;

	private AnimationPlayer _player;
	private FpsDemoInput _input;
	private AnimatedCharacter _char;
	private Label _animLabel;
	private Label _infoLabel;
	private Label _hintLabel;
	private PanelContainer _controlsPanel;
	private string _lastAnim = "";

	private static readonly (string Section, (string Key, string Action)[] Rows)[] ControlsLayout =
	{
		("MOVEMENT", new[] { ("WASD", "Move"), ("Shift", "Run"), ("Alt", "Sprint (tactical)"), ("Space", "Jump"), ("C", "Crouch") }),
		("COMBAT", new[] { ("LMB", "Fire"), ("RMB", "Aim Down Sights"), ("MMB", "Toggle Canted Aim"), ("V", "Cycle Fire Mode"), ("R", "Reload  (hold: Mag Check)"), ("Q", "Quick Reload"), ("E", "Empty Reload"), ("J", "Clear Jam"), ("G", "Grenade  (hold: Aim, release: Throw)"), ("F", "Melee") }),
		("HANDLING", new[] { ("T", "Inspect  (hold: Inspect Empty)"), ("B", "Grip Change (cycle)"), ("H", "Equip Weapon"), ("U", "Heal Syringe"), ("X", "Interact") }),
		("DEMO", new[] { ("F7", "Toggle Camera (FPS/TPS)"), ("Wheel", "Zoom TPS Camera"), ("L", "Toggle Camera Animation"), ("TAB", "Show / Hide Controls"), ("ESC", "Quit") }),
	};

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		if (Engine.IsEditorHint()) return;

		_player = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
		_input = GetNodeOrNull<FpsDemoInput>(InputPath);
		_char = GetNodeOrNull<AnimatedCharacter>(CharacterPath);
		BuildHud();
		_controlsPanel.Visible = ControlsVisibleOnStart;
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint()) return;

		if (_animLabel != null && _player != null)
		{
			string cur = CleanAnimName(_player.CurrentAnimation);
			if (cur != _lastAnim)
			{
				_lastAnim = cur;
				_animLabel.Text = $"Current Animation\n{cur}";
			}
		}
		if (_infoLabel != null && _input != null)
		{
			string cam = _char != null ? (_char.CurrentViewMode == ViewMode.Tps ? "TPS" : "FPS") : "—";
			_infoLabel.Text = $"Camera: {cam}\nFire Mode: {_input.FireModeName}\nAmmo: {_input.Ammo} / {_input.MagSizeValue}";
		}
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (Engine.IsEditorHint() || _controlsPanel == null) return;
		if (e is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == ToggleKey)
		{
			_controlsPanel.Visible = !_controlsPanel.Visible;
			GetViewport().SetInputAsHandled();
		}
	}

	private static string CleanAnimName(string raw)
	{
		if (string.IsNullOrEmpty(raw)) return "—";
		int slash = raw.LastIndexOf('/');
		string name = slash >= 0 ? raw.Substring(slash + 1) : raw;
		foreach (string prefix in new[] { "A_TFA_FP_AR_", "AM_TFA_FP_AR_", "A_TFA_FP_", "A_TFA_" })
			if (name.StartsWith(prefix)) { name = name.Substring(prefix.Length); break; }
		return name.Replace('_', ' ');
	}

	private void BuildHud()
	{
		_animLabel = MakeLabel("", HorizontalAlignment.Center);
		_animLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
		_animLabel.OffsetTop = 18;
		_animLabel.AddThemeFontSizeOverride("font_size", 20);
		AddChild(_animLabel);

		_infoLabel = MakeLabel("", HorizontalAlignment.Right);
		_infoLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
		_infoLabel.GrowHorizontal = GrowDirection.Begin;
		_infoLabel.GrowVertical = GrowDirection.Begin;
		_infoLabel.OffsetRight = -20;
		_infoLabel.OffsetBottom = -18;
		_infoLabel.VerticalAlignment = VerticalAlignment.Bottom;
		AddChild(_infoLabel);

		_hintLabel = MakeLabel("Press  [TAB]  for Controls", HorizontalAlignment.Left);
		_hintLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
		_hintLabel.GrowVertical = GrowDirection.Begin;
		_hintLabel.OffsetLeft = 20;
		_hintLabel.OffsetBottom = -18;
		AddChild(_hintLabel);

		_controlsPanel = BuildControlsPanel();
		AddChild(_controlsPanel);
		_controlsPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterLeft);
		_controlsPanel.GrowVertical = GrowDirection.Both;
		_controlsPanel.OffsetLeft = 20;
	}

	private static Label MakeLabel(string text, HorizontalAlignment align)
	{
		var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };
		l.AddThemeColorOverride("font_color", Colors.White);
		l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
		l.AddThemeConstantOverride("outline_size", 5);
		return l;
	}

	private PanelContainer BuildControlsPanel()
	{
		var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.05f, 0.07f, 0.72f),
			ContentMarginLeft = 18, ContentMarginRight = 18,
			ContentMarginTop = 14, ContentMarginBottom = 14,
			CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
		};
		panel.AddThemeStyleboxOverride("panel", style);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 4);
		panel.AddChild(vbox);

		var title = new Label { Text = "CONTROLS" };
		title.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
		title.AddThemeFontSizeOverride("font_size", 18);
		vbox.AddChild(title);

		foreach (var (section, rows) in ControlsLayout)
		{
			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

			var head = new Label { Text = section };
			head.AddThemeColorOverride("font_color", new Color(0.5f, 0.75f, 1f));
			head.AddThemeFontSizeOverride("font_size", 13);
			vbox.AddChild(head);

			var grid = new GridContainer { Columns = 2 };
			grid.AddThemeConstantOverride("h_separation", 18);
			grid.AddThemeConstantOverride("v_separation", 2);
			foreach (var (key, action) in rows)
			{
				var kl = new Label { Text = key, CustomMinimumSize = new Vector2(104, 0) };
				kl.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.7f));
				var al = new Label { Text = action };
				al.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
				grid.AddChild(kl);
				grid.AddChild(al);
			}
			vbox.AddChild(grid);
		}
		return panel;
	}
}
