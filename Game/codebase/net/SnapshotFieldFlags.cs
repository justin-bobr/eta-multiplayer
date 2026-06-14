/// <summary>Per-player field mask for delta-baseline snapshot compression. Bit = 1 sends that field group,
/// 0 keeps the baseline value. Groups bundle fields that change together (Pos+Vel, Yaw+Pitch, …).
/// <see cref="All"/> = full snapshot (player absent from the baseline). 13 bits fit a ushort.</summary>
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
