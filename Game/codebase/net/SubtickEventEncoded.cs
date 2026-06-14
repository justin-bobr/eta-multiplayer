namespace Vantix.Net;

/// <summary>Wire-quantised form of a <see cref="Vantix.Character.SubtickEvent"/>.</summary>
public struct SubtickEventEncoded
{
	public byte TQ;
	public ushort StateAfter;
	public ushort QYaw;
	public ushort QPitch;
}
