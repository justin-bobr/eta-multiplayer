using Godot;

namespace Vantix.Levels;

/// <summary>
/// A respawn region extending <see cref="Zone"/>; players land at the area centre (or a sampled cell
/// when several spawn together). The <see cref="Kind"/> tag selects the mode/team pool, resolved by
/// <see cref="SpawnManager"/> via <see cref="Level.SpawnsForKind"/>.
/// </summary>
[Tool, GlobalClass]
public partial class Spawn : Zone
{
	/// <summary>Spawn pool (deathmatch / team 1 / team 2) this region belongs to.</summary>
	public enum SpawnKind { Deathmatch, Team1, Team2 }

	/// <summary>Spawn pool (Deathmatch/Team1/Team2) this region belongs to; resolved via <see cref="Level.SpawnsForKind"/>.</summary>
	[Export] public SpawnKind Kind { get; set; } = SpawnKind.Deathmatch;
}
