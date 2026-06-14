public struct WeaponButtons { public bool Fire, Reload, Inspect, Ads; }

public struct BufferedSubtickEvent
{
	public ulong Usec;
	public InputBits State;
	public float Yaw;
	public float Pitch;
}
