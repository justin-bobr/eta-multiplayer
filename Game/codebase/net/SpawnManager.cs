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

/// <summary>Display strings + visual tints per team. Single source of truth for UI code (Scoreboard,
/// TeamSelectionMenu, KillFeed).</summary>
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

/// <summary>Spawn-marker management for round-based modes (CT vs T) and Deathmatch. On map load it reads the
/// active map's <see cref="Level"/> registry and buckets each <see cref="Spawn"/> by <see cref="Spawn.SpawnKind"/>.
/// Mapper convention: add <see cref="Spawn"/> nodes (set Kind + Size), list them in <see cref="Level.SpawnPaths"/>,
/// provide ~4-10 per team. Falls back to a hard-coded mid-map position when the map has no spawns.</summary>
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

	/// <summary>Reads the spawns from the active map's <see cref="Level"/> registry; idempotent and safe
	/// to re-call on map reload. The <paramref name="tree"/> argument is kept for call-site compatibility
	/// but is no longer walked — spawns come from <see cref="World.Level"/>.</summary>
	public void Scan(SceneTree tree)
	{
		_ctSpawns.Clear();
		_tSpawns.Clear();
		_dmSpawns.Clear();
		_ctRotator = 0;
		_tRotator = 0;
		_dmRotator = 0;

		// Spawn extends Zone (Area3D) — we capture the centre + yaw as the spawn pose. Multiple
		// players in the same Spawn area get de-clumped by the FreeRadius retry inside PickFromList.
		var level = World.Level;
		if (level != null)
		{
			foreach (var sp in level.SpawnsForKind(Spawn.SpawnKind.Team1)) _ctSpawns.Add(AreaToPoint(sp));
			foreach (var sp in level.SpawnsForKind(Spawn.SpawnKind.Team2)) _tSpawns.Add(AreaToPoint(sp));
			foreach (var sp in level.SpawnsForKind(Spawn.SpawnKind.Deathmatch)) _dmSpawns.Add(AreaToPoint(sp));
		}

		Initialized = true;
		Dbg.Print($"[SpawnManager] Scan: {_ctSpawns.Count} CT, {_tSpawns.Count} T, {_dmSpawns.Count} DM spawns found");
		if (_ctSpawns.Count == 0 && _tSpawns.Count == 0 && _dmSpawns.Count == 0)
			GD.PushWarning("[SpawnManager] No spawns in Level — falling back to DefaultPos. Add Spawn nodes to the map and list them in Level.SpawnPaths.");
	}

	/// <summary>Converts a Spawn (Area3D) into a SpawnPoint using the area's centre and yaw.</summary>
	private static SpawnPoint AreaToPoint(Spawn s) =>
		new() { Pos = s.GlobalPosition, Yaw = s.GlobalRotation.Y };

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
