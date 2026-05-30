using Godot;
using System.Collections.Generic;

/// <summary>
/// Tab-activated CS2-style scoreboard. Header (mode + map) on top, per-team sections with a
/// big colored team badge on the left (score + name + alive count) and the player rows on the
/// right. Switches to a single-list layout for Deathmatch (no teams).
///
/// Data is pulled from <see cref="NetClient.LastSelfSnap"/> + <see cref="NetClient.LastRemoteSnapshots"/>.
/// Action: <c>scoreboard</c> (default = Tab). Refresh 4 Hz throttled.
/// </summary>
public partial class Scoreboard : CanvasLayer
{
	private const string ActionName = "scoreboard";
	private const float RefreshInterval = 0.25f;

	private PanelContainer _panel;
	private VBoxContainer _master;
	private Label _headerModeLabel;
	private Label _headerTimerLabel;
	private TeamSection _vektor;
	private TeamSection _atlas;
	private TeamSection _dm;
	private float _refreshTimer;

	/// <summary>Per-team UI cluster — badge on left (score + name + alive) plus a vertical
	/// stack of player rows on the right. Rows are pooled per section.</summary>
	private class TeamSection
	{
		public HBoxContainer Container;
		public PanelContainer Badge;
		public Label BadgeScore;
		public Label BadgeName;
		public Label BadgeAlive;
		public VBoxContainer RowsContainer;
		public List<RowWidgets> Rows = new();
		public StyleBoxFlat BadgeStyle;
	}

	private class RowWidgets
	{
		public PanelContainer Outer;
		public HBoxContainer Row;
		public StyleBoxFlat BgStyle;
		public ColorRect ColorSquare;
		public Label Name, Kills, Deaths, Ping;
	}

	public override void _Ready()
	{
		Layer = 90;
		BuildUi();
		_panel.Visible = false;
	}

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("Scoreboard._Process");
		bool wantShow = Input.IsActionPressed(ActionName);
		if (wantShow != _panel.Visible)
		{
			_panel.Visible = wantShow;
			if (wantShow) RefreshRows();
			_refreshTimer = 0f;
		}
		if (_panel.Visible)
		{
			_refreshTimer -= (float)delta;
			if (_refreshTimer <= 0f)
			{
				_refreshTimer = RefreshInterval;
				RefreshRows();
			}
		}
	}

	private void BuildUi()
	{
		_panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		// Anchor top-center: anchors all at center-top, dann explizite Offsets links/rechts gleich
		// weit auseinander = horizontal zentriert. Width via offset_right - offset_left = 880.
		_panel.AnchorLeft = 0.5f;
		_panel.AnchorRight = 0.5f;
		_panel.AnchorTop = 0f;
		_panel.AnchorBottom = 0f;
		_panel.OffsetLeft = -440f;
		_panel.OffsetRight = 440f;
		_panel.OffsetTop = 80f;
		_panel.OffsetBottom = 80f;
		_panel.GrowVertical = Control.GrowDirection.End;
		_panel.GrowHorizontal = Control.GrowDirection.Both;
		var panelStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.06f, 0.06f, 0.07f, 0.92f),
			BorderColor = new Color(0.28f, 0.28f, 0.30f, 0.6f),
			BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
			CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
			ContentMarginLeft = 18, ContentMarginRight = 18,
			ContentMarginTop = 14, ContentMarginBottom = 14,
		};
		_panel.AddThemeStyleboxOverride("panel", panelStyle);
		AddChild(_panel);

		_master = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_master.AddThemeConstantOverride("separation", 10);
		_panel.AddChild(_master);

		BuildHeader();
		BuildColumnHeader();
		_vektor = BuildTeamSection((byte)Team.Team1);
		_atlas = BuildTeamSection((byte)Team.Team2);
		_dm = BuildTeamSection((byte)Team.Deathmatch);
	}

	private void BuildHeader()
	{
		var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_headerModeLabel = new Label
		{
			Text = "",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_headerModeLabel.AddThemeFontSizeOverride("font_size", 18);
		_headerModeLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
		row.AddChild(_headerModeLabel);

		_headerTimerLabel = new Label
		{
			Text = "",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		_headerTimerLabel.AddThemeFontSizeOverride("font_size", 18);
		_headerTimerLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
		row.AddChild(_headerTimerLabel);

		_master.AddChild(row);

		var sep = new HSeparator();
		_master.AddChild(sep);
	}

	private void BuildColumnHeader()
	{
		var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride("separation", 10);
		var spacer = new Control
		{
			CustomMinimumSize = new Vector2(108, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		row.AddChild(spacer);

		var sub = new HBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		sub.AddThemeConstantOverride("separation", 8);
		var headerColor = new Color(0.62f, 0.66f, 0.72f);
		var swatchSpacer = new Control { CustomMinimumSize = new Vector2(12, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
		sub.AddChild(swatchSpacer);
		sub.AddChild(MakeHeaderCell("Player", headerColor, 0, expand: true));
		sub.AddChild(MakeHeaderCell("K", headerColor, 60));
		sub.AddChild(MakeHeaderCell("D", headerColor, 60));
		sub.AddChild(MakeHeaderCell("Ping", headerColor, 80));
		row.AddChild(sub);
		_master.AddChild(row);
	}

	private TeamSection BuildTeamSection(byte team)
	{
		var section = new TeamSection();
		section.Container = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		section.Container.AddThemeConstantOverride("separation", 10);

		section.Badge = new PanelContainer
		{
			CustomMinimumSize = new Vector2(108, 90),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		section.BadgeStyle = new StyleBoxFlat
		{
			BgColor = TeamBadgeBgColor(team),
			BorderColor = new Color(0f, 0f, 0f, 0.4f),
			BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
			CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
			ContentMarginLeft = 8, ContentMarginRight = 8,
			ContentMarginTop = 6, ContentMarginBottom = 6,
		};
		section.Badge.AddThemeStyleboxOverride("panel", section.BadgeStyle);

		var badgeBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		badgeBox.AddThemeConstantOverride("separation", 0);
		badgeBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		badgeBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		section.BadgeScore = new Label
		{
			Text = "0",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		section.BadgeScore.AddThemeFontSizeOverride("font_size", 36);
		section.BadgeScore.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
		section.BadgeScore.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.7f));
		section.BadgeScore.AddThemeConstantOverride("outline_size", 3);
		badgeBox.AddChild(section.BadgeScore);

		section.BadgeName = new Label
		{
			Text = TeamLabelText(team),
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		section.BadgeName.AddThemeFontSizeOverride("font_size", 11);
		section.BadgeName.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.9f));
		badgeBox.AddChild(section.BadgeName);

		section.BadgeAlive = new Label
		{
			Text = "",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		section.BadgeAlive.AddThemeFontSizeOverride("font_size", 10);
		section.BadgeAlive.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f, 0.75f));
		badgeBox.AddChild(section.BadgeAlive);

		section.Badge.AddChild(badgeBox);
		section.Container.AddChild(section.Badge);

		section.RowsContainer = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		section.RowsContainer.AddThemeConstantOverride("separation", 2);
		section.Container.AddChild(section.RowsContainer);

		section.Container.Visible = false;
		_master.AddChild(section.Container);
		return section;
	}

	private readonly List<(byte netId, string name, byte kills, byte deaths, byte ping, bool isSelf, byte team, byte teamSlot, byte hp)> _entryBuf = new();
	private readonly List<(byte netId, string name, byte kills, byte deaths, byte ping, bool isSelf, byte team, byte teamSlot, byte hp)> _ctBuf = new();
	private readonly List<(byte netId, string name, byte kills, byte deaths, byte ping, bool isSelf, byte team, byte teamSlot, byte hp)> _tBuf = new();

	private void RefreshRows()
	{
		var net = NetMain.Instance;
		if (net == null) return;
		var client = net.Client;

		_entryBuf.Clear();
		_ctBuf.Clear();
		_tBuf.Clear();
		if (client != null)
		{
			if (client.LastSelfSnap.HasValue)
			{
				var s = client.LastSelfSnap.Value;
				_entryBuf.Add((client.OwnNetId, net.Cli?.PlayerName ?? "You", s.Kills, s.Deaths, s.PingMs, true, s.Team, s.TeamSlot, s.Hp));
			}
			foreach (var kv in client.LastRemoteSnapshots)
			{
				string name = client.RemotePlayers.TryGetValue(kv.Key, out var r) ? r.PlayerName : $"P{kv.Key}";
				_entryBuf.Add((kv.Key, name, kv.Value.Kills, kv.Value.Deaths, kv.Value.PingMs, false, kv.Value.Team, kv.Value.TeamSlot, kv.Value.Hp));
			}
		}

		string mapPart = client != null && !string.IsNullOrEmpty(client.MapName) ? client.MapName : "";
		_headerModeLabel.Text = string.IsNullOrEmpty(mapPart) ? "" : mapPart;
		if (client != null && client.RoundsTotal > 0)
		{
			int remaining = client.RoundTimeRemainingSec;
			int min = remaining / 60;
			int sec = remaining % 60;
			_headerTimerLabel.Text = $"Round {client.RoundNumber} / {client.RoundsTotal}   {min}:{sec:D2}";
		}
		else
		{
			_headerTimerLabel.Text = "";
		}

		bool isDM = _entryBuf.Count > 0 && _entryBuf[0].team == (byte)Team.Deathmatch;
		_vektor.Container.Visible = !isDM;
		_atlas.Container.Visible = !isDM;
		_dm.Container.Visible = isDM;

		if (isDM)
		{
			_entryBuf.Sort((a, b) => b.kills.CompareTo(a.kills));
			PopulateSection(_dm, _entryBuf, isDeathmatch: true);
		}
		else
		{
			foreach (var e in _entryBuf)
			{
				if (e.team == (byte)Team.Team1) _ctBuf.Add(e);
				else if (e.team == (byte)Team.Team2) _tBuf.Add(e);
			}
			_ctBuf.Sort((a, b) => b.kills.CompareTo(a.kills));
			_tBuf.Sort((a, b) => b.kills.CompareTo(a.kills));
			PopulateSection(_vektor, _ctBuf, isDeathmatch: false);
			PopulateSection(_atlas, _tBuf, isDeathmatch: false);
		}
	}

	private void PopulateSection(TeamSection section, List<(byte netId, string name, byte kills, byte deaths, byte ping, bool isSelf, byte team, byte teamSlot, byte hp)> entries, bool isDeathmatch)
	{
		while (section.Rows.Count < entries.Count) section.Rows.Add(CreateRowWidget(section.RowsContainer));

		int totalKills = 0, alive = 0;
		for (int i = 0; i < entries.Count; i++)
		{
			var e = entries[i];
			totalKills += e.kills;
			if (e.hp > 0) alive++;
			var row = section.Rows[i];
			row.Outer.Visible = true;

			Color slotColor = isDeathmatch ? new Color(0.9f, 0.9f, 0.9f) : PuppetPlayer.PlayerColor(e.teamSlot);
			bool dead = e.hp == 0;
			Color textColor = dead ? new Color(0.55f, 0.55f, 0.55f) : (e.isSelf ? slotColor.Lightened(0.30f) : slotColor);

			row.BgStyle.BgColor = e.isSelf
				? new Color(1f, 0.85f, 0.30f, 0.18f)
				: new Color(1f, 1f, 1f, i % 2 == 0 ? 0.03f : 0.06f);

			row.ColorSquare.Color = slotColor;
			row.ColorSquare.Visible = !isDeathmatch;
			row.Name.Text = e.name + (e.isSelf ? " (you)" : "") + (dead ? "  ✕" : "");
			row.Name.AddThemeColorOverride("font_color", textColor);
			row.Kills.Text = e.kills.ToString();
			row.Kills.AddThemeColorOverride("font_color", textColor);
			row.Deaths.Text = e.deaths.ToString();
			row.Deaths.AddThemeColorOverride("font_color", textColor);
			row.Ping.Text = $"{e.ping}ms";
			row.Ping.AddThemeColorOverride("font_color", textColor);
		}
		for (int i = entries.Count; i < section.Rows.Count; i++) section.Rows[i].Outer.Visible = false;

		section.BadgeScore.Text = totalKills.ToString();
		section.BadgeAlive.Text = isDeathmatch ? $"{entries.Count} players" : $"Alive: {alive} / {entries.Count}";
	}

	private RowWidgets CreateRowWidget(VBoxContainer parent)
	{
		var rw = new RowWidgets();
		rw.Outer = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		rw.BgStyle = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0f),
			ContentMarginLeft = 6, ContentMarginRight = 6,
			ContentMarginTop = 3, ContentMarginBottom = 3,
		};
		rw.Outer.AddThemeStyleboxOverride("panel", rw.BgStyle);

		rw.Row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		rw.Row.AddThemeConstantOverride("separation", 8);

		rw.ColorSquare = new ColorRect
		{
			CustomMinimumSize = new Vector2(12, 12),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		rw.Name = MakeRowCell(0, expand: true);
		rw.Kills = MakeRowCell(60);
		rw.Deaths = MakeRowCell(60);
		rw.Ping = MakeRowCell(80);

		rw.Row.AddChild(rw.ColorSquare);
		rw.Row.AddChild(rw.Name);
		rw.Row.AddChild(rw.Kills);
		rw.Row.AddChild(rw.Deaths);
		rw.Row.AddChild(rw.Ping);
		rw.Outer.AddChild(rw.Row);
		parent.AddChild(rw.Outer);
		return rw;
	}

	private static Label MakeHeaderCell(string text, Color color, int minWidth, bool expand = false)
	{
		var l = new Label
		{
			Text = text,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		if (minWidth > 0) l.CustomMinimumSize = new Vector2(minWidth, 0);
		if (expand) l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		l.AddThemeFontSizeOverride("font_size", 12);
		l.AddThemeColorOverride("font_color", color);
		return l;
	}

	private static Label MakeRowCell(int minWidth, bool expand = false)
	{
		var l = new Label
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		if (minWidth > 0) l.CustomMinimumSize = new Vector2(minWidth, 0);
		if (expand) l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		l.AddThemeFontSizeOverride("font_size", 14);
		l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		l.AddThemeConstantOverride("outline_size", 2);
		return l;
	}

	private static string TeamLabelText(byte team) => team switch
	{
		(byte)Team.Team1 => Teams.Team1Name,
		(byte)Team.Team2 => Teams.Team2Name,
		(byte)Team.Deathmatch => Teams.DeathmatchName,
		_ => "",
	};

	/// <summary>Solid colored badge background per team (saturated, low alpha so the overall panel
	/// stays dark). Team1=blau, Team2=orange, DM=neutralgrey.</summary>
	private static Color TeamBadgeBgColor(byte team) => team switch
	{
		(byte)Team.Team1 => new Color(0.20f, 0.40f, 0.65f, 0.85f),
		(byte)Team.Team2 => new Color(0.65f, 0.40f, 0.18f, 0.85f),
		_ => new Color(0.30f, 0.30f, 0.32f, 0.85f),
	};
}
