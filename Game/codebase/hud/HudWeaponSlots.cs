using Godot;

/// <summary>
/// Bottom-right loadout strip (no background): weapon silhouette, ammo, and two equipment slots.
/// Active slot is brighter with a red accent underscore. Rendered via <see cref="_Draw"/>.
/// </summary>
public partial class HudWeaponSlots : Control
{
	/// <summary>0 = primary weapon, 1 = equipment slot 1, 2 = equipment slot 2.</summary>
	public int ActiveSlot;
	public int AmmoCurrent = 30;
	/// <summary>Reserve ammo; -1 renders as the infinity glyph.</summary>
	public int AmmoReserve = 90;
	/// <summary>Smoke count for equipment slot 1; -1 renders as the infinity glyph.</summary>
	public int SmokeCount = -1;
	public int Equip2Count = 1;
	/// <summary>Grenade throw charge in the 0..1 range.</summary>
	public float GrenadeCharge;
	public string WeaponName = "";

	public const float IconW = 80f;
	public const float AmmoW = 92f;
	public const float EquipW = 50f;
	public const float Gap = 12f;
	public const float WeaponAmmoGap = -4f;   // weapon + ammo sit closer together
	public const float EquipGap = 0f;          // the two equipment slots sit closer together
	public const float StripH = 60f;
	public const float StripW = IconW + WeaponAmmoGap + AmmoW + Gap + EquipW + EquipGap + EquipW;

	private static readonly Color Shadow = new(0f, 0f, 0f, 0.7f);
	private static readonly Color Accent = new("c00201", 0.95f);
	private static readonly Color Alarm = new(1f, 0.42f, 0.32f, 0.97f);

	/// <summary>Renders the four cells (weapon, ammo, equipment 1, equipment 2) left to right.</summary>
	public override void _Draw()
	{
		if (Size.X <= 1f) return;
		Font font = GetThemeDefaultFont();

		float x = 0f;
		DrawWeaponCell(font, x, IconW, "1", isRifle: true, active: ActiveSlot == 0, count: 0);
		x += IconW + WeaponAmmoGap;
		DrawAmmo(font, x);
		x += AmmoW + Gap;
		DrawSeparator(x - Gap * 0.5f);
		DrawWeaponCell(font, x, EquipW, "2", isRifle: false, active: ActiveSlot == 1, count: SmokeCount);
		x += EquipW + EquipGap;
		DrawWeaponCell(font, x, EquipW, "3", isRifle: false, active: ActiveSlot == 2, count: Equip2Count);
	}

	/// <summary>Faint vertical divider separating the weapon + ammo block from the equipment slots.</summary>
	private void DrawSeparator(float x)
	{
		DrawRect(new Rect2(x - 1f, 10f, 2f, StripH - 20f), new Color(1f, 1f, 1f, 0.16f));
	}

	/// <summary>Draws a single weapon or equipment cell, including the active-slot accent and optional charge bar.</summary>
	private void DrawWeaponCell(Font font, float x, float cellW, string key, bool isRifle, bool active, int count)
	{
		Color icon = new(1f, 1f, 1f, active ? 0.80f : 0.28f);
		Color txt = new(1f, 1f, 1f, active ? 0.92f : 0.45f);
		float cx = x + cellW * 0.5f;

		if (isRifle) DrawIcon(RifleTex, cx, 24f, IconW + 8f, 44f, icon);
		else DrawIcon(SmokeTex, cx, 26f, 20f, 32f, icon);

		DrawRightAligned(font, key, x + cellW - 3f, 13f, 14, txt);

		if (!isRifle)
		{
			string c = count < 0 ? "x∞" : "x" + count;
			DrawCentered(font, c, new Vector2(cx, 46f), 12, txt);
		}

		if (active)
			DrawRect(new Rect2(x + 8f, StripH - 3f, cellW - 16f, 2f), Accent);

		if (!isRifle && active && GrenadeCharge > 0.001f)
		{
			DrawRect(new Rect2(x + 6f, StripH - 3f, cellW - 12f, 2f), new Color(0f, 0f, 0f, 0.5f));
			DrawRect(new Rect2(x + 6f, StripH - 3f, (cellW - 12f) * Mathf.Clamp(GrenadeCharge, 0f, 1f), 2f), Accent);
		}
	}

	/// <summary>Draws the central ammo block: weapon name caption, current ammo (large), and reserve ammo (small).</summary>
	private void DrawAmmo(Font font, float x)
	{
		float cx = x + AmmoW * 0.5f;

		string reserve = AmmoReserve < 0 ? "∞" : AmmoReserve.ToString();
		DrawCentered(font, reserve, new Vector2(cx, 12f), 11, new Color(1f, 1f, 1f, 0.5f));

		bool low = AmmoCurrent <= 5;
		DrawCentered(font, AmmoCurrent.ToString(), new Vector2(cx, 37f), 30,
			low ? Alarm : new Color(1f, 1f, 1f, 0.96f));
	}

	private Texture2D _rifleTex, _smokeTex;
	private Texture2D RifleTex => _rifleTex ??= GD.Load<Texture2D>("res://ui/hud/ar15.png");
	private Texture2D SmokeTex => _smokeTex ??= GD.Load<Texture2D>("res://ui/hud/smoke.png");

	/// <summary>Draws a HUD icon texture fitted (aspect-preserving) into maxW × maxH, centered at (cx, cy), with
	/// a soft drop shadow. <paramref name="mod"/> modulates color + alpha.</summary>
	private void DrawIcon(Texture2D tex, float cx, float cy, float maxW, float maxH, Color mod)
	{
		if (tex == null)
			return;
		float scale = Mathf.Min(maxW / tex.GetWidth(), maxH / tex.GetHeight());
		float w = tex.GetWidth() * scale, h = tex.GetHeight() * scale;
		var rect = new Rect2(cx - w * 0.5f, cy - h * 0.5f, w, h);
		DrawTextureRect(tex, new Rect2(rect.Position + Vector2.One, rect.Size), false, new Color(0f, 0f, 0f, mod.A * 0.6f));
		DrawTextureRect(tex, rect, false, mod);
	}

	/// <summary>Draws horizontally centered text with a soft shadow, no background.</summary>
	private void DrawCentered(Font font, string text, Vector2 center, int fs, Color col)
	{
		if (font == null) return;
		Vector2 sz = font.GetStringSize(text, HorizontalAlignment.Left, -1, fs);
		var pos = new Vector2(center.X - sz.X * 0.5f, center.Y + fs * 0.36f);
		DrawString(font, pos + Vector2.One, text, HorizontalAlignment.Left, -1, fs, Shadow);
		DrawString(font, pos, text, HorizontalAlignment.Left, -1, fs, col);
	}

	/// <summary>Draws right-aligned text with a soft shadow, no background.</summary>
	private void DrawRightAligned(Font font, string text, float rightX, float baselineY, int fs, Color col)
	{
		if (font == null) return;
		Vector2 sz = font.GetStringSize(text, HorizontalAlignment.Left, -1, fs);
		var pos = new Vector2(rightX - sz.X, baselineY);
		DrawString(font, pos + Vector2.One, text, HorizontalAlignment.Left, -1, fs, Shadow);
		DrawString(font, pos, text, HorizontalAlignment.Left, -1, fs, col);
	}
}
