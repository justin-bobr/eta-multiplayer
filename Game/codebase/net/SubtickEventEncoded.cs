/// <summary>Pre-quantised form of a single subtick event — 7 bytes on the wire. TQ = TFraction × 256
/// (clamped 0..255), StateAfter = the InputBits bitmask after this event, QYaw/QPitch = the view at this
/// event, quantised the same way as the top-level fields.</summary>
public struct SubtickEventEncoded
{
	public byte TQ;
	public ushort StateAfter;
	public ushort QYaw;
	public ushort QPitch;
}
