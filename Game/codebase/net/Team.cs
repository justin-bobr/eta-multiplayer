namespace Vantix.Server;

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
