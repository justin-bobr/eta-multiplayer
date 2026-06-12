using Godot;

/// <summary>Per-player block in the snapshot packet — server-authoritative state for one tick.</summary>
public struct SnapshotPlayer
{
	public byte NetId;
	public byte Flags;
	public Vector3 Pos;
	public Vector3 Vel;
	public float Yaw;
	public float Pitch;
	public byte AdsBlend;
	public byte CrouchBlend;
	public byte RaiseBlend;
	public ushort ShotIndex;
	public byte Hp;
	/// <summary>Kevlar 0..50. Consumed without regen; headshots bypass it.</summary>
	public byte Armor;
	public byte ActiveSlot;
	public byte WeaponId;
	public sbyte AimPunchX;
	public sbyte AimPunchY;
	public ushort FootstepPhase;
	public byte Kills;
	public byte Deaths;
	public byte PingMs;
	/// <summary>Server-broadcast team (cast of <see cref="Team"/>). Drives puppet team-glow + scoreboard
	/// colour. None=0/CT=1/T=2/Deathmatch=3.</summary>
	public byte Team;
	/// <summary>Persistent index within the player's team (0..15), assigned at register time, stable for the
	/// session. Drives the per-player colour (palette[teamSlot]); unique within a team.</summary>
	public byte TeamSlot;
}
