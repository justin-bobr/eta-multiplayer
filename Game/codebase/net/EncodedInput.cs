/// <summary>Pre-quantised form of a client-produced input frame for NetClient's redundancy ring. Quantising
/// happens once on push, not per send (3× at full redundancy) — saves ~5 µs CPU per tick.</summary>
public struct EncodedInput
{
	public uint TickIndex;
	public ushort QYaw;
	public ushort QPitch;
	public short QWishX;
	public short QWishZ;
	public byte Flags1;
	public byte Flags2;
	/// <summary>Sub-tick fire-press offset (0..255 → 0..0.996 of a tick). Only meaningful when <see cref="Flags1"/>
	/// bit 7 (firePressed) is set; otherwise 0. Captures the wallclock fraction at which the fire-press edge
	/// occurred within the client's tick, so the server can rewind lag-comp to a fractional tick.</summary>
	public byte FireSubTick;
	/// <summary>InputBits at the start of the tick (state at t=0). Seeds the server's subtick replay before the
	/// first event. 0 on legacy non-subtick paths.</summary>
	public ushort InitialBits;
	public ushort QInitialYaw;
	public ushort QInitialPitch;
	/// <summary>Number of valid entries in <see cref="Events"/>. 0 = no subtick events this tick → server takes
	/// the legacy single-segment path. Capped at <see cref="Packets.MaxSubtickEventsWire"/>.</summary>
	public byte EventCount;
	/// <summary>Subtick event array, length == EventCount. Null when EventCount = 0 (most ticks).</summary>
	public SubtickEventEncoded[] Events;
}
