using Godot;

public struct InitialPlayerState
{
	public byte NetId;
	public string PlayerName;
	public Vector3 Position;
	public float Yaw;
	public byte Hp;
	public byte ActiveSlot;
	public byte WeaponId;
	/// <summary>Cast of <see cref="Team"/>; sent at join so puppets show team-glow before the first snapshot.</summary>
	public byte Team;
	/// <summary>See <see cref="SnapshotPlayer.TeamSlot"/>; sent at join for the right colour pre-snapshot.</summary>
	public byte TeamSlot;
}
