using Godot;

namespace Vantix.Hud;

/// <summary>
/// Team-select overlay shown while in <see cref="Team.Spectator"/> during the competitive handshake:
/// Team1/Team2 buttons plus live rosters from <see cref="NetClient.RemotePlayers"/>, over a blurred
/// back-buffer. Clicking a side sends <see cref="PacketType.TeamSelect"/>; auto-removes when
/// <see cref="NetClient.SpawnAuthorized"/> flips true.
/// </summary>
public partial class TeamSelectionMenu : CanvasLayer
{
	[Export] public int LayerOrder = 250;

	private Control _root;
	private Button _team1Btn;
	private Button _team2Btn;
	private Button _spectateBtn;
	private Label _statusLabel;
	private VBoxContainer _team1List;
	private VBoxContainer _team2List;
	private VBoxContainer _spectatorList;
	private Label _team1Header;
	private Label _team2Header;
	private Label _spectatorHeader;
	private Container _spectatorWrapper;
	private bool _selectionSent;
	private Input.MouseModeEnum _previousMouseMode;
	private float _rosterRefreshAccum;
	private const float RosterRefreshSec = 2.0f;
	private System.Action<InitialPlayerState> _onJoinedHandler;
	private System.Action<byte, LeaveReason> _onLeftHandler;

	public override void _Ready()
	{
		Layer = LayerOrder;
		ProcessMode = ProcessModeEnum.Always;
		BuildUi();
		_previousMouseMode = Input.MouseMode;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		// Subscribe to NetClient join/left events for instant roster updates instead of waiting
		// for the 2s polling tick. Polling still runs as a safety net (in case an event is missed
		// during the brief window between menu construction and event subscription).
		var client = NetMain.Instance?.Client;
		if (client != null)
		{
			_onJoinedHandler = _ => RefreshRosters();
			_onLeftHandler = (_, _) => RefreshRosters();
			client.OnPlayerJoined += _onJoinedHandler;
			client.OnPlayerLeft += _onLeftHandler;
		}
		RefreshRosters();
	}

	public override void _ExitTree()
	{
		var client = NetMain.Instance?.Client;
		if (client != null)
		{
			if (_onJoinedHandler != null)
				client.OnPlayerJoined -= _onJoinedHandler;
			if (_onLeftHandler != null)
				client.OnPlayerLeft -= _onLeftHandler;
		}
	}

	public override void _Process(double delta)
	{
		var client = NetMain.Instance?.Client;
		if (client == null)
			return;
		if (client.SpawnAuthorized)
		{
			Input.MouseMode = _previousMouseMode == Input.MouseModeEnum.Visible
				? Input.MouseModeEnum.Captured
				: _previousMouseMode;
			QueueFree();
			return;
		}
		_rosterRefreshAccum += (float)delta;
		if (_rosterRefreshAccum >= RosterRefreshSec)
		{
			_rosterRefreshAccum = 0f;
			RefreshRosters();
		}
	}

	private void BuildUi()
	{
		_root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		// Background: gaussian blur of the back buffer + dark tint. Defocuses the preview-camera view.
		var blur = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
		blur.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		var shader = GD.Load<Shader>("res://codebase/hud/team_select_blur.gdshader");
		if (shader != null)
		{
			var mat = new ShaderMaterial { Shader = shader };
			mat.SetShaderParameter("mip_level", 0.5f);
			mat.SetShaderParameter("tint", new Color(0f, 0f, 0f, 0.40f));
			blur.Material = mat;
		}
		else
		{
			blur.Color = new Color(0f, 0f, 0f, 0.55f);
		}
		_root.AddChild(blur);

		// Three-column grid: left roster | centre buttons | right roster.
		var grid = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		grid.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		grid.AddThemeConstantOverride("separation", 32);
		_root.AddChild(grid);

		_team1List = BuildRosterColumn(out _team1Header, Teams.Team1Name, Teams.Team1Color);
		grid.AddChild(_team1List.GetParent<Container>());

		var centre = new CenterContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		grid.AddChild(centre);

		var centreCol = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		centreCol.AddThemeConstantOverride("separation", 20);
		centre.AddChild(centreCol);

		var title = new Label { Text = "CHOOSE YOUR TEAM", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 30);
		title.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.95f));
		centreCol.AddChild(title);

		var btnRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		btnRow.AddThemeConstantOverride("separation", 24);
		centreCol.AddChild(btnRow);

		_team1Btn = MakeTeamButton(Teams.Team1Name, Teams.Team1Color);
		_team1Btn.Pressed += () => OnTeamPressed(Team.Team1);
		btnRow.AddChild(_team1Btn);

		_team2Btn = MakeTeamButton(Teams.Team2Name, Teams.Team2Color);
		_team2Btn.Pressed += () => OnTeamPressed(Team.Team2);
		btnRow.AddChild(_team2Btn);

		// Small "Spectate" button below the two team buttons — keeps the user in preview-cam
		// mode without spawning a LocalPlayer. Useful for join-as-spectator (= watch others play).
		_spectateBtn = new Button
		{
			Text = "Spectate only",
			CustomMinimumSize = new Vector2(0, 36),
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_spectateBtn.AddThemeFontSizeOverride("font_size", 14);
		_spectateBtn.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
		var spectateSb = new StyleBoxFlat
		{
			BgColor = new Color(0.20f, 0.20f, 0.22f, 0.85f),
			BorderColor = new Color(0.45f, 0.45f, 0.48f, 0.9f),
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ContentMarginLeft = 14,
			ContentMarginRight = 14,
		};
		_spectateBtn.AddThemeStyleboxOverride("normal", spectateSb);
		_spectateBtn.Pressed += OnSpectatePressed;
		centreCol.AddChild(_spectateBtn);

		_statusLabel = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		_statusLabel.AddThemeFontSizeOverride("font_size", 14);
		_statusLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.7f));
		centreCol.AddChild(_statusLabel);

		// Spectator section sits below the team-buttons in the centre column. Hidden when empty so
		// the layout doesn't reserve a gap. Header label tracks the count, list shows centred names.
		_spectatorWrapper = BuildSpectatorSection(out _spectatorHeader, out _spectatorList);
		centreCol.AddChild(_spectatorWrapper);

		_team2List = BuildRosterColumn(out _team2Header, Teams.Team2Name, Teams.Team2Color);
		grid.AddChild(_team2List.GetParent<Container>());
	}

	private Container BuildSpectatorSection(out Label header, out VBoxContainer list)
	{
		var outer = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
		};
		outer.AddThemeConstantOverride("separation", 4);

		header = new Label
		{
			Text = $"{Teams.SpectatorName} (0)",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		header.AddThemeFontSizeOverride("font_size", 14);
		header.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f, 0.85f));
		outer.AddChild(header);

		list = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
		};
		list.AddThemeConstantOverride("separation", 2);
		outer.AddChild(list);
		return outer;
	}

	/// <summary>Builds one side's roster column (header + VBox); returns the inner VBox for populating entries.</summary>
	private VBoxContainer BuildRosterColumn(out Label header, string teamName, Color teamTint)
	{
		var outer = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(220, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		outer.AddThemeConstantOverride("separation", 6);

		header = new Label
		{
			Text = $"{teamName} (0)",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		header.AddThemeFontSizeOverride("font_size", 18);
		header.AddThemeColorOverride("font_color", teamTint);
		outer.AddChild(header);

		var sep = new HSeparator();
		outer.AddChild(sep);

		var list = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 2);
		outer.AddChild(list);
		return list;
	}

	private Button MakeTeamButton(string label, Color tint)
	{
		var b = new Button
		{
			Text = label,
			CustomMinimumSize = new Vector2(220, 90),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		b.AddThemeFontSizeOverride("font_size", 22);
		var sb = new StyleBoxFlat
		{
			BgColor = tint with { A = 0.80f },
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
		};
		b.AddThemeStyleboxOverride("normal", sb);
		var hover = new StyleBoxFlat
		{
			BgColor = tint with { A = 1.0f },
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
		};
		b.AddThemeStyleboxOverride("hover", hover);
		return b;
	}

	/// <summary>Rebuilds the Team1/Team2/Spectator lists from NetClient.RemotePlayers (event-driven + 2s polling).</summary>
	private void RefreshRosters()
	{
		_team1List.QueueFreeChildren();
		_team2List.QueueFreeChildren();
		_spectatorList.QueueFreeChildren();
		int t1 = 0, t2 = 0, sp = 0;
		var client = NetMain.Instance?.Client;
		if (client != null)
		{
			foreach (var kv in client.RemotePlayers)
			{
				var p = kv.Value;
				switch ((Team)p.Team)
				{
					case Team.Team1:
						AddRosterEntry(_team1List, p.PlayerName, p.NetId, false);
						t1++;
						break;
					case Team.Team2:
						AddRosterEntry(_team2List, p.PlayerName, p.NetId, false);
						t2++;
						break;
					case Team.Spectator:
						AddRosterEntry(_spectatorList, p.PlayerName, p.NetId, true);
						sp++;
						break;
				}
			}
			// Add LocalPlayer entry (we're not in RemotePlayers — that's only OTHER players).
			// Show as Spectator since by definition the local user is in the team-select phase.
			AddRosterEntry(_spectatorList, $"{_cli?.PlayerName ?? "You"} (you)", client.OwnNetId, true);
			sp++;
		}
		_team1Header.Text = $"{Teams.Team1Name} ({t1})";
		_team2Header.Text = $"{Teams.Team2Name} ({t2})";
		_spectatorHeader.Text = $"{Teams.SpectatorName} ({sp})";
		_spectatorWrapper.Visible = sp > 0;
	}

	private NetCli _cli => NetMain.Instance?.Cli;

	private void AddRosterEntry(VBoxContainer list, string playerName, byte netId, bool centered)
	{
		var row = new HBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = centered ? Control.SizeFlags.ShrinkCenter : Control.SizeFlags.Fill,
		};
		row.AddThemeConstantOverride("separation", 8);
		// Tiny color square as a stand-in for a future avatar; netId-derived so it's deterministic.
		var sq = new ColorRect
		{
			CustomMinimumSize = new Vector2(18, 18),
			Color = PuppetPlayer.PlayerColor(netId),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		row.AddChild(sq);
		var lbl = new Label { Text = string.IsNullOrEmpty(playerName) ? $"Player_{netId}" : playerName };
		lbl.AddThemeFontSizeOverride("font_size", 14);
		lbl.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.9f));
		row.AddChild(lbl);
		list.AddChild(row);
	}

	private void OnTeamPressed(Team team)
	{
		if (_selectionSent)
			return;
		_selectionSent = true;
		_team1Btn.Disabled = true;
		_team2Btn.Disabled = true;
		if (_spectateBtn != null)
			_spectateBtn.Disabled = true;
		_statusLabel.Text = $"Waiting for spawn ({Teams.DisplayName(team)})…";
		NetMain.Instance?.Client?.SendTeamSelect(team);
	}

	/// <summary>Closes the menu without a TeamSelect packet: stays in Spectator preview-cam mode, no LocalPlayer spawns.</summary>
	private void OnSpectatePressed()
	{
		if (_selectionSent)
			return;
		_selectionSent = true;
		Input.MouseMode = Input.MouseModeEnum.Captured;
		Dbg.Print("[TeamSelectionMenu] User chose Spectate — staying in preview-cam mode, no LocalPlayer will spawn");
		QueueFree();
	}
}

/// <summary>Helper to queue-free all children of a container in one call.</summary>
internal static class TeamSelectionMenuExtensions
{
	public static void QueueFreeChildren(this Node n)
	{
		foreach (Node c in n.GetChildren())
			c.QueueFree();
	}
}
