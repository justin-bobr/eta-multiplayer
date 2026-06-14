using Godot;

/// <summary>
/// A named bomb plant region (A/B/C), extending <see cref="Zone"/> and adding a <see cref="Slot"/> tag.
/// Used for HUD compass markers (via <see cref="Level.BombSpotForSlot"/>) and as bot navigation targets.
/// Resolved through the <see cref="Level"/> registry by slot, not by groups.
/// </summary>
[Tool, GlobalClass]
public partial class BombSpot : Zone
{
	public enum BombSlot { A, B, C }

	/// <summary>Plant slot (A/B/C) this spot represents; resolved via the <see cref="Level"/> registry.</summary>
	[Export] public BombSlot Slot { get; set; } = BombSlot.A;
}
