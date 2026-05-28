using Godot;

/// <summary>
/// Fullscreen overlay shown after a disconnect (server shutdown / timeout / kick). Displays the reason
/// plus a reconnect and quit button. Code-driven UI; no .tscn required. Instantiated by
/// <see cref="NetMain.HandleDisconnect"/> and added to the root.
/// </summary>
public partial class DisconnectScreen : Control
{
	private static readonly Color EtaRed = new(0.7529412f, 0.007843138f, 0.003921569f);
	public string Reason = "Verbindung verloren";

	/// <summary>Builds the overlay UI, frees the mouse, and grabs focus so the user can interact with the buttons.</summary>
	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		GrabFocus();

		var bg = new ColorRect { Color = EtaRed };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var col = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
		col.AddThemeConstantOverride("separation", 18);
		center.AddChild(col);

		var title = new Label
		{
			Text = "VERBINDUNG VERLOREN",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeColorOverride("font_color", Colors.White);
		title.AddThemeFontSizeOverride("font_size", 32);
		col.AddChild(title);

		var reasonLabel = new Label
		{
			Text = Reason,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(420, 0),
		};
		reasonLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
		reasonLabel.AddThemeFontSizeOverride("font_size", 14);
		col.AddChild(reasonLabel);

		col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

		var reconnectBtn = new Button { Text = "Erneut verbinden", CustomMinimumSize = new Vector2(220, 40) };
		reconnectBtn.Pressed += OnReconnectPressed;
		var btnRow = new CenterContainer();
		btnRow.AddChild(reconnectBtn);
		col.AddChild(btnRow);

		var quitBtn = new Button { Text = "Beenden", CustomMinimumSize = new Vector2(220, 32) };
		quitBtn.Pressed += () => GetTree().Quit();
		var quitRow = new CenterContainer();
		quitRow.AddChild(quitBtn);
		col.AddChild(quitRow);

		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	/// <summary>Forwards the reconnect button press to NetMain, which restarts the network client and switches scenes.</summary>
	private void OnReconnectPressed()
	{
		NetMain.Instance?.RequestReconnect();
	}
}
