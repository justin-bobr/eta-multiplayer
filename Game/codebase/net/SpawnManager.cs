using Godot;
using System.Collections.Generic;

/// <summary>Which spawn pool a player uses. Enum byte values are stable wire-format — do not
/// renumber. Display names live in <see cref="Teams"/>.</summary>
public enum Team : byte
{
	/// <summary>Team 1 — lore-name "VEKTOR". Spawn-marker group "spawn_team1".</summary>
	Team1 = 0,
	/// <summary>Team 2 — lore-name "ATLAS-9". Spawn-marker group "spawn_team2".</summary>
	Team2 = 1,
	/// <summary>Deathmatch / Free-for-All — marker group "spawn_deathmatch".</summary>
	Deathmatch = 2,
	/// <summary>Initial state in competitive mode while the player is choosing a team. No spawn pose
	/// is assigned, the LocalPlayer is not instantiated, and the client cycles through preview cameras.
	/// Switches to Team1/Team2 via <see cref="PacketType.TeamSelect"/> after which the server replies
	/// with <see cref="PacketType.SpawnAuthorize"/> carrying the real spawn pose.</summary>
	Spectator = 3,
}

/// <summary>Display strings + visual tints for each team. Single source of truth — UI code
/// (Scoreboard, TeamSelectionMenu, KillFeed) reads from here instead of hardcoding "VEKTOR"/"ATLAS-9".
/// Lore names from the project README: VEKTOR is the licensed PMC contracted by clients, ATLAS-9
/// the deniable strike network.</summary>
public static class Teams
{
	public const string Team1Name = "VEKTOR";
	public const string Team2Name = "ATLAS-9";
	public const string DeathmatchName = "DEATHMATCH";
	public const string SpectatorName = "SPECTATOR";

	public static readonly Color Team1Color = new(0.30f, 0.60f, 1.00f);
	public static readonly Color Team2Color = new(1.00f, 0.65f, 0.20f);

	public static string DisplayName(Team t) => t switch
	{
		Team.Team1 => Team1Name,
		Team.Team2 => Team2Name,
		Team.Deathmatch => DeathmatchName,
		Team.Spectator => SpectatorName,
		_ => t.ToString(),
	};

	public static Color DisplayColor(Team t) => t switch
	{
		Team.Team1 => Team1Color,
		Team.Team2 => Team2Color,
		_ => Colors.White,
	};
}

/// <summary>
/// Spawn-marker management for round-based modes (CT vs T) and Deathmatch. On map load it scans
/// all <see cref="Marker3D"/> nodes in the Godot groups <c>spawn_ct</c>, <c>spawn_t</c>, and
/// <c>spawn_deathmatch</c>.
///
/// Mapper convention:
///   1. In the Godot editor, right-click → Add Child → <see cref="Marker3D"/>.
///   2. Name it e.g. "spawn_ct_01", "spawn_t_03", "spawn_dm_05" (the name itself is free-form).
///   3. In the Inspector add the marker to one of the groups "spawn_ct" / "spawn_t" / "spawn_deathmatch".
///   4. Adjust position and Y-rotation; the player spawns with this pose.
///   5. Provide at least 4 to 10 markers per team to avoid over-dense spawns.
///
/// Fallback: when no group markers are present, nodes named "spawn_*" are sorted by name suffix
/// (_t, _ct, _dm). If no markers exist at all, a hard-coded default position (mid-map) is used.
/// </summary>
public class SpawnManager
{
	/// <summary>One spawn marker resolved to position and yaw.</summary>
	private struct SpawnPoint { public Vector3 Pos; public float Yaw; }

	private readonly List<SpawnPoint> _ctSpawns = new();
	private readonly List<SpawnPoint> _tSpawns = new();
	private readonly List<SpawnPoint> _dmSpawns = new();
	private int _ctRotator;
	private int _tRotator;
	private int _dmRotator;

	public bool Initialized { get; private set; }
	public int CtCount => _ctSpawns.Count;
	public int TCount => _tSpawns.Count;
	public int DmCount => _dmSpawns.Count;

	/// <summary>Default spawn position used when the map has no markers at all.</summary>
	public static readonly Vector3 DefaultPos = new(9.857169f, 1.0f, 2.1423106f);

	/// <summary>Minimum distance (metres) to an already occupied spawn before a slot is considered free.</summary>
	public const float FreeRadius = 1.0f;

	/// <summary>Scans the scene tree once for spawn markers; idempotent and safe to re-call on map reload.</summary>
	public void Scan(SceneTree tree)
	{
		_ctSpawns.Clear();
		_tSpawns.Clear();
		_dmSpawns.Clear();
		_wanderTargets = null;
		_ctRotator = 0;
		_tRotator = 0;
		_dmRotator = 0;

		// Accept both the new "spawn_team1"/"spawn_team2" names AND the legacy "spawn_ct"/"spawn_t"
		// so existing maps don't need an editor pass. New maps should use the team1/team2 names.
		foreach (var n in tree.GetNodesInGroup("spawn_team1"))
			if (n is Marker3D m) _ctSpawns.Add(MarkerToPoint(m));
		foreach (var n in tree.GetNodesInGroup("spawn_ct"))
			if (n is Marker3D m) _ctSpawns.Add(MarkerToPoint(m));
		foreach (var n in tree.GetNodesInGroup("spawn_team2"))
			if (n is Marker3D m) _tSpawns.Add(MarkerToPoint(m));
		foreach (var n in tree.GetNodesInGroup("spawn_t"))
			if (n is Marker3D m) _tSpawns.Add(MarkerToPoint(m));
		foreach (var n in tree.GetNodesInGroup("spawn_deathmatch"))
			if (n is Marker3D m) _dmSpawns.Add(MarkerToPoint(m));

		if (_ctSpawns.Count == 0 && _tSpawns.Count == 0 && _dmSpawns.Count == 0 && tree.CurrentScene != null)
			ScanFallbackByName(tree.CurrentScene);

		Initialized = true;
		Dbg.Print($"[SpawnManager] Scan: {_ctSpawns.Count} CT, {_tSpawns.Count} T, {_dmSpawns.Count} DM spawns found");
		if (_ctSpawns.Count == 0 && _tSpawns.Count == 0 && _dmSpawns.Count == 0)
			GD.PushWarning("[SpawnManager] NO markers found — falling back to DefaultPos. Place Marker3D nodes in group 'spawn_ct'/'spawn_t'/'spawn_deathmatch' in the map.");
	}

	/// <summary>Cached wander-target list for bot AI: all spawn marker positions (CT + T + DM)
	/// collapsed into a single Vector3[] so a bot can pick a random one as its current goal.
	/// Built lazily on first access, invalidated on <see cref="Scan"/>. Returns an empty array
	/// when no markers are defined (the BotController then falls back to standing still).</summary>
	private Vector3[] _wanderTargets;
	public System.Collections.Generic.IReadOnlyList<Vector3> WanderTargets
	{
		get
		{
			if (_wanderTargets != null) return _wanderTargets;
			var arr = new Vector3[_ctSpawns.Count + _tSpawns.Count + _dmSpawns.Count];
			int i = 0;
			foreach (var s in _ctSpawns) arr[i++] = s.Pos;
			foreach (var s in _tSpawns) arr[i++] = s.Pos;
			foreach (var s in _dmSpawns) arr[i++] = s.Pos;
			_wanderTargets = arr;
			return _wanderTargets;
		}
	}

	/// <summary>Converts a Marker3D into a SpawnPoint capturing its global position and Y rotation.</summary>
	private static SpawnPoint MarkerToPoint(Marker3D m) =>
		new() { Pos = m.GlobalPosition, Yaw = m.GlobalRotation.Y };

	/// <summary>Recursive fallback scan that sorts spawn_* markers by name suffix into the appropriate pool.</summary>
	private void ScanFallbackByName(Node root)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is Marker3D m && m.Name.ToString().StartsWith("spawn_"))
			{
				string n = m.Name.ToString().ToLowerInvariant();
				if (n.Contains("_dm") || n.Contains("deathmatch")) _dmSpawns.Add(MarkerToPoint(m));
				else if (n.Contains("_t_") || n.EndsWith("_t")) _tSpawns.Add(MarkerToPoint(m));
				else _ctSpawns.Add(MarkerToPoint(m));
			}
			ScanFallbackByName(child);
		}
	}

	/// <summary>Picks a free spawn slot for the requested team, falling back to the other team, Deathmatch, or DefaultPos.</summary>
	public (Vector3 pos, float yaw) PickFreeSpawn(Team team, IReadOnlyList<Vector3> occupied)
	{
		switch (team)
		{
			case Team.Team1:
				if (_ctSpawns.Count > 0) return PickFromList(_ctSpawns, ref _ctRotator, occupied);
				break;
			case Team.Team2:
				if (_tSpawns.Count > 0) return PickFromList(_tSpawns, ref _tRotator, occupied);
				break;
			case Team.Deathmatch:
				if (_dmSpawns.Count > 0) return PickFromList(_dmSpawns, ref _dmRotator, occupied);
				break;
		}
		if (_dmSpawns.Count > 0) return PickFromList(_dmSpawns, ref _dmRotator, occupied);
		if (_ctSpawns.Count > 0) return PickFromList(_ctSpawns, ref _ctRotator, occupied);
		if (_tSpawns.Count > 0)  return PickFromList(_tSpawns,  ref _tRotator,  occupied);
		return (DefaultPos, 0f);
	}

	/// <summary>Rotates through the list and returns the first slot passing the FreeRadius check; falls back to a rotating slot if all are occupied.</summary>
	private static (Vector3 pos, float yaw) PickFromList(List<SpawnPoint> list, ref int rotator, IReadOnlyList<Vector3> occupied)
	{
		for (int attempt = 0; attempt < list.Count; attempt++)
		{
			int idx = (rotator + attempt) % list.Count;
			var pt = list[idx];
			if (IsFree(pt.Pos, occupied))
			{
				rotator = (idx + 1) % list.Count;
				return (pt.Pos, pt.Yaw);
			}
		}
		var fb = list[rotator % list.Count];
		rotator = (rotator + 1) % list.Count;
		return (fb.Pos, fb.Yaw);
	}

	/// <summary>Returns true when no occupied position lies within FreeRadius of the candidate slot.</summary>
	private static bool IsFree(Vector3 pos, IReadOnlyList<Vector3> occupied)
	{
		float r2 = FreeRadius * FreeRadius;
		foreach (var o in occupied)
			if (pos.DistanceSquaredTo(o) < r2) return false;
		return true;
	}
}

/// <summary>
/// Active game mode — determines which spawn pool is used. A future round-manager system selects
/// the mode per match.
/// </summary>
public enum GameMode : byte
{
	/// <summary>Round-based CT vs T. Joiners are assigned alternately.</summary>
	Competitive = 0,
	/// <summary>Free-for-all — everyone uses the Deathmatch pool.</summary>
	Deathmatch = 1,
}
