using Godot;

/// <summary>
/// Kurzer Hitmarker-Effekt: zeigt "-X HP" rechts mittig wenn der LocalPlayer einen Treffer landet
/// (CoD-Style). Headshot = gold, body = white. Fadet nach <see cref="HoldSec"/> Sekunden raus.
/// Stacked: mehrere Hits hintereinander erzeugen mehrere Labels (von alt nach neu nach unten).
///
/// Server-Authoritativ: subscribed an <see cref="NetClient.OnHit"/>. Server schickt das Event NUR
/// an Shooter + Victim, also nur eigene Treffer triggern hier.
/// </summary>
public partial class HudHitmarker : Control
{
	private const int MaxEntries = 6;
	private const float HoldSec = 1.2f;
	private const float FadeSec = 0.6f;

	private VBoxContainer _list;

	public override void _Ready()
	{
		// Center-mittig leicht rechts/oben zum Crosshair — wie CoD MW: "−100 HEADSHOT" gross über
		// dem Crosshair-Bereich, fast wie ein floating tag, fadet schnell raus.
		AnchorLeft = 0.5f; AnchorRight = 0.5f;
		AnchorTop = 0.5f; AnchorBottom = 0.5f;
		OffsetLeft = 30f; OffsetRight = 280f;
		OffsetTop = -110f; OffsetBottom = 30f;
		MouseFilter = MouseFilterEnum.Ignore;

		_list = new VBoxContainer
		{
			AnchorLeft = 0f, AnchorRight = 1f,
			AnchorTop = 0f, AnchorBottom = 1f,
			Alignment = BoxContainer.AlignmentMode.End,    // bottom-up stacking — neueste Treffer unten
			MouseFilter = MouseFilterEnum.Ignore,
		};
		AddChild(_list);

		var client = NetMain.Instance?.Client;
		if (client != null) client.OnHit += OnHit;
	}

	public override void _ExitTree()
	{
		var client = NetMain.Instance?.Client;
		if (client != null) client.OnHit -= OnHit;
	}

	private void OnHit(byte shooterNetId, byte victimNetId, HitboxGroup group, byte damage, byte hpLeft)
	{
		var client = NetMain.Instance?.Client;
		// Nur wenn DU der Schütze bist — Server schickt das Event zwar nur an Schütze + Opfer, aber
		// zur Sicherheit nochmal filtern. Opfer-Side wäre eher Hit-Direction-Indicator als Hitmarker.
		if (client == null || shooterNetId != client.OwnNetId) return;

		bool headshot = group == HitboxGroup.Head;
		bool kill = hpLeft == 0;
		// CoD-Style: kompakt, nur "−DMG" gross, "HEADSHOT" oder "KILL" als kleines Suffix.
		string text = kill ? $"−{damage}  KILL" : (headshot ? $"−{damage}  HEADSHOT" : $"−{damage}");

		var label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Left,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		// Farb-Coding: normal weiss, headshot gold, kill orange-red.
		Color col;
		if (kill)          col = new Color(1f, 0.55f, 0.15f);
		else if (headshot) col = new Color(1f, 0.85f, 0.25f);
		else               col = new Color(1f, 1f, 1f);
		label.AddThemeColorOverride("font_color", col);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
		label.AddThemeConstantOverride("outline_size", 6);
		label.AddThemeFontSizeOverride("font_size", kill ? 30 : (headshot ? 26 : 22));
		label.SetMeta("hit_age", 0.0);

		_list.AddChild(label);
		while (_list.GetChildCount() > MaxEntries)
			_list.GetChild(0).QueueFree();
	}

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("HudHitmarker._Process");
		float dt = (float)delta;
		for (int i = _list.GetChildCount() - 1; i >= 0; i--)
		{
			if (_list.GetChild(i) is not Label lbl) continue;
			double age = lbl.GetMeta("hit_age").AsDouble() + dt;
			lbl.SetMeta("hit_age", age);
			float alpha = age <= HoldSec ? 1f : Mathf.Clamp(1f - (float)(age - HoldSec) / FadeSec, 0f, 1f);
			if (alpha <= 0.001f) { lbl.QueueFree(); continue; }
			Color m = lbl.Modulate; m.A = alpha; lbl.Modulate = m;
		}
	}
}
