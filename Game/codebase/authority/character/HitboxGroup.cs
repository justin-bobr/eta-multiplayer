/// <summary>
/// Enum der Hitbox-Zonen für Damage-Routing. Wird im <see cref="Hitbox"/>-Node als [Export]-Feld
/// gewählt (Dropdown im Editor), beim <see cref="WeaponStats.Damages"/>-Dict als Key benutzt, und
/// im HitEvent-Packet als byte serialisiert (siehe <see cref="Packets.WriteHit"/>).
///
/// Neue Zonen anhängen — Werte NICHT umordnen weil sie wire-serialisiert sind (würde Protocol brechen).
/// </summary>
public enum HitboxGroup : byte
{
	Body = 0,    // Fallback / unspezifischer Treffer
	Head = 1,
	Chest = 2,
	Waist = 3,
	Arm = 4,
	Leg = 5,
	Hand = 6,
	Foot = 7,
}
