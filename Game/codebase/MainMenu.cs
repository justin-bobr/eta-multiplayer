using Godot;

/// <summary>
/// Main menu shown on client startup. Lets the player enter a server address, tune settings,
/// or quit. Bypassed when <see cref="NetCli.AutoConnect"/> is set or the run mode is Listen / Server —
/// in those cases the menu immediately swaps to the loading scene.
/// </summary>
public partial class MainMenu : Control
{
	private const string LoadingScene = "res://loading.tscn";
	private const string DefaultAddress = "127.0.0.1:27015";

	private static readonly Color EtaRed = new(0.7529412f, 0.007843138f, 0.003921569f);
	private static readonly Color CardBg = new(0f, 0f, 0f, 0.55f);
	private static readonly Color CardBorder = new(1f, 1f, 1f, 0.18f);
	private static readonly Color TextPrimary = new(1f, 1f, 1f, 0.95f);
	private static readonly Color TextMuted = new(1f, 1f, 1f, 0.65f);
	private static readonly Color ErrorRed = new(1f, 0.45f, 0.45f, 1f);

	private LineEdit _addressInput;
	private LineEdit _nameInput;
	private Label _errorLabel;
	private SettingsMenu _settingsMenu;

	/// <summary>Decides whether to show the menu or immediately advance to the loading scene
	/// based on the parsed CLI mode and the auto-connect flag.</summary>
	public override void _Ready()
	{
		var cli = NetMain.Instance?.Cli;
		if (
			cli == null
			|| cli.Mode == NetMode.Listen
			|| cli.Mode == NetMode.Server
			|| (cli.Mode == NetMode.Client && cli.AutoConnect)
		)
		{
			CallDeferred(nameof(SwapToLoading));
			return;
		}

		Input.MouseMode = Input.MouseModeEnum.Visible;
		Engine.MaxFps = Settings.MenuFpsCap;
		BuildUi();
		AttachSettingsMenu();
	}

	/// <summary>Switches to the loading scene that drives the connect / world-load flow.</summary>
	private void SwapToLoading()
	{
		GetTree().ChangeSceneToFile(LoadingScene);
	}

	/// <summary>Builds the red-themed menu UI (background, title, card with inputs, action buttons, footer).</summary>
	private void BuildUi()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		var bg = new ColorRect { Color = EtaRed, MouseFilter = MouseFilterEnum.Ignore };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var col = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
		col.AddThemeConstantOverride("separation", 18);
		center.AddChild(col);

		BuildTitle(col);
		BuildCard(col);
		BuildActions(col);
		BuildFooter(col);
	}

	/// <summary>Adds the centered "ETA" title and tagline at the top of the column.</summary>
	private static void BuildTitle(VBoxContainer col)
	{
		var title = new Label { Text = "ETA", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 64);
		title.AddThemeColorOverride("font_color", TextPrimary);
		col.AddChild(title);

		var tagline = new Label { Text = "Tactical 5v5 — pre-alpha", HorizontalAlignment = HorizontalAlignment.Center };
		tagline.AddThemeFontSizeOverride("font_size", 13);
		tagline.AddThemeColorOverride("font_color", TextMuted);
		col.AddChild(tagline);
	}

	/// <summary>Builds the dark card containing the address and name input fields.</summary>
	private void BuildCard(VBoxContainer col)
	{
		var card = new PanelContainer();
		var style = new StyleBoxFlat
		{
			BgColor = CardBg,
			BorderColor = CardBorder,
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
		};
		style.ContentMarginLeft = 22f;
		style.ContentMarginRight = 22f;
		style.ContentMarginTop = 18f;
		style.ContentMarginBottom = 18f;
		card.AddThemeStyleboxOverride("panel", style);
		col.AddChild(card);

		var inner = new VBoxContainer();
		inner.AddThemeConstantOverride("separation", 10);
		card.AddChild(inner);

		AddLabel(inner, "SERVER", 12, TextMuted);
		_addressInput = new LineEdit
		{
			PlaceholderText = DefaultAddress,
			Text = DefaultAddress,
			CustomMinimumSize = new Vector2(0, 32),
		};
		_addressInput.TextSubmitted += _ => OnConnectPressed();
		inner.AddChild(_addressInput);

		inner.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

		AddLabel(inner, "PLAYER NAME", 12, TextMuted);
		string savedName = NetMain.Instance?.Cli?.PlayerName ?? "Player";
		_nameInput = new LineEdit
		{
			PlaceholderText = "Player",
			Text = savedName,
			CustomMinimumSize = new Vector2(0, 32),
		};
		_nameInput.TextSubmitted += _ => OnConnectPressed();
		inner.AddChild(_nameInput);

		_errorLabel = new Label
		{
			Text = "",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		_errorLabel.AddThemeFontSizeOverride("font_size", 12);
		_errorLabel.AddThemeColorOverride("font_color", ErrorRed);
		_errorLabel.Visible = false;
		inner.AddChild(_errorLabel);
	}

	/// <summary>Builds the Connect / Settings / Quit button row.</summary>
	private void BuildActions(VBoxContainer col)
	{
		var connectBtn = new Button { Text = "CONNECT", CustomMinimumSize = new Vector2(0, 44) };
		connectBtn.AddThemeFontSizeOverride("font_size", 16);
		connectBtn.Pressed += OnConnectPressed;
		col.AddChild(connectBtn);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		col.AddChild(row);

		var settingsBtn = new Button { Text = "Settings", CustomMinimumSize = new Vector2(0, 36) };
		settingsBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		settingsBtn.Pressed += OnSettingsPressed;
		row.AddChild(settingsBtn);

		var quitBtn = new Button { Text = "Quit", CustomMinimumSize = new Vector2(0, 36) };
		quitBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		quitBtn.Pressed += () => GetTree().Quit();
		row.AddChild(quitBtn);
	}

	/// <summary>Builds the version / build label below the action buttons.</summary>
	private static void BuildFooter(VBoxContainer col)
	{
		string version = (string)ProjectSettings.GetSetting("application/config/version", "0.0.1");
		var footer = new Label
		{
			Text = $"v{version} — open source · in development",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		footer.AddThemeFontSizeOverride("font_size", 11);
		footer.AddThemeColorOverride("font_color", TextMuted);
		col.AddChild(footer);
	}

	/// <summary>Helper that appends a small caps-style label above an input.</summary>
	private static void AddLabel(VBoxContainer parent, string text, int size, Color color)
	{
		var lbl = new Label { Text = text };
		lbl.AddThemeFontSizeOverride("font_size", size);
		lbl.AddThemeColorOverride("font_color", color);
		parent.AddChild(lbl);
	}

	/// <summary>Spawns the shared SettingsMenu node so the Settings button can open the same overlay used in-game.
	/// Restores menu-mode mouse + FPS cap afterwards because SettingsMenu._Ready calls SetOpen(false)
	/// which assumes the game is running and would capture the cursor.</summary>
	private void AttachSettingsMenu()
	{
		_settingsMenu = new SettingsMenu { Name = "SettingsMenu" };
		AddChild(_settingsMenu);
		Input.MouseMode = Input.MouseModeEnum.Visible;
		Engine.MaxFps = Settings.MenuFpsCap;
	}

	/// <summary>Validates the input fields and hands the address to NetMain to start the connect flow.</summary>
	private void OnConnectPressed()
	{
		string raw = _addressInput.Text.Trim();
		if (string.IsNullOrEmpty(raw))
			raw = DefaultAddress;
		if (!TryParseAddress(raw, out string host, out int port))
		{
			ShowError($"Invalid address: \"{raw}\". Expected HOST or HOST:PORT (e.g. 127.0.0.1:27015).");
			return;
		}

		string name = _nameInput.Text.Trim();
		if (!string.IsNullOrEmpty(name) && NetMain.Instance?.Cli != null)
			NetMain.Instance.Cli.PlayerName = name;

		NetMain.Instance?.ConnectToServer(host, port);
	}

	/// <summary>Opens the embedded settings menu.</summary>
	private void OnSettingsPressed()
	{
		_settingsMenu?.Open();
	}

	/// <summary>Shows an inline validation error under the input fields.</summary>
	private void ShowError(string message)
	{
		_errorLabel.Text = message;
		_errorLabel.Visible = true;
	}

	/// <summary>Parses "HOST" or "HOST:PORT" into a host and port pair. Falls back to the default port (27015) when no port is supplied.</summary>
	private static bool TryParseAddress(string text, out string host, out int port)
	{
		host = "";
		port = 27015;
		int colon = text.LastIndexOf(':');
		if (colon < 0)
		{
			if (text.Length == 0)
				return false;
			host = text;
			return true;
		}
		host = text.Substring(0, colon).Trim();
		string portText = text.Substring(colon + 1).Trim();
		if (host.Length == 0)
			return false;
		if (!int.TryParse(portText, out port))
			return false;
		if (port <= 0 || port > 65535)
			return false;
		return true;
	}
}
