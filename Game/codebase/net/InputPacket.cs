namespace Vantix.Net;

public struct InputPacket
{
	public uint TickIndex;
	/// <summary>Subtick events ordered by TFraction ascending. Null/empty for tick-quantised inputs.</summary>
	public SubtickEvent[] Events;
	public ushort InitialBits;
	public float InitialViewYaw;
	public float InitialViewPitch;
	public float ViewYaw;
	public float ViewPitch;
	public float WishX;
	public float WishZ;
	public bool SprintHeld, ShiftHeld, CrouchHeld, CrouchPressed, AdsHeld, BreathHoldHeld, JumpPressed, FirePressed;
	public bool ReloadPressed, InspectPressed, SlotIsGrenade;
	/// <summary>Sub-tick offset of the fire-press edge (0..255 → 0..0.996 of a tick). Only meaningful when
	/// <see cref="FirePressed"/> is true. Server adds <c>FireSubTick / 256f</c> to the lag-comp rewind tick.</summary>
	public byte FireSubTick;
}
