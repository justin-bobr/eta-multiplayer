using Godot;

namespace Vantix.UI;

/// <summary>Fullscreen overlay shown after a disconnect — displays the reason plus reconnect and quit buttons.
/// Code-driven UI; instantiated by <see cref="NetMain.HandleDisconnect"/>.</summary>
public partial class DisconnectScreen : Control
{
	private static readonly Color EtaRed = new(0.7529412f, 0.007843138f, 0.003921569f);
	public string Reason = "Connection lost";

	/// <summary>Builds the overlay UI, frees the mouse, and grabs focus.</summary>
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

		var title = new Label { Text = "DISCONNECTED", HorizontalAlignment = HorizontalAlignment.Center };
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

		var reconnectBtn = new Button { Text = "Reconnect", CustomMinimumSize = new Vector2(220, 40) };
		reconnectBtn.Pressed += OnReconnectPressed;
		var btnRow = new CenterContainer();
		btnRow.AddChild(reconnectBtn);
		col.AddChild(btnRow);

		var quitBtn = new Button { Text = "Quit", CustomMinimumSize = new Vector2(220, 32) };
		quitBtn.Pressed += () => GetTree().Quit();
		var quitRow = new CenterContainer();
		quitRow.AddChild(quitBtn);
		col.AddChild(quitRow);

		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	/// <summary>Forwards the reconnect press to NetMain.</summary>
	private void OnReconnectPressed()
	{
		NetMain.Instance?.RequestReconnect();
	}
}
