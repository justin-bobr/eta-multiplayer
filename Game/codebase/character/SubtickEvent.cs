namespace Vantix.Character;

/// <summary>An input change at a fractional position within a tick, for subtick movement replay.</summary>
public struct SubtickEvent
{
	/// <summary>Position inside the tick, 0..1 = tick-start..tick-end. Events must be sorted ascending.</summary>
	public float TFraction;

	/// <summary>Full held-state bitmask AFTER this event applies (the bits the player is holding from this
	/// instant onward until the next event).</summary>
	public InputBits StateAfter;

	/// <summary>View yaw at this event, used for the substep starting here.</summary>
	public float ViewYaw;

	/// <summary>View pitch at this event.</summary>
	public float ViewPitch;
}
