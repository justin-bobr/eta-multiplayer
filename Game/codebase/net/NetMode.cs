namespace Vantix.Net;

/// <summary>Mode the game instance runs in — set from the command line (see <see cref="NetCli.Parse"/>).</summary>
public enum NetMode
{
	/// <summary>Server plus local client in the same process — dev shortcut for editor play.</summary>
	Listen,

	/// <summary>Client only. Boots into the main menu unless <see cref="NetCli.AutoConnect"/>
	/// (via <c>--connect HOST:PORT</c>) connects directly to <see cref="NetCli.Host"/>:<see cref="NetCli.Port"/>.</summary>
	Client,

	/// <summary>Dedicated headless server.</summary>
	Server,
}
