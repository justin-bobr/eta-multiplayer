using Godot;

namespace Vantix.UI;

/// <summary>
/// Dynamic ConVar-driven crosshair. Expands with player speed and per-shot fire kicks (decaying),
/// optionally hides during ADS. Creates its own CanvasLayer and drawer; assign <see cref="Player"/>.
/// </summary>
public partial class Crosshair : Node
{
	[Export] public NetworkPlayer Player;
	[Export] public int CanvasLayerOrder = 50;

	private CanvasLayer _layer;
	private CrosshairDrawer _drawer;

	/// <summary>Creates the canvas layer and drawer control on scene entry.</summary>
	public override void _Ready()
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server) { QueueFree(); return; }
		_layer = new CanvasLayer { Layer = CanvasLayerOrder };
		AddChild(_layer);
		HudGate.Register(_layer);

		_drawer = new CrosshairDrawer { Player = Player };
		_drawer.AnchorLeft = 0f;
		_drawer.AnchorTop = 0f;
		_drawer.AnchorRight = 1f;
		_drawer.AnchorBottom = 1f;
		_drawer.MouseFilter = Control.MouseFilterEnum.Ignore;
		_layer.AddChild(_drawer);
	}
}

/// <summary>Inner drawing node. Polls movement state, animates the fire-kick gap, and renders via custom _Draw.</summary>
public partial class CrosshairDrawer : Control
{
	public NetworkPlayer Player;

	private float _fireKickGap;
	private int _lastSeenShotIndex;

	/// <summary>Per-frame poll of player state and decay of the fire-kick gap.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("Crosshair._Process");
		if (Player == null) Player = NetMain.Instance?.FindLocalPlayer();
		if (Player == null) { Visible = false; QueueRedraw(); return; }
		Visible = true;

		float dt = (float)delta;

		if (ConVars.Cl.CrosshairDynamicFiring)
		{
			var mc = Player.Movement;
			if (mc.ShotIndex > _lastSeenShotIndex)
			{
				_fireKickGap += ConVars.Cl.CrosshairFireKickAmount;
				_lastSeenShotIndex = mc.ShotIndex;
			}
			else if (mc.ShotIndex == 0)
			{
				_lastSeenShotIndex = 0;
			}
		}
		_fireKickGap = Mathf.Lerp(_fireKickGap, 0f, Mathf.Min(1f, ConVars.Cl.CrosshairFireRecoverSpeed * dt));

		QueueRedraw();
	}

	/// <summary>Renders the four-line crosshair plus optional outline and center dot.</summary>
	public override void _Draw()
	{
		if (!ConVars.Cl.CrosshairEnabled) return;

		float adsAlpha = 1f;
		if (Player != null && ConVars.Cl.CrosshairHideDuringAds && Player.ViewMode != ViewMode.Tps)
			adsAlpha = 1f - Player.Movement.AdsBlendVisual;
		if (adsAlpha <= 0.01f) return;

		Vector2 center = Size / 2f;

		float gap = ConVars.Cl.CrosshairInnerGap;
		if (Player != null)
		{
			if (ConVars.Cl.CrosshairDynamicMovement)
				gap += Player.Movement.HorizontalSpeed * ConVars.Cl.CrosshairMovementMul;
			gap += _fireKickGap;
		}

		float thickness = ConVars.Cl.CrosshairThickness;
		float length = ConVars.Cl.CrosshairLength;
		float outlineT = ConVars.Cl.CrosshairOutlineThickness;
		bool drawOutline = ConVars.Cl.CrosshairShowOutline && outlineT > 0f;
		Color color = ConVars.Cl.CrosshairColor with { A = ConVars.Cl.CrosshairColor.A * adsAlpha };
		Color outlineColor = ConVars.Cl.CrosshairOutlineColor with { A = ConVars.Cl.CrosshairOutlineColor.A * adsAlpha };

		DrawLineSeg(new Vector2(center.X, center.Y - gap - length), new Vector2(center.X, center.Y - gap), thickness, outlineT, drawOutline, color, outlineColor);
		DrawLineSeg(new Vector2(center.X, center.Y + gap), new Vector2(center.X, center.Y + gap + length), thickness, outlineT, drawOutline, color, outlineColor);
		DrawLineSeg(new Vector2(center.X - gap - length, center.Y), new Vector2(center.X - gap, center.Y), thickness, outlineT, drawOutline, color, outlineColor);
		DrawLineSeg(new Vector2(center.X + gap, center.Y), new Vector2(center.X + gap + length, center.Y), thickness, outlineT, drawOutline, color, outlineColor);

		if (ConVars.Cl.CrosshairShowDot)
		{
			float ds = ConVars.Cl.CrosshairDotSize;
			Rect2 dotRect = new(center.X - ds * 0.5f, center.Y - ds * 0.5f, ds, ds);
			if (drawOutline)
			{
				Rect2 dotOut = dotRect.Grow(outlineT);
				DrawRect(dotOut, outlineColor, true);
			}
			Color dotColor = ConVars.Cl.CrosshairDotColor with { A = ConVars.Cl.CrosshairDotColor.A * adsAlpha };
			DrawRect(dotRect, dotColor, true);
		}
	}

	/// <summary>Draws an axis-aligned line as a rect (for clean pixel thickness) with an optional outline.</summary>
	private void DrawLineSeg(Vector2 from, Vector2 to, float thickness, float outlineT, bool outline, Color color, Color outlineColor)
	{
		bool vertical = Mathf.Abs(to.X - from.X) < 0.01f;
		Rect2 rect = vertical
			? new Rect2(from.X - thickness * 0.5f, Mathf.Min(from.Y, to.Y), thickness, Mathf.Abs(to.Y - from.Y))
			: new Rect2(Mathf.Min(from.X, to.X), from.Y - thickness * 0.5f, Mathf.Abs(to.X - from.X), thickness);

		if (outline)
		{
			Rect2 outRect = rect.Grow(outlineT);
			DrawRect(outRect, outlineColor, true);
		}
		DrawRect(rect, color, true);
	}
}
