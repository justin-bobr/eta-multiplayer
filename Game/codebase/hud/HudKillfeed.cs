using Godot;

/// <summary>
/// CS2/CoD-Style Killfeed top-right. Eine Zeile pro Death-Event: "Attacker (Weapon) → Victim [HS]".
/// Headshot = small HS icon dahinter, gold-tint. Suicide / world-damage (attacker=0 oder ==victim)
/// als "Player Y ✕" ohne Attacker. Eigene Kills sind gelb hervorgehoben.
/// </summary>
public partial class HudKillfeed : Control
{
	private const int MaxEntries = 6;
	private const float HoldSec = 5.0f;
	private const float FadeSec = 1.5f;

	private VBoxContainer _list;

	public override void _Ready()
	{
		// Top-Right Anchor.
		AnchorLeft = 1f; AnchorRight = 1f;
		AnchorTop = 0f; AnchorBottom = 0f;
		OffsetLeft = -480f; OffsetRight = -16f;
		OffsetTop = 16f; OffsetBottom = 240f;
		MouseFilter = MouseFilterEnum.Ignore;

		_list = new VBoxContainer
		{
			AnchorLeft = 0f, AnchorRight = 1f,
			Alignment = BoxContainer.AlignmentMode.End,    // neue Einträge unten anhängen, ältere oben
			MouseFilter = MouseFilterEnum.Ignore,
		};
		AddChild(_list);

		var client = NetMain.Instance?.Client;
		if (client != null) client.OnDeath += OnDeath;
	}

	public override void _ExitTree()
	{
		var client = NetMain.Instance?.Client;
		if (client != null) client.OnDeath -= OnDeath;
	}

	private void OnDeath(byte victim, byte attacker, byte weaponId, bool isHeadshot)
	{
		var client = NetMain.Instance?.Client;
		string vName = NameOf(victim);
		bool selfKill = attacker == 0 || attacker == victim;
		string aName = selfKill ? "" : NameOf(attacker);
		string wName = WeaponName(weaponId);
		string hs = isHeadshot ? "  [HS]" : "";

		string text = selfKill
			? $"  ✕  {vName}"
			: $"{aName}  ({wName})  →  {vName}{hs}";

		var label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Right,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		// Color-Coding: eigene Kills/Tode gelb hervorgehoben, headshots gold, sonst weiß.
		Color col;
		bool ownKill = client != null && attacker == client.OwnNetId && !selfKill;
		bool ownDeath = client != null && victim == client.OwnNetId;
		if (ownDeath)        col = new Color(1f, 0.4f, 0.35f);     // dein Tod = rot
		else if (ownKill)    col = isHeadshot ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.95f, 0.45f);
		else if (isHeadshot) col = new Color(0.9f, 0.85f, 0.6f);
		else                 col = new Color(0.95f, 0.95f, 0.95f);
		label.AddThemeColorOverride("font_color", col);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		label.AddThemeConstantOverride("outline_size", 4);
		label.AddThemeFontSizeOverride("font_size", 16);
		label.SetMeta("kf_age", 0.0);

		_list.AddChild(label);
		while (_list.GetChildCount() > MaxEntries)
			_list.GetChild(0).QueueFree();
	}

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("HudKillfeed._Process");
		float dt = (float)delta;
		for (int i = _list.GetChildCount() - 1; i >= 0; i--)
		{
			if (_list.GetChild(i) is not Label lbl) continue;
			double age = lbl.GetMeta("kf_age").AsDouble() + dt;
			lbl.SetMeta("kf_age", age);
			float alpha = age <= HoldSec ? 1f : Mathf.Clamp(1f - (float)(age - HoldSec) / FadeSec, 0f, 1f);
			if (alpha <= 0.001f) { lbl.QueueFree(); continue; }
			Color m = lbl.Modulate; m.A = alpha; lbl.Modulate = m;
		}
	}

	private static string NameOf(byte netId)
	{
		var client = NetMain.Instance?.Client;
		if (client == null) return $"Player {netId}";
		if (client.OwnNetId == netId) return "YOU";
		if (client.RemotePlayers.TryGetValue(netId, out var p) && !string.IsNullOrEmpty(p.PlayerName))
			return p.PlayerName;
		return $"Player {netId}";
	}

	/// <summary>WeaponId-Registry für v1: nur M4A1. Sobald multiple Weapons → echte Registry.</summary>
	private static string WeaponName(byte weaponId) => weaponId switch
	{
		_ => "M4A1",
	};
}
