/// <summary>Per-tick weapon action buttons (raw, ungated). Provided by the driver: <see cref="LocalPlayer"/>
/// reads Godot input, <see cref="ServerPlayer"/> reads the replicated packet. Base = neutral (puppets never tick).</summary>
public struct WeaponButtons { public bool Fire, Reload, Inspect, Ads; }

/// <summary>A sub-tick input event captured by the local client between two ticks: the microsecond
/// timestamp, the input bit-state at that instant, and the view angles — used to reconstruct the in-tick
/// ordering of fire presses for sub-tick-precise hit registration.</summary>
public struct BufferedSubtickEvent
{
	public ulong Usec;
	public InputBits State;
	public float Yaw;
	public float Pitch;
}
