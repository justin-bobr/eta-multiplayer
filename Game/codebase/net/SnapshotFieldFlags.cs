/// <summary>Per-player field mask for delta-baseline snapshot compression. Each bit either sends a field
/// group (bit = 1) or omits it (the value stays at the baseline). Groups follow the "changes together"
/// heuristic (Pos+Vel, Yaw+Pitch, the three blends, …). <see cref="All"/> emits like a full snapshot —
/// e.g. for a player that wasn't in the baseline (freshly joined / re-entered PVS). 13 bits fit a ushort.</summary>
[System.Flags]
public enum SnapshotFieldFlags : ushort
{
	None      = 0,
	Flags     = 1 << 0,
	Movement  = 1 << 1,  // Pos + Vel
	View      = 1 << 2,  // Yaw + Pitch
	Blends    = 1 << 3,  // AdsBlend + CrouchBlend + RaiseBlend
	ShotIndex = 1 << 4,
	Hp        = 1 << 5,
	Armor     = 1 << 6,
	Weapon    = 1 << 7,  // ActiveSlot + WeaponId
	AimPunch  = 1 << 8,  // AimPunchX + AimPunchY
	Footstep  = 1 << 9,
	Score     = 1 << 10, // Kills + Deaths
	Ping      = 1 << 11,
	Team      = 1 << 12, // Team + TeamSlot
	All       = (1 << 13) - 1,
}
