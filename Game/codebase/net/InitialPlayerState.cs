using Godot;

/// <summary>Initial world state for a player — flows through SpawnAck + PlayerJoined.</summary>
public struct InitialPlayerState
{
	public byte NetId;
	public string PlayerName;
	public Vector3 Position;
	public float Yaw;
	public byte Hp;
	public byte ActiveSlot;
	public byte WeaponId;
	/// <summary>Cast of <see cref="Team"/>. Lets puppets show team-glow on the first frame without waiting
	/// for the first snapshot.</summary>
	public byte Team;
	/// <summary>See <see cref="SnapshotPlayer.TeamSlot"/>. Sent at join so the puppet shows the right colour
	/// before the first snapshot arrives.</summary>
	public byte TeamSlot;
}
