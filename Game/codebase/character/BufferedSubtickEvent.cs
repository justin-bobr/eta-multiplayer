namespace Vantix.Character;

/// <summary>An input-state edge buffered with its wall-clock timestamp for subtick replay.</summary>
public struct BufferedSubtickEvent
{
	public ulong Usec;
	public InputBits State;
	public float Yaw;
	public float Pitch;
}
