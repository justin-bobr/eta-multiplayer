using Godot;

/// <summary>
/// Bottom-left vitals strip without a background: red cross icon plus health number, then a
/// shield icon plus armor number. Below that a solid health bar and a thinner stamina bar.
/// Rendered directly via <see cref="_Draw"/>.
/// </summary>
public partial class HudVitals : Control
{
	public int Health = 100;
	public int Armor = 100;
	public int Stamina = 100;
	public bool StaminaExhausted;

	public const float StripW = 290f;
	public const float StripH = 48f;

	private static readonly Color Shadow = new(0f, 0f, 0f, 0.72f);
	private static readonly Color Track = new(0f, 0f, 0f, 0.46f);
	private static readonly Color HealthBar = new("c00201", 0.95f);
	private static readonly Color StaminaBar = new("01c002", 0.92f);
	private static readonly Color Alarm = new(1f, 0.42f, 0.32f, 0.97f);
	private static readonly Color TextWhite = new(1f, 1f, 1f, 0.96f);
	private static readonly Color ArmorText = new(1f, 1f, 1f, 0.80f);
	private static readonly Color IconDim = new(1f, 1f, 1f, 0.62f);

	/// <summary>Renders the cross icon, health and armor numbers, and the health and stamina bars.</summary>
	public override void _Draw()
	{
		if (Size.X <= 1f) return;
		Font font = GetThemeDefaultFont();

		bool hpLow = Health <= 25;
		Color hpCol = hpLow ? Alarm : TextWhite;
		const float iconCy = 15f;

		DrawCross(new Vector2(11f, iconCy), 9f, 4f, hpLow ? Alarm : HealthBar);

		float x = DrawNumber(font, Health.ToString(), 26f, iconCy, 34, hpCol);

		x += 16f;
		DrawShield(new Vector2(x + 8f, iconCy), 8f, IconDim);
		x += 22f;
		DrawNumber(font, Armor.ToString(), x, iconCy, 22, ArmorText);

		DrawSolidBar(new Rect2(2f, 33f, 224f, 6f), Health / 100f, hpLow ? Alarm : HealthBar);
		DrawSolidBar(new Rect2(2f, 42f, 224f, 3f),
			StaminaExhausted ? 0f : Stamina / 100f, StaminaExhausted ? Alarm : StaminaBar);
	}

	/// <summary>Draws a solid bar: dark track with a proportional fill, no segmenting.</summary>
	private void DrawSolidBar(Rect2 r, float frac, Color col)
	{
		frac = Mathf.Clamp(frac, 0f, 1f);
		DrawRect(r, Track);
		if (frac > 0f)
			DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * frac, r.Size.Y)), col);
	}

	/// <summary>Draws a red plus cross with a shadow. (c) = center, arm = half length, thick = thickness.</summary>
	private void DrawCross(Vector2 c, float arm, float thick, Color col)
	{
		var v = new Rect2(c.X - thick * 0.5f, c.Y - arm, thick, arm * 2f);
		var h = new Rect2(c.X - arm, c.Y - thick * 0.5f, arm * 2f, thick);
		DrawRect(new Rect2(v.Position + Vector2.One, v.Size), Shadow);
		DrawRect(new Rect2(h.Position + Vector2.One, h.Size), Shadow);
		DrawRect(v, col);
		DrawRect(h, col);
	}

	/// <summary>Draws a shield outline. (c) = center, hw = half width.</summary>
	private void DrawShield(Vector2 c, float hw, Color col)
	{
		Vector2[] p =
		{
			new(c.X - hw, c.Y - hw),
			new(c.X + hw, c.Y - hw),
			new(c.X + hw, c.Y + hw * 0.35f),
			new(c.X, c.Y + hw * 1.4f),
			new(c.X - hw, c.Y + hw * 0.35f),
			new(c.X - hw, c.Y - hw),
		};
		DrawPolyline(p, col, 1.6f, true);
	}

	/// <summary>Draws a shadowed number; (x, centerY) is the left edge / vertical center. Returns the right edge.</summary>
	private float DrawNumber(Font font, string text, float x, float centerY, int fs, Color col)
	{
		if (font == null) return x;
		Vector2 sz = font.GetStringSize(text, HorizontalAlignment.Left, -1, fs);
		var pos = new Vector2(x, centerY + sz.Y * 0.32f);
		DrawString(font, pos + new Vector2(2f, 2f), text, HorizontalAlignment.Left, -1, fs, Shadow);
		DrawString(font, pos, text, HorizontalAlignment.Left, -1, fs, col);
		return x + sz.X;
	}
}
