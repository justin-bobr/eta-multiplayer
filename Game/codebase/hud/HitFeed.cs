using Godot;

/// <summary>
/// HUD hit feed shown at the top center. Lists server-confirmed hits where the local player is
/// either shooter or victim. Format: "Shooter -> (PART) -> Victim (-DMG -> HP)". The server sends
/// the event (<see cref="Packets.WriteHit"/>) only to shooter and victim; other peers see nothing.
/// Auto-attached via NetMain.
/// </summary>
public partial class HitFeed : Control
{
	private const int MaxEntries = 8;
	private const float HoldSec = 4.0f;
	private const float FadeSec = 1.5f;

	private VBoxContainer _list;

	/// <summary>Builds the centered top strip and subscribes to the client's hit event.</summary>
	public override void _Ready()
	{
		AnchorLeft = 0f; AnchorRight = 1f;
		AnchorTop = 0f; AnchorBottom = 0f;
		OffsetTop = 60f;
		MouseFilter = MouseFilterEnum.Ignore;

		_list = new VBoxContainer
		{
			AnchorLeft = 0f, AnchorRight = 1f,
			Alignment = BoxContainer.AlignmentMode.Center,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		AddChild(_list);

		var client = NetMain.Instance?.Client;
		if (client != null) client.OnHit += OnHit;
	}

	/// <summary>Unsubscribes from the client hit event when leaving the scene tree.</summary>
	public override void _ExitTree()
	{
		var client = NetMain.Instance?.Client;
		if (client != null) client.OnHit -= OnHit;
	}

	/// <summary>Adds a new hit row at the top of the feed and trims older rows beyond the cap.</summary>
	private void OnHit(byte shooterNetId, byte victimNetId, HitboxGroup group, byte damage, byte hpLeft)
	{
		var label = new Label
		{
			Text = FormatLine(shooterNetId, victimNetId, group, damage, hpLeft),
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		Color baseColor = group == HitboxGroup.Head ? new Color(1f, 0.85f, 0.30f) : new Color(0.95f, 0.95f, 0.95f);
		label.AddThemeColorOverride("font_color", baseColor);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		label.AddThemeConstantOverride("outline_size", 5);
		label.AddThemeFontSizeOverride("font_size", 20);
		label.SetMeta("hit_age", 0.0);

		_list.AddChild(label);
		_list.MoveChild(label, 0);

		while (_list.GetChildCount() > MaxEntries)
			_list.GetChild(_list.GetChildCount() - 1).QueueFree();
	}

	/// <summary>Formats a single hit row including the kill marker when HP drops to zero.</summary>
	private string FormatLine(byte shooter, byte victim, HitboxGroup group, byte damage, byte hpLeft)
	{
		string s = NameOf(shooter);
		string v = NameOf(victim);
		string p = group.ToString().ToUpperInvariant();
		string killed = hpLeft == 0 ? "  💀" : "";
		return $"{s}  →  ({p})  →  {v}   (−{damage} → {hpLeft} HP){killed}";
	}

	/// <summary>Returns the display name for a net id; falls back to "Player {netId}" when no cached name is known.</summary>
	private static string NameOf(byte netId)
	{
		if (NetMain.Instance?.Client?.OwnNetId == netId) return "YOU";
		return $"Player {netId}";
	}

	/// <summary>Ages each entry, fades it out after the hold window, and removes fully transparent rows.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("HitFeed._Process");
		float dt = (float)delta;
		for (int i = _list.GetChildCount() - 1; i >= 0; i--)
		{
			if (_list.GetChild(i) is not Label lbl) continue;
			double age = lbl.GetMeta("hit_age").AsDouble() + dt;
			lbl.SetMeta("hit_age", age);

			float alpha = 1f;
			if (age > HoldSec)
				alpha = Mathf.Clamp(1f - (float)(age - HoldSec) / FadeSec, 0f, 1f);
			if (alpha <= 0.001f)
			{
				lbl.QueueFree();
				continue;
			}
			Color m = lbl.Modulate;
			m.A = alpha;
			lbl.Modulate = m;
		}
	}
}
