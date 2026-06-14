namespace Vantix.Character;

/// <summary>
/// Held-input bitfield for subtick movement. Each bit is set while the corresponding key is down. Press-edges
/// (Jump/Crouch/Fire/Reload/Inspect) are detected by the driver from the 0→1 transition between consecutive
/// <see cref="SubtickEvent.StateAfter"/> masks, so there is no separate "pressed" bit.
/// </summary>
[System.Flags]
public enum InputBits : ushort
{
	None = 0,
	Forward = 1 << 0,
	Back = 1 << 1,
	Left = 1 << 2,
	Right = 1 << 3,
	Jump = 1 << 4,
	Crouch = 1 << 5,
	Sprint = 1 << 6,
	ShiftWalk = 1 << 7,
	Fire = 1 << 8,
	Ads = 1 << 9,
	Reload = 1 << 10,
	Inspect = 1 << 11,
	BreathHold = 1 << 12,
}
