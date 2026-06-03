using Godot;

/// <summary>
/// A respawn region. Extends <see cref="Zone"/> so it inherits the Size + BoxShape + ShapeOwner
/// pattern — a player respawning here lands at the area's centre (or a sampled cell inside its
/// box when more than one player needs to spawn in the same region). The <see cref="Kind"/>
/// dropdown (Deathmatch / Team1 / Team2) tags which mode / team pool the spawn belongs to.
///
/// <see cref="SpawnManager"/> resolves spawns by Kind via <see cref="Map.SpawnsForKind"/> at
/// runtime. Spawns play no role in the bot navigation system — long-range bot targets come from
/// the <see cref="Zone"/> + <see cref="BombSpot"/> pool, not the spawn list, and the actual route
/// to those targets is computed by <see cref="NavigationServer3D"/> from the baked NavMesh.
/// </summary>
[Tool, GlobalClass]
public partial class Spawn : Zone
{
	public enum SpawnKind { Deathmatch, Team1, Team2 }

	/// <summary>Which spawn class this region belongs to. Dropdown in the inspector — picks the
	/// pool the spawn goes into (Deathmatch / Team1 / Team2). <see cref="Map.SpawnsForKind"/>
	/// resolves this at runtime so <see cref="SpawnManager"/> can pick a free slot.</summary>
	[Export] public SpawnKind Kind { get; set; } = SpawnKind.Deathmatch;
}
