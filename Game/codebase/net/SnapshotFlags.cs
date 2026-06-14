/// <summary>Bit flags packed into <see cref="SnapshotPlayer.Flags"/>.</summary>
[System.Flags]
public enum SnapshotFlags : byte
{
	None           = 0,
	Sliding        = 1 << 0,
	Airborne       = 1 << 1,
	Reloading      = 1 << 2,
	Sprinting      = 1 << 3,
	WallClinging   = 1 << 4,
	Inspecting     = 1 << 5,
	/// <summary>Client finished world preloads (<see cref="PacketType.WorldInitComplete"/>); cleared on
	/// respawn/reconnect. Puppet TPS body stays hidden while unset.</summary>
	WorldReady     = 1 << 6,
	Dead           = 1 << 7,
}
