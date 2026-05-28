using Godot;

/// <summary>
/// Global container for netcode stats. Written by <see cref="NetClient"/> and <see cref="NetServer"/>,
/// read by <see cref="DebugOverlay"/>. Static so the debug-overlay row does not need to be wired
/// up per node path.
///
/// In Mode=Server (headless) only the server fields are populated; in Mode=Client only the client
/// fields; in Listen both.
/// </summary>
public static class NetStats
{
	public static NetMode Mode = NetMode.Listen;
	public static bool ServerRunning;
	public static bool ClientConnected;

	public static int PingMs;
	public static float PacketLossUpPct;
	public static float PacketLossDownPct;
	public static int BytesPerSecUp;
	public static int BytesPerSecDown;
	public static uint ClientTick;
	public static uint ServerTickEstimate;
	public static int InterpDelayMs;

	public static int PeerCount;
	public static int MaxPlayers;
	public static uint ServerTick;

	/// <summary>Last reconcile drift in metres (server pos vs. client prediction at the ack'd tick).
	/// 0 = no correction since spawn. Used by <see cref="DebugOverlay"/>/NetGraphOverlay for
	/// severity colour coding (green/yellow/red).</summary>
	public static float LastReconcileDriftM;
	/// <summary>Number of reconciles in the last ~1 s (rolling). 0 = stable, high = constant drift replays.</summary>
	public static int ReconcilesPerSec;
	/// <summary>Engine time (sec) when the last reconcile triggered — used for "recent" highlight.</summary>
	public static double LastReconcileTimeSec;

	/// <summary>Snapshot inter-arrival time variance in ms — fed by NetClient.HandleSnapshot.</summary>
	public static float JitterDownMs;
	/// <summary>Input send variance in ms (client → server) — send-interval drift.</summary>
	public static float JitterUpMs;

	/// <summary>Called once on mode change — clears stale values.</summary>
	public static void Reset(NetMode mode)
	{
		Mode = mode;
		ServerRunning = false;
		ClientConnected = false;
		PingMs = 0;
		PacketLossUpPct = 0f;
		PacketLossDownPct = 0f;
		BytesPerSecUp = 0;
		BytesPerSecDown = 0;
		ClientTick = 0u;
		ServerTickEstimate = 0u;
		ServerTick = 0u;
		PeerCount = 0;
		InterpDelayMs = 100;
		LastReconcileDriftM = 0f;
		ReconcilesPerSec = 0;
		LastReconcileTimeSec = 0.0;
		JitterDownMs = 0f;
		JitterUpMs = 0f;
	}
}
