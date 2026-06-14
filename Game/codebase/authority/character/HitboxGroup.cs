/// <summary>
/// Hitbox zone enum for damage routing; keys <see cref="WeaponStats.Damages"/> and serialised as a byte
/// in HitEvent packets (see <see cref="Packets.WriteHit"/>).
/// Append-only — reordering values breaks the wire protocol.
/// </summary>
public enum HitboxGroup : byte
{
	Body = 0,
	Head = 1,
	Chest = 2,
	Waist = 3,
	Arm = 4,
	Leg = 5,
	Hand = 6,
	Foot = 7,
}
