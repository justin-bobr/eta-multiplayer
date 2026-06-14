using Godot;

namespace Vantix.Hud;

/// <summary>
/// Floating compass strip (no background box). HUD feeds <see cref="HeadingDegrees"/> each frame;
/// ticks every 5°, numeric labels every 15°, cardinals every 45°, red center marker.
/// </summary>
public partial class HudCompass : Control
{
	/// <summary>Current heading in degrees (0..360); set by the HUD each frame.</summary>
	public float HeadingDegrees;
	/// <summary>Compass bearing to bombsite A; NaN hides the marker.</summary>
	public float SiteABearing = float.NaN;
	/// <summary>Compass bearing to bombsite B; NaN hides the marker.</summary>
	public float SiteBBearing = float.NaN;
	/// <summary>Compass bearing to bombsite C; NaN hides the marker (standard 2-site maps leave this NaN).</summary>
	public float SiteCBearing = float.NaN;

	/// <summary>Degrees of arc visible across the full strip width.</summary>
	private const float VisibleRange = 104f;
	private static readonly string[] Cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
	private static readonly Color Shadow = new(0f, 0f, 0f, 0.6f);
	private static readonly Color Marker = new(1f, 0.36f, 0.30f);
	private static readonly Color Objective = new(1f, 0.82f, 0.30f);

	/// <summary>Renders ticks, numeric labels, the center marker, and any active objective markers.</summary>
	public override void _Draw()
	{
		float w = Size.X, h = Size.Y;
		if (w <= 1f) return;

		Font font = GetThemeDefaultFont();
		float pxPerDeg = w / VisibleRange;
		float center = w * 0.5f;
		float baseY = h - 2f;

		for (int d = 0; d < 360; d += 5)
		{
			float diff = Mathf.Wrap(d - HeadingDegrees + 180f, 0f, 360f) - 180f;
			float x = center + diff * pxPerDeg;
			if (x < -4f || x > w + 4f) continue;

			bool cardinal = d % 45 == 0;
			bool mid = d % 15 == 0;
			float tickH = cardinal ? h * 0.46f : (mid ? h * 0.28f : h * 0.16f);
			Color col = cardinal ? new Color(1, 1, 1, 0.95f)
								  : new Color(1, 1, 1, mid ? 0.55f : 0.30f);
			float tw = cardinal ? 2f : 1f;

			var a = new Vector2(x, baseY);
			var b = new Vector2(x, baseY - tickH);
			DrawLine(a + Vector2.One, b + Vector2.One, Shadow, tw);
			DrawLine(a, b, col, tw);

			if (font == null) continue;
			if (cardinal)
				DrawText(font, Cardinals[(d / 45) % 8], x, baseY - tickH - 3f, 14, new Color(1, 1, 1, 0.97f));
			else if (mid)
				DrawText(font, d.ToString(), x, baseY - tickH - 3f, 10, new Color(1, 1, 1, 0.62f));
		}

		DrawLine(new Vector2(center + 1f, 3f), new Vector2(center + 1f, h - 1f), Shadow, 2f);
		DrawLine(new Vector2(center, 2f), new Vector2(center, h - 2f), Marker, 2f);
		Vector2[] tri = { new(center - 6f, 0f), new(center + 6f, 0f), new(center, 8f) };
		DrawColoredPolygon(tri, Marker);

		DrawSiteMarker(font, "A", SiteABearing);
		DrawSiteMarker(font, "B", SiteBBearing);
		DrawSiteMarker(font, "C", SiteCBearing);
	}

	/// <summary>Draws a bombsite marker at its bearing; off-screen targets clamp to the edge with a direction arrow.</summary>
	private void DrawSiteMarker(Font font, string letter, float bearing)
	{
		if (font == null || float.IsNaN(bearing)) return;

		float w = Size.X;
		float pxPerDeg = w / VisibleRange;
		float center = w * 0.5f;
		float diff = Mathf.Wrap(bearing - HeadingDegrees + 180f, 0f, 360f) - 180f;
		float rawX = center + diff * pxPerDeg;

		const float edge = 10f;
		float x = rawX;
		float arrowDir = 0f;
		if (rawX < edge) { x = edge + 11f; arrowDir = -1f; }
		else if (rawX > w - edge) { x = w - edge - 11f; arrowDir = 1f; }

		if (arrowDir != 0f)
		{
			float tipX = arrowDir < 0f ? edge : w - edge;
			float baseX = tipX - arrowDir * 7f;
			Vector2[] arrow = { new(tipX, 7f), new(baseX, 2f), new(baseX, 12f) };
			DrawColoredPolygon(arrow, Objective);
		}

		const float by = 7f, r = 7f;
		Vector2[] dia = { new(x, by - r), new(x + r, by), new(x, by + r), new(x - r, by) };
		Vector2[] sh  = { new(x + 1f, by - r + 1f), new(x + r + 1f, by + 1f),
						  new(x + 1f, by + r + 1f), new(x - r + 1f, by + 1f) };
		DrawColoredPolygon(sh, Shadow);
		DrawColoredPolygon(dia, Objective);

		Vector2 sz = font.GetStringSize(letter, HorizontalAlignment.Left, -1, 11);
		DrawString(font, new Vector2(x - sz.X * 0.5f, by + 4f), letter,
			HorizontalAlignment.Left, -1, 11, new Color(0f, 0f, 0f, 0.9f));
	}

	/// <summary>Draws centered text with a shadow; floating, with no box background.</summary>
	private void DrawText(Font font, string text, float cx, float baselineY, int fontSize, Color color)
	{
		Vector2 sz = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
		var pos = new Vector2(cx - sz.X * 0.5f, baselineY);
		DrawString(font, pos + Vector2.One, text, HorizontalAlignment.Left, -1, fontSize, Shadow);
		DrawString(font, pos, text, HorizontalAlignment.Left, -1, fontSize, color);
	}
}
