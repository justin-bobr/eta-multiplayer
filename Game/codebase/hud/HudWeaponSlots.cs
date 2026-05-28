using Godot;

/// <summary>
/// Bottom-right loadout strip: weapon silhouette, ammo, and two equipment slots side by side,
/// without a background. Key number is shown compactly above the icon; the active slot is brighter
/// and gets a red accent underscore. Icons are drawn as silhouettes via <see cref="_Draw"/>.
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
	public const float AmmoW = 98f;
	public const float Gap = 12f;
	public const float StripH = 60f;
	public const float StripW = IconW * 3f + AmmoW + Gap * 3f;

	private static readonly Color Shadow = new(0f, 0f, 0f, 0.7f);
	private static readonly Color Accent = new("c00201", 0.95f);
	private static readonly Color Alarm = new(1f, 0.42f, 0.32f, 0.97f);

	/// <summary>Renders the four cells (weapon, ammo, equipment 1, equipment 2) left to right.</summary>
	public override void _Draw()
	{
		if (Size.X <= 1f) return;
		Font font = GetThemeDefaultFont();

		float x = 0f;
		DrawWeaponCell(font, x, "1", isRifle: true, active: ActiveSlot == 0, count: 0);
		x += IconW + Gap;
		DrawAmmo(font, x);
		x += AmmoW + Gap;
		DrawWeaponCell(font, x, "2", isRifle: false, active: ActiveSlot == 1, count: SmokeCount);
		x += IconW + Gap;
		DrawWeaponCell(font, x, "3", isRifle: false, active: ActiveSlot == 2, count: Equip2Count);
	}

	/// <summary>Draws a single weapon or equipment cell, including the active-slot accent and optional charge bar.</summary>
	private void DrawWeaponCell(Font font, float x, string key, bool isRifle, bool active, int count)
	{
		Color icon = new(1f, 1f, 1f, active ? 0.97f : 0.40f);
		Color txt = new(1f, 1f, 1f, active ? 0.97f : 0.48f);
		float cx = x + IconW * 0.5f;

		if (isRifle) DrawRifle(cx, 24f, icon);
		else DrawGrenade(cx, 24f, icon);

		DrawRightAligned(font, key, x + IconW - 3f, 13f, 14, txt);

		if (!isRifle)
		{
			string c = count < 0 ? "x∞" : "x" + count;
			DrawCentered(font, c, new Vector2(cx, 46f), 12, txt);
		}

		if (active)
			DrawRect(new Rect2(x + 10f, StripH - 3f, IconW - 20f, 2f), Accent);

		if (!isRifle && active && GrenadeCharge > 0.001f)
		{
			DrawRect(new Rect2(x + 8f, StripH - 3f, IconW - 16f, 2f), new Color(0f, 0f, 0f, 0.5f));
			DrawRect(new Rect2(x + 8f, StripH - 3f, (IconW - 16f) * Mathf.Clamp(GrenadeCharge, 0f, 1f), 2f), Accent);
		}
	}

	/// <summary>Draws the central ammo block: weapon name caption, current ammo (large), and reserve ammo (small).</summary>
	private void DrawAmmo(Font font, float x)
	{
		float cx = x + AmmoW * 0.5f;

		if (WeaponName.Length > 0)
			DrawCentered(font, WeaponName.ToUpper(), new Vector2(cx, 9f), 10, new Color(1f, 1f, 1f, 0.5f));

		bool low = AmmoCurrent <= 5;
		DrawCentered(font, AmmoCurrent.ToString(), new Vector2(cx, 31f), 30,
			low ? Alarm : new Color(1f, 1f, 1f, 0.96f));

		string reserve = AmmoReserve < 0 ? "∞" : AmmoReserve.ToString();
		DrawCentered(font, reserve, new Vector2(cx, 49f), 15, new Color(1f, 1f, 1f, 0.5f));
	}

	/// <summary>Draws a rifle silhouette with the barrel pointing left. (cx, cy) is the icon center.</summary>
	private void DrawRifle(float cx, float cy, Color c)
	{
		float left = cx - 39f;
		float top = cy - 4f;
		void R(float x, float y, float ww, float hh) =>
			DrawRect(new Rect2(left + x, top + y, ww, hh), c);

		R(0f, -3f, 22f, 5f);
		R(20f, -6f, 40f, 11f);
		R(58f, -9f, 18f, 18f);
		R(34f, 4f, 11f, 17f);
		R(47f, 4f, 8f, 12f);
		R(40f, -12f, 6f, 5f);
	}

	/// <summary>Draws a grenade silhouette. (cx, cy) is the icon center.</summary>
	private void DrawGrenade(float cx, float cy, Color c)
	{
		float bodyCx = cx - 4f;
		DrawCircle(new Vector2(bodyCx, cy + 5f), 13f, c);
		DrawRect(new Rect2(bodyCx - 6f, cy - 13f, 12f, 8f), c);
		DrawRect(new Rect2(bodyCx + 4f, cy - 12f, 14f, 4f), c);
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
