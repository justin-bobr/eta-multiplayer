namespace Vantix.Server;

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
