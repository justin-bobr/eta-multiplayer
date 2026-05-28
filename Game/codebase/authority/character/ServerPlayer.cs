using Godot;

/// <summary>
/// Server-authority body for a real connected player. Instantiated from
/// <c>res://character/server_player.tscn</c> by <see cref="NetServer.EnsureServerAgent"/> as soon
/// as a peer finalizes its handshake.
///
/// Inherits the complete sim/movement/hitscan logic from <see cref="PlayerCore"/>; only sets the
/// mode flags (<c>IsServerAgent=true</c>, <c>IsLocalPlayer=false</c>) so the authority path is
/// taken. Each tick <see cref="ServerBaseCharacter.NetInputSource"/> is filled by NetServer with
/// the last received InputPacket; FixedTick deterministically simulates the resulting position.
/// Snapshots are sent from <see cref="ServerBaseCharacter.AuthorityPosition"/> plus
/// MovementController state.
///
/// On disconnect: moved into the reconnect pool with <see cref="ServerBaseCharacter.IsFrozen"/>=true
/// (pose remains for the grace period).
///
/// server_player.tscn statically contains no FPS stack (viewmodel_layer/fps_camera/wall_check/tps_camera)
/// — no camera, no SubViewport, no render overhead.
/// </summary>
public partial class ServerPlayer : PlayerCore
{
	/// <summary>Derived from the class type — ServerPlayer is always server-authority.</summary>
	public override bool IsServerAgent => true;
}
