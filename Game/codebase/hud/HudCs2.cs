using Godot;

/// <summary>
/// Competitive shooter HUD: semi-transparent text and icons with subtle dark backgrounds only
/// where required for legibility. Data points are exposed via public properties so real data
/// (health, ammo, round, etc.) can be wired in later.
/// Add as a node to the main scene; it creates its own CanvasLayer and layout.
/// </summary>
public partial class HudCs2 : Node
{
	/// <summary>Optional player reference. When set, stamina is read from the movement controller.</summary>
	[Export] public NetworkPlayer Player;
	[Export] public int CanvasLayerOrder = 40;
	[Export] public float Scale = 1.0f;

	[ExportGroup("Demo Data")]
	[Export] public int Health = 100;
	[Export] public int Armor = 100;
	[Export] public int Stamina = 100;
	[Export] public int AmmoCurrent = 30;
	[Export] public int AmmoReserve = 90;
	[Export] public string WeaponName = "AK-47";
	[Export] public int Money = 800;
	[Export] public int RoundTimeSec = 115;
	[Export] public int ScoreT = 0;
	[Export] public int ScoreCT = 0;
	[Export] public int RoundNumber = 1;
	[Export] public int MaxRounds = 9;
	[Export] public bool BombPlanted = false;
	[Export] public float BombTimer = 35f;

	[ExportGroup("Loadout (Demo)")]
	/// <summary>Smoke count; -1 renders as the infinity glyph for test mode.</summary>
	[Export] public int SmokeCount = -1;

	// Bombsite navigation: BombSpot lookups go through World.Level.BombSpotForSlot — no groups, no string
	// exports. The mapper places BombSpot nodes with the Slot dropdown and lists them in Level.BombSpotPaths,
	// the HUD asks for the BombSpot of each slot every frame.

	private CanvasLayer _layer;
	private Label _moneyLabel, _roundLabel;
	private Label _timeLabel, _scoreTLabel, _scoreCTLabel;
	private Label _bombTimerLabel;
	private Label _zoneLabel;
	private HudCompass _compass;
	private HudWeaponSlots _weaponSlots;
	private HudVitals _vitals;
	private VBoxContainer _topCol;
	private Zone _activeZone;
	private string _lastZoneText = null;
	private bool _navChecked;

	private int _lastHealth = int.MinValue, _lastArmor = int.MinValue, _lastStamina = int.MinValue;
	private bool _lastStaminaExhausted;
	private int _lastMoney = int.MinValue;
	private int _lastScoreT = int.MinValue, _lastScoreCT = int.MinValue;
	private int _lastRoundNumber = int.MinValue, _lastMaxRounds = int.MinValue;
	private int _lastRoundTimeSec = int.MinValue;
	private bool _lastBombPlanted;
	private float _lastBombTimer = float.MinValue;
	private int _lastAmmoCurrent = int.MinValue, _lastAmmoReserve = int.MinValue, _lastSmokeCount = int.MinValue;
	private string _lastWeaponName = null;
	private int _lastActiveSlot = int.MinValue;
	private float _lastGrenadeCharge = float.MinValue;
	private float _lastHeading = float.MinValue;
	private float _lastSiteABearing = float.NaN, _lastSiteBBearing = float.NaN, _lastSiteCBearing = float.NaN;
	private float _lastMarginH = float.MinValue, _lastMarginV = float.MinValue;

	private const float HudRefreshInterval = 0.5f;
	private float _hudRefreshTimer;

	private static readonly Color CtBlue = new(0.50f, 0.78f, 1f, 0.85f);
	private static readonly Color TOrange = new(1f, 0.72f, 0.30f, 0.85f);
	private static readonly Color MoneyGreen = new(0.55f, 0.95f, 0.55f, 0.85f);
	private static readonly Color TextWhite = new(1f, 1f, 1f, 0.86f);
	private static readonly Color TextDim = new(0.82f, 0.85f, 0.88f, 0.55f);
	private static readonly Color ScoreCaption = new(1f, 1f, 1f, 0.88f);
	private static readonly Color Alarm = new(1f, 0.42f, 0.32f, 0.95f);

	/// <summary>Resolves the player reference, creates the canvas layer, and builds all HUD sub-elements.</summary>
	public override void _Ready()
	{
		// Auf Dedicated-Server (NetMode.Server) ist HUD komplett überflüssig — kein Renderer, kein
		// User. Self-disable + queueFree damit nichts allokiert wird + _Process nie läuft.
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server)
		{
			QueueFree();
			return;
		}

		if (Player == null)
		{
			Node n = GetParent();
			while (n != null && Player == null)
			{
				if (n is NetworkPlayer lc) Player = lc;
				n = n.GetParent();
			}
		}

		_layer = new CanvasLayer { Layer = CanvasLayerOrder };
		AddChild(_layer);
		HudGate.Register(_layer);

		BuildTopBar();
		BuildVitals();
		BuildMoneyBlock();
		BuildBottomRight();
		BuildBombBanner();
		UpdateAll();
	}

	/// <summary>Drives the per-frame compass refresh and the throttled refresh of the rest of the HUD.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("HudCs2._Process");
		if (Player == null) Player = NetMain.Instance?.FindLocalPlayer();

		if (Player != null && _compass != null)
		{
			float heading = Mathf.PosMod(-Mathf.RadToDeg(Player.Rotation.Y), 360f);
			float siteA = BearingToBombSpot(BombSpot.BombSlot.A);
			float siteB = BearingToBombSpot(BombSpot.BombSlot.B);
			float siteC = BearingToBombSpot(BombSpot.BombSlot.C);
			if (Mathf.Abs(heading - _lastHeading) > 0.5f
				|| !NearlyEqualBearing(siteA, _lastSiteABearing)
				|| !NearlyEqualBearing(siteB, _lastSiteBBearing)
				|| !NearlyEqualBearing(siteC, _lastSiteCBearing))
			{
				_compass.HeadingDegrees = heading;
				_compass.SiteABearing = siteA;
				_compass.SiteBBearing = siteB;
				_compass.SiteCBearing = siteC;
				_compass.QueueRedraw();
				_lastHeading = heading;
				_lastSiteABearing = siteA;
				_lastSiteBBearing = siteB;
				_lastSiteCBearing = siteC;
			}

			UpdateZoneLabel();

			if (!_navChecked && World.Level is { Resolved: true } level)
			{
				_navChecked = true;
				Dbg.Print($"[HUD] BombSpots from Level: A={(level.BombSpotForSlot(BombSpot.BombSlot.A) != null ? "OK" : "MISSING")}" +
					$" · B={(level.BombSpotForSlot(BombSpot.BombSlot.B) != null ? "OK" : "MISSING")}" +
					$" · C={(level.BombSpotForSlot(BombSpot.BombSlot.C) != null ? "OK" : "MISSING")}");
			}
		}

		if (Player != null)
		{
			var mc = Player.Movement;
			Stamina = Mathf.RoundToInt(mc.Stamina / Mathf.Max(1f, ConVars.Sv.MaxStamina) * 100f);
			AmmoCurrent = mc.CurrentMag;
			AmmoReserve = mc.ReserveAmmo;
			WeaponName = ConVars.Weapons.AR15?.Name ?? "—";
			var snap = NetMain.Instance?.Client?.LastSelfSnap;
			if (snap.HasValue)
			{
				Health = snap.Value.Hp;
				Armor = snap.Value.Armor;
			}
		}

		UpdateAll();
		ApplyHudMargins();

		_hudRefreshTimer -= (float)delta;
		if (_hudRefreshTimer > 0f) return;
		_hudRefreshTimer = HudRefreshInterval;

		if (BombPlanted)
			BombTimer = Mathf.Max(0f, BombTimer - HudRefreshInterval);
		else
			RoundTimeSec = Mathf.Max(0, RoundTimeSec - (int)HudRefreshInterval);
	}

	/// <summary>Compares two bearings (in degrees, NaN allowed) for near equality within 0.5 degrees.</summary>
	private static bool NearlyEqualBearing(float a, float b)
	{
		if (float.IsNaN(a) && float.IsNaN(b)) return true;
		if (float.IsNaN(a) || float.IsNaN(b)) return false;
		return Mathf.Abs(a - b) < 0.5f;
	}

	/// <summary>Builds the top-center column: compass strip, score row, and round label.</summary>
	private void BuildTopBar()
	{
		var col = new VBoxContainer();
		_topCol = col;
		col.AddThemeConstantOverride("separation", 5);
		col.AnchorLeft = 0.5f; col.AnchorRight = 0.5f;
		col.AnchorTop = 0f; col.AnchorBottom = 0f;
		col.OffsetTop = Settings.HudMarginV;
		col.GrowHorizontal = Control.GrowDirection.Both;
		col.GrowVertical = Control.GrowDirection.End;

		_compass = new HudCompass
		{
			CustomMinimumSize = new Vector2(540f, 30f),
			ClipContents = true,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		col.AddChild(_compass);

		// Live "you are in: …" zone display directly under the compass strip. Updated by
		// UpdateZoneLabel() each frame — populated from the Zone group, point-in-box test against
		// every Zone in the scene. Empty string keeps it visually hidden (Label collapses to
		// minimum size) so 2-site maps without zones don't see a stray gap.
		_zoneLabel = MakeLabel("", 13, TextDim);
		_zoneLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_zoneLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		col.AddChild(_zoneLabel);

		var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		row.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		row.AddThemeConstantOverride("separation", 24);
		row.Alignment = BoxContainer.AlignmentMode.Center;

		_scoreCTLabel = Punchy(MakeLabel("0", 30, TextWhite));
		_scoreCTLabel.HorizontalAlignment = HorizontalAlignment.Center;
		row.AddChild(MakeScoreColumn("CT", _scoreCTLabel));

		var mid = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		mid.AddThemeConstantOverride("separation", 1);
		mid.Alignment = BoxContainer.AlignmentMode.Center;
		mid.AddChild(new ColorRect
		{
			Color = new Color("c00201"),
			CustomMinimumSize = new Vector2(48f, 3f),
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});
		_timeLabel = Punchy(MakeLabel("1:55", 28, TextWhite));
		_timeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_timeLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		mid.AddChild(_timeLabel);
		_roundLabel = Punchy(MakeLabel("ROUND 1 / 9", 12, ScoreCaption));
		_roundLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_roundLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		mid.AddChild(_roundLabel);
		row.AddChild(mid);

		_scoreTLabel = Punchy(MakeLabel("0", 30, TextWhite));
		_scoreTLabel.HorizontalAlignment = HorizontalAlignment.Center;
		row.AddChild(MakeScoreColumn("T", _scoreTLabel));

		col.AddChild(row);

		_layer.AddChild(col);
	}

	/// <summary>Builds the bottom-left vitals strip (health number plus health and stamina bars).</summary>
	private void BuildVitals()
	{
		_vitals = new HudVitals { MouseFilter = Control.MouseFilterEnum.Ignore };
		_vitals.CustomMinimumSize = new Vector2(HudVitals.StripW, HudVitals.StripH);
		AnchorCorner(_vitals, 0f, 1f, Settings.HudMarginH, -Settings.HudMarginV, Control.GrowDirection.End, Control.GrowDirection.Begin);
		_layer.AddChild(_vitals);
	}

	/// <summary>Builds the semi-transparent money text in the top-left corner.</summary>
	private void BuildMoneyBlock()
	{
		_moneyLabel = MakeLabel("$800", 26, MoneyGreen);
		AnchorCorner(_moneyLabel, 0f, 0f, Settings.HudMarginH, Settings.HudMarginV, Control.GrowDirection.End, Control.GrowDirection.End);
		_layer.AddChild(_moneyLabel);
	}

	/// <summary>Builds the bottom-right loadout strip (weapon, ammo, equipment slots).</summary>
	private void BuildBottomRight()
	{
		_weaponSlots = new HudWeaponSlots { MouseFilter = Control.MouseFilterEnum.Ignore };
		_weaponSlots.CustomMinimumSize = new Vector2(HudWeaponSlots.StripW, HudWeaponSlots.StripH);
		AnchorCorner(_weaponSlots, 1f, 1f, -Settings.HudMarginH, -Settings.HudMarginV, Control.GrowDirection.Begin, Control.GrowDirection.Begin);
		_layer.AddChild(_weaponSlots);
	}

	/// <summary>Builds the center-bottom bomb banner panel; hidden by default and shown when the bomb is planted.</summary>
	private void BuildBombBanner()
	{
		var panel = MakeSoftPanel(20, 20, 8, 10);
		panel.AnchorLeft = 0.5f; panel.AnchorRight = 0.5f;
		panel.AnchorTop = 1f; panel.AnchorBottom = 1f;
		panel.OffsetBottom = -150f;
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Begin;

		_bombTimerLabel = MakeLabel("◆ BOMB  35.0s", 24, new Color(1f, 0.45f, 0.32f, 0.95f));
		panel.AddChild(_bombTimerLabel);

		panel.Visible = false;
		_layer.AddChild(panel);
	}

	/// <summary>Pushes the cached HUD values into their widgets, only queueing redraws when something actually changed.</summary>
	private void UpdateAll()
	{
		if (_vitals != null)
		{
			bool exhausted = Player != null && Player.Movement.SprintExhausted;
			if (Health != _lastHealth || Armor != _lastArmor || Stamina != _lastStamina || exhausted != _lastStaminaExhausted)
			{
				_vitals.Health = Health;
				_vitals.Armor = Armor;
				_vitals.Stamina = Stamina;
				_vitals.StaminaExhausted = exhausted;
				_vitals.QueueRedraw();
				_lastHealth = Health; _lastArmor = Armor; _lastStamina = Stamina; _lastStaminaExhausted = exhausted;
			}
		}

		if (_moneyLabel != null && Money != _lastMoney)
		{
			_moneyLabel.Text = $"${Money}";
			_lastMoney = Money;
		}
		if (_roundLabel != null && (RoundNumber != _lastRoundNumber || MaxRounds != _lastMaxRounds))
		{
			_roundLabel.Text = $"ROUND {RoundNumber} / {MaxRounds}";
			_lastRoundNumber = RoundNumber; _lastMaxRounds = MaxRounds;
		}
		if (_scoreTLabel != null && ScoreT != _lastScoreT)
		{
			_scoreTLabel.Text = ScoreT.ToString();
			_lastScoreT = ScoreT;
		}
		if (_scoreCTLabel != null && ScoreCT != _lastScoreCT)
		{
			_scoreCTLabel.Text = ScoreCT.ToString();
			_lastScoreCT = ScoreCT;
		}
		if (_timeLabel != null && RoundTimeSec != _lastRoundTimeSec)
		{
			int min = RoundTimeSec / 60;
			int sec = RoundTimeSec % 60;
			_timeLabel.Text = $"{min}:{sec:D2}";
			_timeLabel.Modulate = RoundTimeSec <= 10 ? Alarm : Colors.White;
			_lastRoundTimeSec = RoundTimeSec;
		}
		if (_bombTimerLabel != null)
		{
			if (BombPlanted != _lastBombPlanted)
			{
				_bombTimerLabel.GetParent<Control>().Visible = BombPlanted;
				_lastBombPlanted = BombPlanted;
			}
			if (BombPlanted && Mathf.Abs(BombTimer - _lastBombTimer) >= 0.1f)
			{
				_bombTimerLabel.Text = $"◆ BOMB  {BombTimer:0.0}s";
				_lastBombTimer = BombTimer;
			}
		}

		UpdateLoadout();
	}

	/// <summary>Pushes loadout state to the weapon slot widget when any of its inputs change.</summary>
	private void UpdateLoadout()
	{
		if (_weaponSlots == null) return;

		int activeSlot = Player?.ActiveSlot ?? 0;
		float grenadeCharge = Player?.GrenadeCharge ?? 0f;
		bool changed = AmmoCurrent != _lastAmmoCurrent || AmmoReserve != _lastAmmoReserve
			|| SmokeCount != _lastSmokeCount || WeaponName != _lastWeaponName
			|| activeSlot != _lastActiveSlot || Mathf.Abs(grenadeCharge - _lastGrenadeCharge) > 0.01f;
		if (!changed) return;

		_weaponSlots.AmmoCurrent = AmmoCurrent;
		_weaponSlots.AmmoReserve = AmmoReserve;
		_weaponSlots.SmokeCount = SmokeCount;
		_weaponSlots.WeaponName = WeaponName;
		_weaponSlots.ActiveSlot = activeSlot;
		_weaponSlots.GrenadeCharge = grenadeCharge;
		_weaponSlots.QueueRedraw();
		_lastAmmoCurrent = AmmoCurrent; _lastAmmoReserve = AmmoReserve; _lastSmokeCount = SmokeCount;
		_lastWeaponName = WeaponName; _lastActiveSlot = activeSlot; _lastGrenadeCharge = grenadeCharge;
	}

	/// <summary>Finds the <see cref="Zone"/> the player currently stands in and writes its name
	/// into the zone label under the compass. Delegates to <see cref="Level.ZoneAt"/> which handles
	/// the point-in-box test + smallest-volume tiebreak (innermost nested zone wins). The Level
	/// registry resolves itself on map _Ready, so there's nothing to lazily scan here.</summary>
	private void UpdateZoneLabel()
	{
		if (_zoneLabel == null || Player == null) return;
		var z = World.Level?.ZoneAt(Player.GlobalPosition);
		_activeZone = z;
		string text = z != null ? z.ZoneName : "";
		if (text == _lastZoneText) return;
		_zoneLabel.Text = text;
		_lastZoneText = text;
	}

	/// <summary>
	/// Compass bearing (0..360 degrees, north = -Z) from the player to the BombSpot with the
	/// given slot. Resolved via <see cref="Level.BombSpotForSlot"/>. Returns NaN when the map has no
	/// BombSpot for that slot (so e.g. C-less 2-site maps skip drawing the C diamond).
	/// </summary>
	private float BearingToBombSpot(BombSpot.BombSlot slot)
	{
		if (Player == null) return float.NaN;
		var spot = World.Level?.BombSpotForSlot(slot);
		if (spot == null) return float.NaN;
		Vector3 d = spot.GlobalPosition - Player.GlobalPosition;
		if (d.X * d.X + d.Z * d.Z < 0.01f) return float.NaN;
		return Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), 360f);
	}

	/// <summary>Builds a label with a light outline plus shadow so it stays legible without a background box.</summary>
	private static Label MakeLabel(string text, int fontSize, Color color)
	{
		var lbl = new Label
		{
			Text = text,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		lbl.AddThemeFontSizeOverride("font_size", fontSize);
		lbl.AddThemeColorOverride("font_color", color);
		lbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.55f));
		lbl.AddThemeConstantOverride("outline_size", 3);
		lbl.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.45f));
		lbl.AddThemeConstantOverride("shadow_offset_x", 1);
		lbl.AddThemeConstantOverride("shadow_offset_y", 1);
		return lbl;
	}

	/// <summary>Applies a stronger outline so a label stays legible against bright backgrounds.</summary>
	private static Label Punchy(Label lbl)
	{
		lbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		lbl.AddThemeConstantOverride("outline_size", 5);
		return lbl;
	}

	/// <summary>Builds a score column with a large number on top and a small caption below.</summary>
	private static VBoxContainer MakeScoreColumn(string caption, Label number)
	{
		var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		v.AddThemeConstantOverride("separation", 0);
		v.Alignment = BoxContainer.AlignmentMode.Center;
		v.CustomMinimumSize = new Vector2(52f, 0f);

		var cap = Punchy(MakeLabel(caption, 12, ScoreCaption));
		cap.HorizontalAlignment = HorizontalAlignment.Center;
		cap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		number.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		v.AddChild(number);
		v.AddChild(cap);
		return v;
	}

	/// <summary>Builds a soft, rounded, semi-transparent dark panel used only where a background helps legibility.</summary>
	private static PanelContainer MakeSoftPanel(int padL, int padR, int padT, int padB)
	{
		var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		var sb = new StyleBoxFlat
		{
			BgColor = new Color(0.02f, 0.03f, 0.04f, 0.44f),
			CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
			CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
			ContentMarginLeft = padL, ContentMarginRight = padR,
			ContentMarginTop = padT, ContentMarginBottom = padB,
			ShadowColor = new Color(0, 0, 0, 0.28f),
			ShadowSize = 7,
		};
		panel.AddThemeStyleboxOverride("panel", sb);
		return panel;
	}

	/// <summary>Anchors a content-sized control to a screen corner.</summary>
	private static void AnchorCorner(Control c, float ax, float ay, float offX, float offY,
		Control.GrowDirection growH, Control.GrowDirection growV)
	{
		c.AnchorLeft = ax; c.AnchorRight = ax;
		c.AnchorTop = ay; c.AnchorBottom = ay;
		c.OffsetLeft = offX; c.OffsetRight = offX;
		c.OffsetTop = offY; c.OffsetBottom = offY;
		c.GrowHorizontal = growH;
		c.GrowVertical = growV;
	}

	/// <summary>
	/// Applies the HUD edge margins from <see cref="Settings"/> to all corner-anchored elements
	/// so changes from the settings menu take effect immediately. Cached so it is a no-op when stable.
	/// </summary>
	private void ApplyHudMargins()
	{
		float h = Settings.HudMarginH, v = Settings.HudMarginV;
		if (h == _lastMarginH && v == _lastMarginV) return;
		_lastMarginH = h; _lastMarginV = v;
		if (_topCol != null) _topCol.OffsetTop = v;
		SetCornerOffset(_moneyLabel, h, v);
		SetCornerOffset(_vitals, h, -v);
		SetCornerOffset(_weaponSlots, -h, -v);
	}

	/// <summary>Sets the four offsets of a corner-anchored control while keeping its anchor and grow settings.</summary>
	private static void SetCornerOffset(Control c, float offX, float offY)
	{
		if (c == null) return;
		c.OffsetLeft = offX; c.OffsetRight = offX;
		c.OffsetTop = offY; c.OffsetBottom = offY;
	}
}
