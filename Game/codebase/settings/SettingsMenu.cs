using System.Collections.Generic;
using Godot;

namespace Vantix.Config;

/// <summary>Settings menu, opened via <see cref="ToggleKey"/> (ESC by default). Code-driven UI; live-applies on
/// change plus an explicit Save. A quality preset sets every individual value; tweaking one switches the preset to Custom.</summary>
public partial class SettingsMenu : CanvasLayer
{
	/// <summary>Set while the menu is open; other systems consult this to block input.</summary>
	public static bool IsAnyOpen { get; private set; }

	[Export]
	public Key ToggleKey = Key.Escape;

	[Export]
	public int LayerOrder = 200;

	private Control _root;
	private bool _isOpen;
	/// <summary>Mouse mode snapshotted on open and restored on close, so the menu works opened from the main menu (cursor visible) or in-game (cursor captured).</summary>
	private Input.MouseModeEnum _mouseModeBeforeOpen = Input.MouseModeEnum.Visible;

	private OptionButton _presetOpt;
	private HSlider _renderScaleSlider;
	private Label _renderScaleValue;
	private HSlider _viewmodelScaleSlider;
	private Label _viewmodelScaleValue;
	private HSlider _uiScaleSlider;
	private Label _uiScaleValue;
	private OptionButton _uiMsaaOpt;
	private OptionButton _aaOpt;
	private OptionButton _upscalerOpt;
	private OptionButton _shadowsOpt;
	private OptionButton _aoOpt;
	private OptionButton _reflectionsOpt;
	private OptionButton _reflectionProbesOpt;
	private OptionButton _volumetricFogOpt;
	private OptionButton _skyOpt;
	private OptionButton _cloudShadowsOpt;
	private OptionButton _postProcessingOpt;
	private OptionButton _cloudShadowDistOpt;
	private OptionButton _godRaysOpt;
	private OptionButton _lensFlareOpt;
	private OptionButton _dustMotesOpt;
	private OptionButton _motionBlurOpt;
	private OptionButton _filmGrainOpt;
	private OptionButton _vignetteOpt;
	private OptionButton _sharpeningOpt;
	private OptionButton _chromaticAberrationOpt;
	private OptionButton _adsDofOpt;
	private OptionButton _adsFovZoomOpt;
	private OptionButton _autoExposureOpt;
	private OptionButton _teamGlowOpt;
	private OptionButton _viewBobOpt;
	private OptionButton _sprintSwayOpt;
	private OptionButton _mouseInertiaOpt;
	private OptionButton _directionLeanOpt;
	private OptionButton _cameraShakeOpt;
	private OptionButton _showDebugBarOpt;
	private OptionButton _showNetGraphOpt;
	private OptionButton _weaponLightOpt;
	private OptionButton _windowModeOpt;
	private OptionButton _monitorOpt;
	private OptionButton _resolutionOpt;
	private OptionButton _vsyncOpt;
	private OptionButton _fpsCapOpt;
	private OptionButton _menuFpsCapOpt;
	private HSlider _brightnessSlider;
	private Label _brightnessValue;
	private HSlider _hudMarginHSlider;
	private Label _hudMarginHValue;
	private HSlider _hudMarginVSlider;
	private Label _hudMarginVValue;
	private HSlider _sensSlider;
	private Label _sensValue;
	private HSlider _fovSlider;
	private Label _fovValue;
	private Label _saveStatus;

	/// <summary>Suppresses change handlers while the UI is filled programmatically (else seeding controls would flip the preset to Custom).</summary>
	private bool _suppressEvents;

	private Vector2I[] _resolutions;
	private int[] _fpsCaps;
	private static readonly int[] MenuCaps = { 30, 60, 90, 120, 144 };

	/// <summary>Builds the menu, hides it, and seeds the controls from current Settings.</summary>
	public override void _Ready()
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server) { QueueFree(); return; }
		Layer = LayerOrder;
		BuildDynamicLists();
		BuildUI();
		SetOpen(false);
		PullStateFromSettings();
	}

	/// <summary>Builds the resolution + fps-cap dropdowns for <see cref="Settings.MonitorIndex"/>. Resolutions come
	/// from native enumeration (<see cref="Win32Display.EnumModes"/>/<see cref="LinuxDisplay.EnumModes"/>) for the
	/// exact advertised modes in true physical pixels; falls back to a hardcoded candidate list filtered by native
	/// size when no native backend exists (macOS/Wayland).</summary>
	private void BuildDynamicLists()
	{
		int screenCount = DisplayServer.GetScreenCount();
		int idx = Settings.MonitorIndex;
		if (idx < 0 || idx >= screenCount) idx = 0;

		Vector2I[] enumerated = Win32Display.IsSupported
			? Win32Display.EnumModes(idx)
			: LinuxDisplay.IsSupported ? LinuxDisplay.EnumModes(idx) : System.Array.Empty<Vector2I>();

		if (enumerated.Length > 0)
		{
			_resolutions = enumerated;
		}
		else
		{
			Vector2I native = DisplayServer.ScreenGetSize(idx);
			int maxW = native.X, maxH = native.Y;
			var candidates = new List<Vector2I>
			{
				new(640, 480),    new(800, 600),    new(1024, 768),   new(1280, 1024),  // 4:3
				new(1280, 720),   new(1366, 768),   new(1600, 900),   new(1920, 1080),  // 16:9
				new(2560, 1440),  new(3840, 2160),
				new(1280, 800),   new(1440, 900),   new(1680, 1050),  new(1920, 1200),  // 16:10
				new(2560, 1600),
				new(2560, 1080),  new(3440, 1440),  new(3840, 1600),  new(5120, 2160),  // 21:9 / 32:9
			};
			var resList = new List<Vector2I>();
			foreach (var r in candidates)
				if (r.X <= maxW && r.Y <= maxH)
					resList.Add(r);
			if (!resList.Contains(native)) resList.Add(native);
			resList.Sort((a, b) => (a.X * a.Y).CompareTo(b.X * b.Y));
			_resolutions = resList.ToArray();
		}

		int hz = (int)Mathf.Round(DisplayServer.ScreenGetRefreshRate(idx));
		if (hz <= 0) hz = 60;
		var capsSet = new HashSet<int> { 30, 60, hz, hz * 2 };
		var capsList = new List<int>(capsSet);
		capsList.Sort();
		capsList.Insert(0, 0);
		_fpsCaps = capsList.ToArray();
	}

	/// <summary>Formats "1920×1080 (16:9)"; drops the tag for unrecognised aspect ratios.</summary>
	private static string FormatResolutionLabel(Vector2I r)
	{
		string aspect = AspectRatioTag(r);
		return aspect != null ? $"{r.X}×{r.Y} ({aspect})" : $"{r.X}×{r.Y}";
	}

	private static string AspectRatioTag(Vector2I r)
	{
		if (r.Y == 0) return null;
		int g = Gcd(r.X, r.Y);
		int ax = r.X / g, ay = r.Y / g;
		// Snap to canonical ratios so e.g. 1366×768 still reads "16:9".
		float ratio = (float)r.X / r.Y;
		if (Mathf.Abs(ratio - 16f / 9f) < 0.02f) return "16:9";
		if (Mathf.Abs(ratio - 16f / 10f) < 0.02f) return "16:10";
		if (Mathf.Abs(ratio - 4f / 3f) < 0.02f) return "4:3";
		if (Mathf.Abs(ratio - 5f / 4f) < 0.02f) return "5:4";
		if (Mathf.Abs(ratio - 21f / 9f) < 0.03f) return "21:9";
		if (Mathf.Abs(ratio - 32f / 9f) < 0.03f) return "32:9";
		if (ax <= 32 && ay <= 32) return $"{ax}:{ay}";
		return null;
	}

	private static int Gcd(int a, int b) { while (b != 0) { int t = b; b = a % b; a = t; } return a; }

	/// <summary>Handles the toggle key to open/close the menu.</summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == ToggleKey)
		{
			SetOpen(!_isOpen);
			GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>Opens the menu programmatically (used by the main menu Settings button).</summary>
	public void Open() => SetOpen(true);

	/// <summary>Closes the menu programmatically.</summary>
	public void Close() => SetOpen(false);

	/// <summary>Opens/closes the menu, managing mouse mode (snapshot+restore) and the menu/game FPS-cap swap.</summary>
	private void SetOpen(bool open)
	{
		_isOpen = open;
		IsAnyOpen = open;
		_root.Visible = open;
		if (open)
		{
			_mouseModeBeforeOpen = Input.MouseMode;
			Input.MouseMode = Input.MouseModeEnum.Visible;
			Engine.MaxFps = Settings.MenuFpsCap;
			// Disconnect button only meaningful while connected to a server.
			if (_disconnectBtn != null)
				_disconnectBtn.Visible = NetMain.Instance?.Client?.Connected == true;
		}
		else
		{
			Input.MouseMode = _mouseModeBeforeOpen;
			Engine.MaxFps = Settings.FpsCap;
		}
	}

	/// <summary>Constructs the full menu layout (panel, tabs, save/close row).</summary>
	private void BuildUI()
	{
		_root = new Control
		{
			AnchorLeft = 0f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		AddChild(_root);

		var bg = new ColorRect
		{
			AnchorLeft = 0f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			Color = new Color(0f, 0f, 0f, 0f),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_root.AddChild(bg);

		var panel = new PanelContainer
		{
			AnchorLeft = 0.5f,
			AnchorTop = 0.5f,
			AnchorRight = 0.5f,
			AnchorBottom = 0.5f,
			OffsetLeft = -280f,
			OffsetRight = 280f,
			OffsetTop = -330f,
			OffsetBottom = 330f,
		};
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.1f, 0.12f, 0.95f),
			BorderColor = new Color(0.4f, 0.7f, 0.4f, 0.8f),
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
		};
		style.ContentMarginLeft = 24f;
		style.ContentMarginRight = 24f;
		style.ContentMarginTop = 18f;
		style.ContentMarginBottom = 18f;
		panel.AddThemeStyleboxOverride("panel", style);
		_root.AddChild(panel);

		var outer = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		outer.AddThemeConstantOverride("separation", 10);
		panel.AddChild(outer);

		var title = new Label { Text = "SETTINGS  (ESC to close)" };
		title.AddThemeFontSizeOverride("font_size", 18);
		title.AddThemeColorOverride("font_color", new Color(0.85f, 1f, 0.85f));
		outer.AddChild(title);

		var tabs = new TabContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		outer.AddChild(tabs);
		tabs.AddChild(BuildGraphicsTab());
		tabs.AddChild(BuildDisplayTab());
		tabs.AddChild(BuildControlsTab());

		outer.AddChild(new HSeparator());
		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 12);
		outer.AddChild(btnRow);

		var saveBtn = new Button { Text = "  Save  ", CustomMinimumSize = new Vector2(120, 36) };
		saveBtn.Pressed += OnSavePressed;
		btnRow.AddChild(saveBtn);

		var closeBtn = new Button { Text = "  Close  ", CustomMinimumSize = new Vector2(120, 36) };
		closeBtn.Pressed += () => SetOpen(false);
		btnRow.AddChild(closeBtn);

		_saveStatus = new Label { Text = "" };
		_saveStatus.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.7f));
		_saveStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btnRow.AddChild(_saveStatus);

		// Disconnect — visibility refreshed in SetOpen; only shown when connected.
		_disconnectBtn = new Button { Text = "  Disconnect  ", CustomMinimumSize = new Vector2(140, 36) };
		_disconnectBtn.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.5f));
		_disconnectBtn.Pressed += OnDisconnectPressed;
		btnRow.AddChild(_disconnectBtn);

		var quitBtn = new Button { Text = "  Quit  ", CustomMinimumSize = new Vector2(120, 36) };
		quitBtn.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.55f));
		quitBtn.Pressed += () => GetTree().Quit();
		btnRow.AddChild(quitBtn);
	}

	private Button _disconnectBtn;

	/// <summary>Disconnect button: closes the menu, then asks NetMain to tear down the connection.</summary>
	private void OnDisconnectPressed()
	{
		SetOpen(false);
		NetMain.Instance?.RequestDisconnect("Disconnected by user");
	}

	/// <summary>Creates a tab page (ScrollContainer + VBox). The node name becomes the tab title.</summary>
	private static (ScrollContainer page, VBoxContainer vbox) NewTabPage(string name)
	{
		var scroll = new ScrollContainer
		{
			Name = name,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		vbox.AddThemeConstantOverride("separation", 9);
		scroll.AddChild(vbox);
		return (scroll, vbox);
	}

	/// <summary>Builds the Graphics tab.</summary>
	private Control BuildGraphicsTab()
	{
		var (page, vbox) = NewTabPage("Graphics");

		AddSectionHeader(vbox, "QUALITY");
		_presetOpt = AddDropdown(vbox, "Quality-Preset", new[] { "Low", "Medium", "High", "Ultra", "Custom" });
		_presetOpt.ItemSelected += OnPresetChanged;

		(_renderScaleSlider, _renderScaleValue) = AddSlider(vbox, "Render Scale", 0.5f, 2.0f, 0.05f);
		_renderScaleSlider.ValueChanged += OnRenderScaleChanged;

		// >100% supersamples the weapon viewport (SSAA) — smooths iron-sight edges without temporal ghosting.
		(_viewmodelScaleSlider, _viewmodelScaleValue) = AddSlider(vbox, "Viewmodel Render Scale", 0.5f, 2.0f, 0.05f);
		_viewmodelScaleSlider.ValueChanged += OnViewmodelScaleChanged;

		_uiMsaaOpt = AddDropdown(vbox, "UI Quality (MSAA 2D)", new[] { "Off", "2×", "4×", "8×" });
		_uiMsaaOpt.ItemSelected += OnUiMsaaChanged;

		_aaOpt = AddDropdown(vbox, "Anti-Aliasing", new[] { "Off", "FXAA", "SMAA", "TAA" });
		_aaOpt.ItemSelected += OnAaChanged;

		_upscalerOpt = AddDropdown(vbox, "Upscaler", new[] { "Bilinear", "FSR 1.0", "FSR 2.0" });
		_upscalerOpt.ItemSelected += OnUpscalerChanged;

		_shadowsOpt = AddDropdown(vbox, "Shadows", new[] { "Off", "Low", "Medium", "High" });
		_shadowsOpt.ItemSelected += OnShadowsChanged;

		AddSectionHeader(vbox, "WORLD EFFECTS");
		_aoOpt = AddDropdown(vbox, "Ambient Occlusion", new[] { "Off", "On" });
		_aoOpt.ItemSelected += OnAoChanged;

		_reflectionsOpt = AddDropdown(vbox, "Reflections", new[] { "Off", "On" });
		_reflectionsOpt.ItemSelected += OnReflectionsChanged;

		// Reflection-probe atlas quality (per-probe pixel size). Change requires level reload.
		_reflectionProbesOpt = AddDropdown(
			vbox,
			"Reflection Probes",
			new[] { "Low (128)", "Medium (256)", "High (512)", "Ultra (1024)" }
		);
		_reflectionProbesOpt.ItemSelected += OnReflectionProbesChanged;

		// Off is a perf-diagnosis option; with Off, smoke-grenade FogVolumes render nothing. Indices 0..3 map 1:1 to the enum.
		_volumetricFogOpt = AddDropdown(vbox, "Volumetric Fog", new[] { "Off (smokes invisible)", "Low (64³)", "Medium (96³)", "High (160³)" });
		_volumetricFogOpt.ItemSelected += OnVolumetricFogChanged;

		_skyOpt = AddDropdown(vbox, "Sky", new[] { "Off", "On" });
		_skyOpt.ItemSelected += OnSkyChanged;

		_cloudShadowsOpt = AddDropdown(vbox, "Cloud Shadows", new[] { "Off", "On" });
		_cloudShadowsOpt.ItemSelected += OnCloudShadowsChanged;

		// Master toggle for the post-process compositor effect; Off skips the whole dispatch.
		_postProcessingOpt = AddDropdown(vbox, "Post Processing", new[] { "Off", "On" });
		_postProcessingOpt.ItemSelected += OnPostProcessingChanged;

		_cloudShadowDistOpt = AddDropdown(
			vbox,
			"Cloud Shadow Distance",
			new[] { "Near (60m)", "Medium (120m)", "Far (200m)" }
		);
		_cloudShadowDistOpt.ItemSelected += OnCloudShadowDistChanged;

		_godRaysOpt = AddDropdown(vbox, "God Rays", new[] { "Off", "On" });
		_godRaysOpt.ItemSelected += OnGodRaysChanged;

		_lensFlareOpt = AddDropdown(vbox, "Lens Flare", new[] { "Off", "On" });
		_lensFlareOpt.ItemSelected += OnLensFlareChanged;

		_dustMotesOpt = AddDropdown(vbox, "Dust Motes", new[] { "Off", "On" });
		_dustMotesOpt.ItemSelected += OnDustMotesChanged;

		AddSectionHeader(vbox, "POST-PROCESSING");

		_motionBlurOpt = AddDropdown(vbox, "Motion Blur", new[] { "Off", "On" });
		_motionBlurOpt.ItemSelected += OnMotionBlurChanged;

		_filmGrainOpt = AddDropdown(vbox, "Film Grain", new[] { "Off", "On" });
		_filmGrainOpt.ItemSelected += OnFilmGrainChanged;

		_vignetteOpt = AddDropdown(vbox, "Vignette", new[] { "Off", "On" });
		_vignetteOpt.ItemSelected += OnVignetteChanged;

		_chromaticAberrationOpt = AddDropdown(vbox, "Chromatic Aberration", new[] { "Off", "On" });
		_chromaticAberrationOpt.ItemSelected += OnChromaticAberrationChanged;

		_sharpeningOpt = AddDropdown(vbox, "Sharpening", new[] { "Off", "On" });
		_sharpeningOpt.ItemSelected += OnSharpeningChanged;

		_adsDofOpt = AddDropdown(vbox, "Depth of Field (ADS)", new[] { "Off", "On" });
		_adsDofOpt.ItemSelected += OnAdsDofChanged;

		_adsFovZoomOpt = AddDropdown(vbox, "ADS FOV Zoom", new[] { "Off", "On" });
		_adsFovZoomOpt.ItemSelected += OnAdsFovZoomChanged;

		_autoExposureOpt = AddDropdown(vbox, "Auto Exposure", new[] { "Off", "On" });
		_autoExposureOpt.ItemSelected += OnAutoExposureChanged;

		_teamGlowOpt = AddDropdown(vbox, "Team Glow (Outline)", new[] { "Off", "On" });
		_teamGlowOpt.ItemSelected += OnTeamGlowChanged;

		AddSectionHeader(vbox, "CAMERA EFFECTS");
		_viewBobOpt = AddDropdown(vbox, "View Bob (Walk Bob)", new[] { "Off", "On" });
		_viewBobOpt.ItemSelected += OnViewBobChanged;

		_sprintSwayOpt = AddDropdown(vbox, "Sprint Sway", new[] { "Off", "On" });
		_sprintSwayOpt.ItemSelected += OnSprintSwayChanged;

		_mouseInertiaOpt = AddDropdown(vbox, "Mouse Inertia (Weapon-Lag)", new[] { "Off", "On" });
		_mouseInertiaOpt.ItemSelected += OnMouseInertiaChanged;

		_directionLeanOpt = AddDropdown(vbox, "Direction Lean (Strafe-Tilt)", new[] { "Off", "On" });
		_directionLeanOpt.ItemSelected += OnDirectionLeanChanged;

		_cameraShakeOpt = AddDropdown(vbox, "Camera Shake (Firing)", new[] { "Off", "On" });
		_cameraShakeOpt.ItemSelected += OnCameraShakeChanged;

		AddSectionHeader(vbox, "DEBUG OVERLAYS");
		_showDebugBarOpt = AddDropdown(vbox, "Debug Bar (F3)", new[] { "Off", "On" });
		_showDebugBarOpt.ItemSelected += OnShowDebugBarChanged;

		_showNetGraphOpt = AddDropdown(vbox, "Net Graph (F4)", new[] { "Off", "On" });
		_showNetGraphOpt.ItemSelected += OnShowNetGraphChanged;

		_weaponLightOpt = AddDropdown(vbox, "Weapon Light (debug)", new[] { "Off", "On" });
		_weaponLightOpt.ItemSelected += OnWeaponLightChanged;

		return page;
	}

	/// <summary>Builds the Display tab.</summary>
	private Control BuildDisplayTab()
	{
		var (page, vbox) = NewTabPage("Display");

		_windowModeOpt = AddDropdown(
			vbox,
			"Window Mode",
			new[] { "Windowed", "Borderless Fullscreen", "Exclusive Fullscreen" }
		);
		_windowModeOpt.ItemSelected += OnWindowModeChanged;

		// Monitor dropdown, one entry per attached display; the resolution list rebuilds on monitor change.
		_monitorOpt = AddDropdown(vbox, "Monitor", BuildMonitorLabels());
		_monitorOpt.ItemSelected += OnMonitorChanged;

		string[] resStrings = new string[_resolutions.Length];
		for (int i = 0; i < _resolutions.Length; i++)
			resStrings[i] = FormatResolutionLabel(_resolutions[i]);
		_resolutionOpt = AddDropdown(vbox, "Resolution", resStrings);
		_resolutionOpt.ItemSelected += OnResolutionChanged;

		_vsyncOpt = AddDropdown(vbox, "VSync", new[] { "Disabled", "Enabled", "Adaptive", "Mailbox" });
		_vsyncOpt.ItemSelected += OnVSyncChanged;

		string[] fpsStrings = new string[_fpsCaps.Length];
		for (int i = 0; i < _fpsCaps.Length; i++)
			fpsStrings[i] = _fpsCaps[i] == 0 ? "Unlimited" : _fpsCaps[i].ToString();
		_fpsCapOpt = AddDropdown(vbox, "FPS Cap", fpsStrings);
		_fpsCapOpt.ItemSelected += OnFpsCapChanged;

		string[] menuCapStr = new string[MenuCaps.Length];
		for (int i = 0; i < MenuCaps.Length; i++)
			menuCapStr[i] = MenuCaps[i].ToString();
		_menuFpsCapOpt = AddDropdown(vbox, "Menu FPS Cap", menuCapStr);
		_menuFpsCapOpt.ItemSelected += OnMenuFpsCapChanged;

		(_brightnessSlider, _brightnessValue) = AddSlider(vbox, "Brightness", 0.6f, 1.4f, 0.05f);
		_brightnessSlider.ValueChanged += OnBrightnessChanged;

		(_uiScaleSlider, _uiScaleValue) = AddSlider(vbox, "UI Scale", 0.8f, 1.5f, 0.05f);
		_uiScaleSlider.ValueChanged += OnUiScaleChanged;

		AddSectionHeader(vbox, "HUD");
		(_hudMarginHSlider, _hudMarginHValue) = AddSlider(vbox, "HUD Margin Horizontal", 0f, 140f, 2f);
		_hudMarginHSlider.ValueChanged += OnHudMarginHChanged;
		(_hudMarginVSlider, _hudMarginVValue) = AddSlider(vbox, "HUD Margin Vertical", 0f, 140f, 2f);
		_hudMarginVSlider.ValueChanged += OnHudMarginVChanged;

		return page;
	}

	/// <summary>Builds the Controls tab (mouse sensitivity, FOV).</summary>
	private Control BuildControlsTab()
	{
		var (page, vbox) = NewTabPage("Controls");

		(_sensSlider, _sensValue) = AddSlider(vbox, "Mouse Sens", 0.01f, 10.0f, 0.01f);
		_sensSlider.ValueChanged += OnSensChanged;

		(_fovSlider, _fovValue) = AddSlider(vbox, "FOV", 80f, 100f, 1f);
		_fovSlider.ValueChanged += OnFovChanged;

		return page;
	}

	/// <summary>Adds a styled section header (separator + label) into the given VBox.</summary>
	private void AddSectionHeader(VBoxContainer parent, string text)
	{
		parent.AddChild(new HSeparator());
		var lbl = new Label { Text = text };
		lbl.AddThemeFontSizeOverride("font_size", 13);
		lbl.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 0.55f));
		parent.AddChild(lbl);
	}

	/// <summary>Adds a labelled dropdown (OptionButton) row and returns the button.</summary>
	private OptionButton AddDropdown(VBoxContainer parent, string label, string[] items)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 16);
		parent.AddChild(row);

		row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(170, 0) });

		var opt = new OptionButton { CustomMinimumSize = new Vector2(250, 0) };
		foreach (var it in items)
			opt.AddItem(it);
		row.AddChild(opt);
		return opt;
	}

	/// <summary>Adds a labelled horizontal slider row with a value label, returns both.</summary>
	private (HSlider, Label) AddSlider(VBoxContainer parent, string label, float min, float max, float step)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 16);
		parent.AddChild(row);

		row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(170, 0) });

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = step,
			CustomMinimumSize = new Vector2(190, 0),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
		};
		row.AddChild(slider);

		var val = new Label { Text = "", CustomMinimumSize = new Vector2(54, 0) };
		row.AddChild(val);
		return (slider, val);
	}

	/// <summary>Seeds every UI control with the current Settings values without firing change events.</summary>
	private void PullStateFromSettings()
	{
		_suppressEvents = true;

		_windowModeOpt.Selected = WindowModeToIndex(Settings.WindowMode);
		// Sync MonitorIndex with where the window actually is now (may have moved between sessions);
		// rebuild the resolution list if the active monitor changed.
		int actualMonitor = DisplayServer.WindowGetCurrentScreen();
		if (actualMonitor != Settings.MonitorIndex)
		{
			Settings.MonitorIndex = actualMonitor;
			BuildDynamicLists();
			_resolutionOpt.Clear();
			for (int i = 0; i < _resolutions.Length; i++)
				_resolutionOpt.AddItem(FormatResolutionLabel(_resolutions[i]));
		}
		_monitorOpt.Selected = Mathf.Clamp(Settings.MonitorIndex, 0, _monitorOpt.ItemCount - 1);
		_resolutionOpt.Selected = ResolutionToIndex(Settings.Resolution);
		_vsyncOpt.Selected = VSyncToIndex(Settings.VSync);
		_fpsCapOpt.Selected = FpsCapToIndex(Settings.FpsCap);

		int menuIdx = 1;
		for (int i = 0; i < MenuCaps.Length; i++)
			if (MenuCaps[i] == Settings.MenuFpsCap)
			{
				menuIdx = i;
				break;
			}
		_menuFpsCapOpt.Selected = menuIdx;

		_sensSlider.Value = Settings.MouseSensitivity;
		_sensValue.Text = Settings.MouseSensitivity.ToString("F2");
		_fovSlider.Value = Settings.Fov;
		_fovValue.Text = Settings.Fov.ToString("F0") + "°";
		_brightnessSlider.Value = Settings.Brightness;
		_brightnessValue.Text = Mathf.RoundToInt(Settings.Brightness * 100f) + "%";
		_hudMarginHSlider.Value = Settings.HudMarginH;
		_hudMarginHValue.Text = Mathf.RoundToInt(Settings.HudMarginH) + " px";
		_hudMarginVSlider.Value = Settings.HudMarginV;
		_hudMarginVValue.Text = Mathf.RoundToInt(Settings.HudMarginV) + " px";

		RefreshGraphicsControls();

		_suppressEvents = false;
	}

	/// <summary>Sets all graphics controls to the current Settings values (including preset).</summary>
	private void RefreshGraphicsControls()
	{
		bool prev = _suppressEvents;
		_suppressEvents = true;

		_presetOpt.Selected = (int)Settings.Preset;
		_renderScaleSlider.Value = Settings.RenderScale;
		_renderScaleValue.Text = Mathf.RoundToInt(Settings.RenderScale * 100f) + "%";
		_viewmodelScaleSlider.Value = Settings.ViewmodelRenderScale;
		_viewmodelScaleValue.Text = Mathf.RoundToInt(Settings.ViewmodelRenderScale * 100f) + "%";
		_uiScaleSlider.Value = Settings.UiScale;
		_uiScaleValue.Text = Mathf.RoundToInt(Settings.UiScale * 100f) + "%";
		_uiMsaaOpt.Selected = (int)Settings.UiMsaa;
		_aaOpt.Selected = (int)Settings.AntiAliasing;
		_upscalerOpt.Selected = (int)Settings.Upscaler;
		_shadowsOpt.Selected = (int)Settings.Shadows;
		_aoOpt.Selected = Settings.AmbientOcclusion ? 1 : 0;
		_reflectionsOpt.Selected = Settings.Reflections ? 1 : 0;
		_reflectionProbesOpt.Selected = (int)Settings.ReflectionProbes;
		_volumetricFogOpt.Selected = (int)Settings.VolumetricFog;
		_skyOpt.Selected = Settings.Sky ? 1 : 0;
		_cloudShadowsOpt.Selected = Settings.CloudShadows ? 1 : 0;
		_postProcessingOpt.Selected = Settings.PostProcessing ? 1 : 0;
		_cloudShadowDistOpt.Selected =
			Settings.CloudShadowDistance <= 90f ? 0
			: Settings.CloudShadowDistance <= 160f ? 1
			: 2;
		_godRaysOpt.Selected = Settings.GodRays ? 1 : 0;
		_lensFlareOpt.Selected = Settings.LensFlare ? 1 : 0;
		_dustMotesOpt.Selected = Settings.DustMotes ? 1 : 0;
		_motionBlurOpt.Selected = Settings.MotionBlur ? 1 : 0;
		_filmGrainOpt.Selected = Settings.FilmGrain ? 1 : 0;
		_vignetteOpt.Selected = Settings.Vignette ? 1 : 0;
		_sharpeningOpt.Selected = Settings.Sharpening ? 1 : 0;
		_chromaticAberrationOpt.Selected = Settings.ChromaticAberration ? 1 : 0;
		_adsDofOpt.Selected = Settings.AdsDepthOfField ? 1 : 0;
		_adsFovZoomOpt.Selected = Settings.AdsFovZoom ? 1 : 0;
		_autoExposureOpt.Selected = Settings.AutoExposure ? 1 : 0;
		_teamGlowOpt.Selected = Settings.TeamGlow ? 1 : 0;
		_viewBobOpt.Selected = Settings.ViewBob ? 1 : 0;
		_sprintSwayOpt.Selected = Settings.SprintSway ? 1 : 0;
		_mouseInertiaOpt.Selected = Settings.MouseInertia ? 1 : 0;
		_directionLeanOpt.Selected = Settings.DirectionLean ? 1 : 0;
		_cameraShakeOpt.Selected = Settings.CameraShake ? 1 : 0;
		_showDebugBarOpt.Selected = Settings.ShowDebugBar ? 1 : 0;
		_showNetGraphOpt.Selected = Settings.ShowNetGraph ? 1 : 0;
		_weaponLightOpt.Selected = Settings.WeaponLight ? 1 : 0;

		_suppressEvents = prev;
	}

	/// <summary>A single control was tweaked → preset switches to Custom.</summary>
	private void MarkCustomPreset()
	{
		Settings.Preset = QualityPreset.Custom;
		_presetOpt.Selected = (int)QualityPreset.Custom;
	}

	/// <summary>Preset dropdown changed: applies preset values, refreshes controls and re-applies settings.</summary>
	private void OnPresetChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.ApplyPreset((QualityPreset)(int)idx);
		RefreshGraphicsControls();
		Settings.Apply(GetTree());
	}

	/// <summary>Render-scale slider changed: updates value label and applies.</summary>
	private void OnRenderScaleChanged(double v)
	{
		_renderScaleValue.Text = Mathf.RoundToInt((float)v * 100f) + "%";
		if (_suppressEvents)
			return;
		Settings.RenderScale = (float)v;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Viewmodel-scale slider changed (weapon viewport scales independently from the world).</summary>
	private void OnViewmodelScaleChanged(double v)
	{
		_viewmodelScaleValue.Text = Mathf.RoundToInt((float)v * 100f) + "%";
		if (_suppressEvents)
			return;
		Settings.ViewmodelRenderScale = (float)v;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>UI-scale slider changed (drives Window.ContentScaleFactor).</summary>
	private void OnUiScaleChanged(double v)
	{
		_uiScaleValue.Text = Mathf.RoundToInt((float)v * 100f) + "%";
		if (_suppressEvents)
			return;
		Settings.UiScale = (float)v;
		Settings.Apply(GetTree());
	}

	/// <summary>UI MSAA dropdown changed (drives root Viewport.Msaa2D).</summary>
	private void OnUiMsaaChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.UiMsaa = (Viewport.Msaa)(int)idx;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Anti-aliasing mode changed.</summary>
	private void OnAaChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.AntiAliasing = (AntiAliasingMode)(int)idx;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Upscaler (Bilinear/FSR1/FSR2) changed.</summary>
	private void OnUpscalerChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.Upscaler = (UpscalingMode)(int)idx;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Volumetric fog quality changed. UI indices 0..3 map directly to enum Off/Low/Medium/High.</summary>
	private void OnVolumetricFogChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.VolumetricFog = (VolumetricFogQuality)(int)idx;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Shadow quality changed.</summary>
	private void OnShadowsChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.Shadows = (ShadowQuality)(int)idx;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Ambient occlusion toggle changed.</summary>
	private void OnAoChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.AmbientOcclusion = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Reflections toggle changed.</summary>
	private void OnReflectionsChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.Reflections = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Reflection-probe atlas quality changed; takes effect on next level reload.</summary>
	private void OnReflectionProbesChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.ReflectionProbes = (ReflectionProbeQuality)(int)idx;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Sky toggle changed.</summary>
	private void OnSkyChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.Sky = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Cloud shadows toggle changed.</summary>
	private void OnCloudShadowsChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.CloudShadows = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Post-processing master toggle changed — disables the whole <see cref="PostProcessEffect"/> dispatch when Off.</summary>
	private void OnPostProcessingChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.PostProcessing = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Cloud-shadow distance preset changed (Near/Medium/Far).</summary>
	private void OnCloudShadowDistChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.CloudShadowDistance =
			idx == 0 ? 60f
			: idx == 1 ? 120f
			: 200f;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>God rays toggle changed.</summary>
	private void OnGodRaysChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.GodRays = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Lens flare toggle changed.</summary>
	private void OnLensFlareChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.LensFlare = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Dust motes toggle changed.</summary>
	private void OnDustMotesChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.DustMotes = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Motion blur toggle changed.</summary>
	private void OnMotionBlurChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.MotionBlur = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Film grain toggle changed.</summary>
	private void OnFilmGrainChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.FilmGrain = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Vignette toggle changed.</summary>
	private void OnVignetteChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.Vignette = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Chromatic aberration toggle changed.</summary>
	private void OnChromaticAberrationChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.ChromaticAberration = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Sharpening toggle changed (also gated off under an FSR upscaler in Settings.Apply).</summary>
	private void OnSharpeningChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.Sharpening = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>ADS FOV zoom toggle changed.</summary>
	private void OnAdsFovZoomChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.AdsFovZoom = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Auto-exposure toggle changed.</summary>
	private void OnAutoExposureChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.AutoExposure = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>Team-glow toggle changed (GlowViewportSync polls Settings.TeamGlow per frame; no Apply needed).</summary>
	private void OnTeamGlowChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.TeamGlow = idx == 1;
		MarkCustomPreset();
	}

	/// <summary>ADS depth-of-field toggle changed.</summary>
	private void OnAdsDofChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.AdsDepthOfField = idx == 1;
		MarkCustomPreset();
		Settings.Apply(GetTree());
	}

	/// <summary>View bob toggle changed.</summary>
	private void OnViewBobChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.ViewBob = idx == 1;
		MarkCustomPreset();
	}

	/// <summary>Sprint sway toggle changed.</summary>
	private void OnSprintSwayChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.SprintSway = idx == 1;
		MarkCustomPreset();
	}

	/// <summary>Mouse inertia toggle changed.</summary>
	private void OnMouseInertiaChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.MouseInertia = idx == 1;
		MarkCustomPreset();
	}

	/// <summary>Direction lean toggle changed.</summary>
	private void OnDirectionLeanChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.DirectionLean = idx == 1;
		MarkCustomPreset();
	}

	/// <summary>Camera shake toggle changed.</summary>
	private void OnCameraShakeChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.CameraShake = idx == 1;
		MarkCustomPreset();
	}

	/// <summary>Debug bar visibility toggle changed.</summary>
	private void OnShowDebugBarChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.ShowDebugBar = idx == 1;
	}

	/// <summary>Net graph visibility toggle changed.</summary>
	private void OnShowNetGraphChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.ShowNetGraph = idx == 1;
	}

	private void OnWeaponLightChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.WeaponLight = idx == 1;
	}

	/// <summary>Window mode dropdown changed.</summary>
	private void OnWindowModeChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.WindowMode = IndexToWindowMode((int)idx);
		Settings.Apply(GetTree());
	}

	/// <summary>Resolution dropdown changed.</summary>
	private void OnResolutionChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.Resolution = _resolutions[(int)idx];
		Settings.Apply(GetTree());
	}

	/// <summary>Monitor dropdown changed: updates the active monitor, rebuilds the resolution list, picks the
	/// closest valid entry, and applies so the window jumps to the new screen.</summary>
	private void OnMonitorChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.MonitorIndex = (int)idx;
		BuildDynamicLists();
		_suppressEvents = true;
		_resolutionOpt.Clear();
		for (int i = 0; i < _resolutions.Length; i++)
			_resolutionOpt.AddItem(FormatResolutionLabel(_resolutions[i]));
		// Pick the closest match to the previous Resolution, falling back to native.
		int newIdx = ResolutionToIndex(Settings.Resolution);
		Settings.Resolution = _resolutions[newIdx];
		_resolutionOpt.Selected = newIdx;
		_suppressEvents = false;
		Settings.Apply(GetTree());
	}

	/// <summary>Builds Monitor-dropdown labels (Windows "Display N" + physical native + refresh + primary tag).
	/// Uses Win32/xrandr for physical pixels and primary detection because Godot's APIs mis-report under per-monitor DPI.</summary>
	private static string[] BuildMonitorLabels()
	{
		int n = DisplayServer.GetScreenCount();
		int godotPrimary = DisplayServer.GetPrimaryScreen();
		string[] labels = new string[n];
		for (int i = 0; i < n; i++)
		{
			Vector2I size = Win32Display.IsSupported
				? Win32Display.GetNativeResolution(i)
				: LinuxDisplay.IsSupported ? LinuxDisplay.GetNativeResolution(i) : Vector2I.Zero;
			if (size == Vector2I.Zero) size = DisplayServer.ScreenGetSize(i);
			int hz = (int)Mathf.Round(DisplayServer.ScreenGetRefreshRate(i));
			int displayNum = Win32Display.IsSupported ? Win32Display.GetWindowsDisplayNumber(i) : i + 1;
			bool isPrimary = Win32Display.IsSupported ? Win32Display.IsPrimaryMonitor(i) : i == godotPrimary;
			labels[i] = $"Display {displayNum} — {size.X}×{size.Y}@{hz}Hz" + (isPrimary ? " (primary)" : "");
		}
		return labels;
	}

	/// <summary>VSync mode dropdown changed.</summary>
	private void OnVSyncChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.VSync = IndexToVSync((int)idx);
		Settings.Apply(GetTree());
	}

	/// <summary>FPS cap dropdown changed; defers apply while menu is open (MenuFpsCap stays in effect).</summary>
	private void OnFpsCapChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.FpsCap = _fpsCaps[(int)idx];
		if (!_isOpen)
			Settings.Apply(GetTree());
	}

	/// <summary>Menu FPS cap dropdown changed; takes effect immediately if the menu is open.</summary>
	private void OnMenuFpsCapChanged(long idx)
	{
		if (_suppressEvents)
			return;
		Settings.MenuFpsCap = MenuCaps[(int)idx];
		if (_isOpen)
			Engine.MaxFps = Settings.MenuFpsCap;
	}

	/// <summary>Sensitivity slider changed.</summary>
	private void OnSensChanged(double v)
	{
		_sensValue.Text = v.ToString("F2");
		if (_suppressEvents)
			return;
		Settings.MouseSensitivity = (float)v;
		Settings.Apply(GetTree());
	}

	/// <summary>FOV slider changed.</summary>
	private void OnFovChanged(double v)
	{
		_fovValue.Text = v.ToString("F0") + "°";
		if (_suppressEvents)
			return;
		Settings.Fov = (float)v;
		Settings.Apply(GetTree());
	}

	/// <summary>Brightness slider changed.</summary>
	private void OnBrightnessChanged(double v)
	{
		_brightnessValue.Text = Mathf.RoundToInt((float)v * 100f) + "%";
		if (_suppressEvents)
			return;
		Settings.Brightness = (float)v;
		Settings.Apply(GetTree());
	}

	/// <summary>HUD horizontal margin slider changed (HudCs2 reads per frame → applies immediately).</summary>
	private void OnHudMarginHChanged(double v)
	{
		_hudMarginHValue.Text = Mathf.RoundToInt((float)v) + " px";
		if (_suppressEvents)
			return;
		Settings.HudMarginH = (float)v;
	}

	/// <summary>HUD vertical margin slider changed.</summary>
	private void OnHudMarginVChanged(double v)
	{
		_hudMarginVValue.Text = Mathf.RoundToInt((float)v) + " px";
		if (_suppressEvents)
			return;
		Settings.HudMarginV = (float)v;
	}

	/// <summary>Save button: persists settings and flashes a "Saved" indicator for 2s.</summary>
	private void OnSavePressed()
	{
		Settings.Save();
		_saveStatus.Text = "✓ Saved";
		GetTree().CreateTimer(2.0).Timeout += () =>
		{
			if (_saveStatus != null)
				_saveStatus.Text = "";
		};
	}

	/// <summary>Maps a Godot WindowMode to a dropdown index (Windowed/Fullscreen/Exclusive).</summary>
	private static int WindowModeToIndex(DisplayServer.WindowMode m) =>
		m switch
		{
			DisplayServer.WindowMode.Windowed => 0,
			DisplayServer.WindowMode.Fullscreen => 1,
			DisplayServer.WindowMode.ExclusiveFullscreen => 2,
			_ => 0,
		};

	/// <summary>Maps a dropdown index back to a Godot WindowMode.</summary>
	private static DisplayServer.WindowMode IndexToWindowMode(int i) =>
		i switch
		{
			0 => DisplayServer.WindowMode.Windowed,
			1 => DisplayServer.WindowMode.Fullscreen,
			2 => DisplayServer.WindowMode.ExclusiveFullscreen,
			_ => DisplayServer.WindowMode.Windowed,
		};

	/// <summary>Finds the index of the given resolution in the dynamic resolutions list.</summary>
	private int ResolutionToIndex(Vector2I res)
	{
		for (int i = 0; i < _resolutions.Length; i++)
			if (_resolutions[i] == res)
				return i;
		return _resolutions.Length - 1;
	}

	/// <summary>Maps a Godot VSyncMode to a dropdown index.</summary>
	private static int VSyncToIndex(DisplayServer.VSyncMode v) => (int)v;

	/// <summary>Maps a dropdown index back to a Godot VSyncMode.</summary>
	private static DisplayServer.VSyncMode IndexToVSync(int i) => (DisplayServer.VSyncMode)i;

	/// <summary>Finds the index of the given FPS cap in the dynamic FPS-cap list.</summary>
	private int FpsCapToIndex(int cap)
	{
		for (int i = 0; i < _fpsCaps.Length; i++)
			if (_fpsCaps[i] == cap)
				return i;
		return 0;
	}
}
