using Godot;

/// <summary>Mode the game instance runs in — set from the command line (see <see cref="NetCli.Parse"/>).</summary>
public enum NetMode
{
	/// <summary>Server plus local client in the same process — dev shortcut for editor play.</summary>
	Listen,

	/// <summary>Client only. Boots into the main menu unless <see cref="NetCli.AutoConnect"/> is set
	/// (via <c>--connect HOST:PORT</c>), in which case it connects directly to
	/// <see cref="NetCli.Host"/>:<see cref="NetCli.Port"/>.</summary>
	Client,

	/// <summary>Dedicated headless server.</summary>
	Server,
}

/// <summary>
/// Parses command line arguments (Godot separates user args after "--"). Examples:
///   godot                                       Client mode → boots into main menu
///   godot -- --server                           Dedicated on 127.0.0.1:27015
///   godot -- --server --host 0.0.0.0 --port 28000   Dedicated on a custom address
///   godot -- --listen                           Listen server + local client (skip menu)
///   godot -- --connect 192.168.1.10             Client, auto-connect (skip menu), default port
///   godot -- --connect 10.0.0.5:28000           Client, auto-connect on a custom port
///
/// Additional flags: --max-players N (default 16), --bots N (default 0 = no bots), --name "...",
/// --tickrate 128, --reconnect-grace 600, --gamemode dm|competitive, --identity TOKEN.
/// </summary>
public class NetCli
{
	/// <summary>Default mode is <see cref="NetMode.Client"/> so any launch without explicit flags
	/// (editor F5 included) lands in the main menu. Use <c>--listen</c> for the dev shortcut that
	/// boots directly into a local server.</summary>
	public NetMode Mode = NetMode.Client;
	public string Host = "127.0.0.1";
	public int Port = 27015;

	/// <summary>True when <c>--connect HOST:PORT</c> was given. The client auto-connects and the main menu is skipped.</summary>
	public bool AutoConnect = false;
	public int MaxPlayers = 16;

	/// <summary>Number of bots the server auto-spawns (capped to free spawn markers). Default 0 = no bots.
	/// Settable via <c>--bots N</c>. Bots are replaced by real players (bot with lowest NetId despawns).</summary>
	public int MaxBots = 0;
	public string PlayerName = "Player";
	public int TickRate = 128;
	public float ReconnectGraceSec = 600f;
	public GameMode GameMode = GameMode.Competitive;

	/// <summary>Override for Settings.NetIdentityToken — used only for multi-client testing on one PC. Empty = use persisted token from user://settings.cfg.</summary>
	public string IdentityOverride = "";

	/// <summary>Parses command line arguments into a populated <see cref="NetCli"/> instance.</summary>
	public static NetCli Parse()
	{
		var cli = new NetCli();

		var combined = new System.Collections.Generic.List<string>();
		combined.AddRange(OS.GetCmdlineUserArgs());
		foreach (var a in OS.GetCmdlineArgs())
			if (a.StartsWith("--") && !combined.Contains(a))
				combined.Add(a);

		for (int i = 0; i < combined.Count; i++)
		{
			string arg = combined[i];
			string next = i + 1 < combined.Count ? combined[i + 1] : null;
			switch (arg)
			{
				case "--server":
					cli.Mode = NetMode.Server;
					break;
				case "--listen":
					cli.Mode = NetMode.Listen;
					break;
				case "--connect":
					cli.Mode = NetMode.Client;
					cli.AutoConnect = true;
					if (next != null && !next.StartsWith("--"))
					{
						ParseHostPort(next, cli);
						i++;
					}
					break;
				case "--host":
					if (next != null)
					{
						cli.Host = next;
						i++;
					}
					break;
				case "--port":
					if (next != null && int.TryParse(next, out int pv))
					{
						cli.Port = pv;
						i++;
					}
					break;
				case "--max-players":
					if (next != null && int.TryParse(next, out int mp))
					{
						cli.MaxPlayers = Mathf.Clamp(mp, 1, 64);
						i++;
					}
					break;
				case "--bots":
					if (next != null && int.TryParse(next, out int bc))
					{
						cli.MaxBots = Mathf.Max(0, bc);
						i++;
					}
					break;
				case "--name":
					if (next != null)
					{
						cli.PlayerName = next;
						i++;
					}
					break;
				case "--tickrate":
					if (next != null && int.TryParse(next, out int tr))
					{
						cli.TickRate = Mathf.Clamp(tr, 30, 256);
						i++;
					}
					break;
				case "--reconnect-grace":
					if (
						next != null
						&& float.TryParse(
							next,
							System.Globalization.NumberStyles.Float,
							System.Globalization.CultureInfo.InvariantCulture,
							out float rg
						)
					)
					{
						cli.ReconnectGraceSec = Mathf.Max(0f, rg);
						i++;
					}
					break;
				case "--gamemode":
					if (next != null)
					{
						string g = next.ToLowerInvariant();
						cli.GameMode = (g == "dm" || g == "deathmatch") ? GameMode.Deathmatch : GameMode.Competitive;
						i++;
					}
					break;
				case "--identity":
					if (next != null)
					{
						cli.IdentityOverride = next;
						i++;
					}
					break;
			}
		}
		return cli;
	}

	/// <summary>Parses a "host" or "host:port" string into the target <see cref="NetCli"/>.</summary>
	private static void ParseHostPort(string s, NetCli into)
	{
		int colon = s.IndexOf(':');
		if (colon < 0)
		{
			into.Host = s;
			return;
		}
		into.Host = s.Substring(0, colon);
		if (int.TryParse(s.Substring(colon + 1), out int p))
			into.Port = p;
	}

	/// <summary>Diagnostic string representation listing all parsed CLI fields.</summary>
	public override string ToString() =>
		$"Mode={Mode} AutoConnect={AutoConnect} Host={Host} Port={Port} MaxPlayers={MaxPlayers} MaxBots={MaxBots} Name=\"{PlayerName}\" Tick={TickRate} Grace={ReconnectGraceSec}s";
}
