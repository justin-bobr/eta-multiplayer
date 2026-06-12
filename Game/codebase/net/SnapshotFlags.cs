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
	/// <summary>Player finished client-side world preloads and signalled <see cref="PacketType.WorldInitComplete"/>.
	/// Cleared on respawn / reconnect. The puppet TPS body is hidden while this is unset so a mid-loading
	/// player isn't shown on other peers.</summary>
	WorldReady     = 1 << 6,
	Dead           = 1 << 7,
}
