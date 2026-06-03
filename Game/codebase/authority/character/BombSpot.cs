using Godot;

/// <summary>
/// A named bomb plant region (CS-style A / B / C). Extends <see cref="Zone"/> so it inherits the
/// Size, BoxShape, group membership, and the area-AABB pattern — plus a <see cref="Slot"/>
/// dropdown that tags which plant this is.
///
/// Two roles:
///   1. **HUD navigation**: <see cref="HudCs2"/> resolves <see cref="Map.BombSpotForSlot"/> for
///      each slot and feeds the world centre to <see cref="HudCompass"/>, which draws the A /
///      B / C diamond on the compass strip pointing toward each spot.
///   2. **Bot navigation**: BombSpot centres are part of the long-range target candidate pool
///      passed to <see cref="BotController"/> — bots occasionally pick a plant region and walk
///      there via <see cref="NavigationServer3D.MapGetPath"/>.
///
/// No group membership: identification is done purely through the <see cref="Slot"/> dropdown
/// (the mapper picks A / B / C in the inspector) and the runtime <see cref="Map"/> cache.
/// Lookups go via <see cref="Map.BombSpotForSlot"/>, not <see cref="SceneTree.GetNodesInGroup"/>.
///
/// The shape + gizmo / collision config comes from <see cref="Zone"/>: detect-only Area3D with
/// CollisionLayer = 0, Monitoring on, Monitorable off, default Mask = 2 for player bodies.
/// </summary>
[Tool, GlobalClass]
public partial class BombSpot : Zone
{
	public enum BombSlot { A, B, C }

	/// <summary>Which plant slot this spot represents. Dropdown in the inspector — picks the
	/// letter the compass diamond + UI label use for this spot. The map's <see cref="Map"/>
	/// cache scans by type so the runtime side resolves a slot to its BombSpot without any
	/// group plumbing.</summary>
	[Export] public BombSlot Slot { get; set; } = BombSlot.A;
}
