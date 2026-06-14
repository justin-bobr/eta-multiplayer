/// <summary>Disconnect reason for <see cref="PacketType.PlayerLeft"/>.</summary>
public enum LeaveReason : byte
{
	Quit = 0,
	Timeout = 1,
	Kicked = 2,
	ServerShutdown = 3,
}
