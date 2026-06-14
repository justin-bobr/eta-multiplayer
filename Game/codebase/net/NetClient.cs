using Godot;
using LiteNetLib;
using System.Collections.Generic;

namespace Vantix.Client;

/// <summary>Client side of the netcode stack. Handles the handshake, sends ConnectRequest, receives
/// SpawnAck, and dispatches player joined/left events.</summary>
public class NetClient
{
	private NetManager _net;
	private EventBasedNetListener _listener;
	private NetPeer _server;
	private readonly NetCli _cli;

	public bool Connected => _server != null && _server.ConnectionState == ConnectionState.Connected;
	public int PingMs => _server?.RoundTripTime ?? 0;

	/// <summary>Own NetId after successful SpawnAck. 0 = not yet assigned.</summary>
	public byte OwnNetId { get; private set; }
	public bool Spawned { get; private set; }
	/// <summary>True once the server authorized a real spawn (deathmatch SpawnAck, or competitive SpawnAuthorize
	/// after TeamSelect). NetMain instantiates the LocalPlayer only when set; otherwise it shows preview-cams + team-select UI.</summary>
	public bool SpawnAuthorized { get; private set; }
	public Team OwnTeam { get; private set; }
	public uint LastServerTick { get; private set; }
	public ushort ServerTickRate { get; private set; }
	public Vector3 PendingSpawnPos { get; private set; }
	public float PendingSpawnYaw { get; private set; }
	/// <summary>Full resource path of the loaded map (e.g. "res://de_dust2.tscn"). Sent in SpawnAck.</summary>
	public string MapPath { get; private set; } = "";
	/// <summary>Map display name without "res://"/".tscn" (e.g. "de_dust2"), derived from <see cref="MapPath"/>.</summary>
	public string MapName { get; private set; } = "";

	/// <summary>Server-broadcast round state, updated via <see cref="PacketType.RoundState"/> (1Hz + on transitions).</summary>
	public uint RoundStartTick { get; private set; }
	public ushort RoundDurationSec { get; private set; } = 115;
	public ushort RoundNumber { get; private set; } = 1;
	public ushort RoundsTotal { get; private set; } = 9;

	/// <summary>Seconds left in the current round = duration - (now_tick - startTick)/tickRate. 0 if no round state yet or expired.</summary>
	public int RoundTimeRemainingSec
	{
		get
		{
			if (ServerTickRate == 0 || RoundDurationSec == 0) return 0;
			long elapsedTicks = (long)NetStats.ServerTickEstimate - (long)RoundStartTick;
			int elapsedSec = (int)(elapsedTicks / ServerTickRate);
			return Mathf.Max(0, RoundDurationSec - elapsedSec);
		}
	}

	/// <summary>Active snapshot of the world state: all other players.</summary>
	public readonly Dictionary<byte, InitialPlayerState> RemotePlayers = new();

	/// <summary>Last server-reported authority yaw for the OWN player.</summary>
	public float LastSelfServerYaw;
	/// <summary>Self-snapshot stats (Kills, Deaths, Hp, Ping) — the scoreboard reads from this.</summary>
	public SnapshotPlayer? LastSelfSnap;
	/// <summary>Server tick of the most recently received snapshot (used for server-time sync).</summary>
	public uint LastSnapshotServerTick;
	/// <summary>Tick index of our last input the server has acknowledged (used for reconciliation).</summary>
	public uint LastAckedInputTick;

	/// <summary>Last snapshots for all remote players. PuppetPlayer reads from here.</summary>
	public readonly Dictionary<byte, SnapshotPlayer> LastRemoteSnapshots = new();

	/// <summary>Ring of the last ~64 reconstructed received snapshots; baseline source for delta packets (baselineTick != 0).</summary>
	private readonly SnapshotBaselineRing _receivedSnapshots = new();
	/// <summary>Most recent reconstructed snapshot tick. Sent in every input packet as <c>ackedSnapshotTick</c>
	/// (the server's baseline key). <see cref="Packets.NoBaselineTick"/> = nothing received yet.</summary>
	public uint LastReceivedSnapshotTick;

	public System.Action OnSpawned;
	public System.Action<InitialPlayerState> OnPlayerJoined;
	public System.Action<byte, LeaveReason> OnPlayerLeft;
	/// <summary>Fires after each received SnapshotPacket.</summary>
	public System.Action OnSnapshot;
	/// <summary>Fires when the transport drops (timeout, kick, server shutdown). Carries the
	/// human-readable disconnect reason so the UI can display it on the reconnect screen.</summary>
	public System.Action<string> OnDisconnected;

	/// <summary>Creates a new client bound to the given CLI configuration.</summary>
	public NetClient(NetCli cli)
	{
		_cli = cli;
	}

	/// <summary>Starts the UDP socket and initiates a connection to the configured host/port.</summary>
	public void Start()
	{
		_listener = new EventBasedNetListener();
		_net = new NetManager(_listener)
		{
			AutoRecycle = true,
			ChannelsCount = 2,
			UpdateTime = 1,
			EnableStatistics = true,
			DisconnectTimeout = 30000,
		};

		_listener.PeerConnectedEvent += OnPeerConnected;
		_listener.PeerDisconnectedEvent += OnPeerDisconnected;
		_listener.NetworkErrorEvent += OnNetworkError;
		_listener.NetworkReceiveEvent += OnNetworkReceive;

		if (!_net.Start())
		{
			GD.PushError("[NetClient] Failed to open UDP socket");
			return;
		}

		_server = _net.Connect(_cli.Host, _cli.Port, NetServer.ProtocolKey);
		Dbg.Print($"[NetClient] Connecting to {_cli.Host}:{_cli.Port} ...");
	}

	private long _lastBytesSent;
	private long _lastBytesReceived;
	private long _lastPacketsSent;
	private long _lastPacketLoss;
	private long _statsSampleTickMs;

	/// <summary>Pumps the LiteNetLib event loop and updates client-side NetStats.</summary>
	public void Poll()
	{
		_net?.PollEvents();
		NetStats.ClientConnected = Connected;
		NetStats.PingMs = PingMs;
		SampleBandwidth();
	}

	/// <summary>Samples LiteNetLib byte/packet counters every 500 ms and feeds NetStats with smoothed rates.</summary>
	private void SampleBandwidth()
	{
		if (_net == null) return;
		long now = (long)Time.GetTicksMsec();
		long dtMs = now - _statsSampleTickMs;
		if (dtMs < 500) return;
		_statsSampleTickMs = now;

		var s = _net.Statistics;
		long sentNow = (long)s.BytesSent;
		long recvNow = (long)s.BytesReceived;
		long pktSentNow = (long)s.PacketsSent;
		long lossNow = (long)s.PacketLoss;
		long dSent = sentNow - _lastBytesSent;
		long dRecv = recvNow - _lastBytesReceived;
		long dSentPkts = pktSentNow - _lastPacketsSent;
		long dLoss = lossNow - _lastPacketLoss;
		_lastBytesSent = sentNow;
		_lastBytesReceived = recvNow;
		_lastPacketsSent = pktSentNow;
		_lastPacketLoss = lossNow;

		NetStats.BytesPerSecUp = (int)(dSent * 1000L / dtMs);
		NetStats.BytesPerSecDown = (int)(dRecv * 1000L / dtMs);
		if (dSentPkts > 0)
			NetStats.PacketLossUpPct = (float)dLoss / dSentPkts * 100f;
	}

	/// <summary>Stops the UDP socket and resets the connected flag.</summary>
	public void Stop()
	{
		_net?.Stop();
		_net = null;
		NetStats.ClientConnected = false;
	}

	private ulong _lastInputSendUsec;
	private float _expectedInputIntervalMs = 7.8125f;

	private readonly LiteNetLib.Utils.NetDataWriter _inputWriter = new();

	/// <summary>Input redundancy ring: last <see cref="Packets.MaxInputRedundancy"/> sent inputs, oldest→newest.
	/// Every SendInput resends all buffered inputs so one lost packet can't drop an edge-triggered intent (Jump/Reload).</summary>
	private readonly EncodedInput[] _inputRing = new EncodedInput[Packets.MaxInputRedundancy];
	private int _inputRingCount;

	/// <summary>Round-robin pool of subtick-event scratch buffers (one per ring slot) so EncodeInput avoids a
	/// per-tick alloc. Pool size == ring capacity guarantees no two live ring entries alias the same array.</summary>
	private readonly SubtickEventEncoded[][] _eventPool = BuildEventPool();
	private int _eventPoolCursor;

	private static SubtickEventEncoded[][] BuildEventPool()
	{
		var pool = new SubtickEventEncoded[Packets.MaxInputRedundancy][];
		for (int i = 0; i < pool.Length; i++)
			pool[i] = new SubtickEventEncoded[Packets.MaxSubtickEventsWire];
		return pool;
	}

	/// <summary>Sends the last <see cref="Packets.MaxInputRedundancy"/> input frames (unreliable, channel 0);
	/// server dedupes by tickIndex. <paramref name="fireSubTick"/> is the quantised sub-tick offset (0..255)
	/// of the fire-press edge, passed verbatim onto the wire.</summary>
	public void SendInput(uint tickIndex, in MovementInput mi,
		bool firePressed, bool reloadPressed, bool inspectPressed, bool slotIsGrenade,
		byte fireSubTick)
	{
		if (!Connected || !Spawned) return;

		ulong nowUsec = Time.GetTicksUsec();
		if (_lastInputSendUsec > 0)
		{
			float intervalMs = (nowUsec - _lastInputSendUsec) / 1000f;
			float deviationMs = System.Math.Abs(intervalMs - _expectedInputIntervalMs);
			NetStats.JitterUpMs = NetStats.JitterUpMs * 0.85f + deviationMs * 0.15f;
		}
		_lastInputSendUsec = nowUsec;

		SubtickEventEncoded[] eventBuf = _eventPool[_eventPoolCursor];
		_eventPoolCursor = (_eventPoolCursor + 1) % _eventPool.Length;
		var encoded = Packets.EncodeInput(tickIndex, mi, firePressed, reloadPressed, inspectPressed, slotIsGrenade, fireSubTick, eventBuf);
		PushInputToRing(encoded);
		Packets.WriteInputPacketInto(_inputWriter, LastReceivedSnapshotTick, _inputRing, 0, _inputRingCount);
		_server.Send(_inputWriter, NetServer.ChannelUnreliable, LiteNetLib.DeliveryMethod.Unreliable);
	}

	/// <summary>Appends an encoded input to the redundancy ring, shifting out the oldest when at capacity.</summary>
	private void PushInputToRing(in EncodedInput input)
	{
		if (_inputRingCount == Packets.MaxInputRedundancy)
		{
			for (int i = 0; i < Packets.MaxInputRedundancy - 1; i++)
				_inputRing[i] = _inputRing[i + 1];
			_inputRing[Packets.MaxInputRedundancy - 1] = input;
		}
		else
		{
			_inputRing[_inputRingCount] = input;
			_inputRingCount++;
		}
	}

	/// <summary>Fires once the transport-level connection is established; sends the ConnectRequest.</summary>
	private void OnPeerConnected(NetPeer peer)
	{
		_server = peer;
		Dbg.Print($"[NetClient] Transport connected to {peer.Address} (ping {peer.RoundTripTime}ms) — sending ConnectRequest");

		string identity = !string.IsNullOrEmpty(_cli.IdentityOverride)
			? _cli.IdentityOverride
			: Settings.NetIdentityToken;
		byte[] token = string.IsNullOrEmpty(identity)
			? System.Array.Empty<byte>()
			: System.Text.Encoding.UTF8.GetBytes(identity);
		var writer = Packets.WriteConnectRequest(_cli.PlayerName, token);
		peer.Send(writer, NetServer.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Resets all session-scoped state when the transport disconnects and notifies subscribers
	/// (e.g. NetMain swaps to the reconnect screen).</summary>
	private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
	{
		Dbg.Print($"[NetClient] Disconnected: {info.Reason}");
		_server = null;
		bool wasSpawned = Spawned;
		Spawned = false;
		OwnNetId = 0;
		RemotePlayers.Clear();
		LastRemoteSnapshots.Clear();
		_ownedProjectiles.Clear();
		_puppetProjectiles.Clear();
		_receivedSnapshots.Clear();
		LastReceivedSnapshotTick = Packets.NoBaselineTick;
		_inputRingCount = 0;
		if (wasSpawned)
			OnDisconnected?.Invoke(info.Reason.ToString());
	}

	/// <summary>Logs a warning for low-level socket errors reported by LiteNetLib.</summary>
	private void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError error)
	{
		GD.PushWarning($"[NetClient] Network error from {endPoint}: {error}");
	}

	/// <summary>Dispatches an incoming packet to its typed handler based on the leading PacketType byte.</summary>
	private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
	{
		if (reader.AvailableBytes < 1)
		{
			reader.Recycle();
			return;
		}
		var type = (PacketType)reader.GetByte();
		switch (type)
		{
			case PacketType.SpawnAck:
				HandleSpawnAck(reader);
				break;
			case PacketType.SpawnAuthorize:
				HandleSpawnAuthorize(reader);
				break;
			case PacketType.PlayerJoined:
				HandlePlayerJoined(reader);
				break;
			case PacketType.PlayerLeft:
				HandlePlayerLeft(reader);
				break;
			case PacketType.Snapshot:
				HandleSnapshot(reader);
				break;
			case PacketType.ShotFired:
				HandleShotFired(reader);
				break;
			case PacketType.Footstep:
				HandleFootstep(reader);
				break;
			case PacketType.Jump:
				HandleJump(reader);
				break;
			case PacketType.Land:
				HandleLand(reader);
				break;
			case PacketType.DropMag:
				HandleDropMag(reader);
				break;
			case PacketType.Hit:
				HandleHit(reader);
				break;
			case PacketType.Death:
				HandleDeath(reader);
				break;
			case PacketType.Respawn:
				HandleRespawn(reader);
				break;
			case PacketType.GrenadeSpawn:
				HandleGrenadeSpawn(reader);
				break;
			case PacketType.ProjectileState:
				HandleProjectileState(reader);
				break;
			case PacketType.ProjectileDespawn:
				HandleProjectileDespawn(reader);
				break;
			case PacketType.DebugHitboxes:
				HandleDebugHitboxes(reader);
				break;
			case PacketType.ConVarSyncBroadcast:
				HandleConVarSyncBroadcast(reader);
				break;
			case PacketType.RoundState:
				HandleRoundState(reader);
				break;
			case PacketType.ServerLog:
				HandleServerLog(reader);
				break;
			default:
				break;
		}
		reader.Recycle();
	}

	/// <summary>Prints a server-side log message into the client's stdout and the in-game ConsoleHud, prefixed [SV].</summary>
	private void HandleServerLog(NetPacketReader r)
	{
		Packets.ReadServerLog(r, out string msg);
		GD.Print($"[SV→CL] {msg}");
		ConsoleHud.Instance?.PrintLine($"[color=cyan][SV][/color] {msg}");
	}

	/// <summary>Latest server-reported hitbox transforms per agent NetId (~10Hz when debug is on).
	/// <see cref="HudServerHitboxesDebug"/> renders them as red shapes.</summary>
	public readonly Dictionary<byte, Transform3D[]> ServerHitboxTransforms = new();
	private void HandleDebugHitboxes(NetPacketReader r)
	{
		var agent = Packets.ReadDebugHitboxes(r, out uint _);
		ServerHitboxTransforms[agent.NetId] = agent.Transforms;
	}

	/// <summary>Requests an sv_* ConVar change; the server validates and broadcasts the new value to all clients.</summary>
	public void SendConVarSyncRequest(string name, string value)
	{
		if (_server == null) return;
		var w = Packets.WriteConVarSyncRequest(name, value);
		_server.Send(w, NetServer.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>C2S: requests a spawn after the user picks CT/T (competitive). Idempotent — the server
	/// silently drops invalid requests (already in team, bad value, world not ready). Reliable channel.</summary>
	public void SendTeamSelect(Team team)
	{
		if (_server == null) return;
		var w = Packets.WriteTeamSelect(team);
		_server.Send(w, NetServer.ChannelReliable, DeliveryMethod.ReliableOrdered);
		Dbg.Print($"[NetClient] TeamSelect({team}) sent to server");
	}

	/// <summary>Receives the post-TeamSelect spawn authorization; sets the pending pose and SpawnAuthorized=true.</summary>
	private void HandleSpawnAuthorize(NetPacketReader r)
	{
		Packets.ReadSpawnAuthorize(r, out Team team, out Vector3 spawnPos, out float spawnYaw);
		OwnTeam = team;
		PendingSpawnPos = spawnPos;
		PendingSpawnYaw = spawnYaw;
		SpawnAuthorized = true;
		Dbg.Print($"[NetClient] SpawnAuthorize received: team={team} pos={spawnPos} yaw={spawnYaw:F2}");
	}

	public void SendWorldInitComplete()
	{
		if (_server == null || _worldInitSent) return;
		_worldInitSent = true;
		var w = Packets.WriteWorldInitComplete();
		_server.Send(w, NetServer.ChannelReliable, DeliveryMethod.ReliableOrdered);
		Dbg.Print("[NetClient] WorldInitComplete sent to server");
	}
	private bool _worldInitSent;

	/// <summary>Applies a broadcast/initial-sync ConVar update so visualization gates reflect server state.</summary>
	private void HandleConVarSyncBroadcast(NetPacketReader r)
	{
		Packets.ReadConVarSyncBroadcast(r, out string name, out string value);
		if (ConVars.TrySet(name, value))
			GD.Print($"[NetClient] ConVarSync: {name} = {value}");
		else
			GD.PushWarning($"[NetClient] ConVarSync failed to apply: {name} = {value}");
	}

	private uint _nextLocalProjectileId = 1;
	private readonly Dictionary<uint, SmokeGrenade> _ownedProjectiles = new();
	private readonly Dictionary<ulong, SmokeGrenade> _puppetProjectiles = new();
	/// <summary>Builds a combined dictionary key from owner NetId and projectile id.</summary>
	private static ulong PuppetKey(byte ownerNetId, uint projectileId) => ((ulong)ownerNetId << 32) | projectileId;

	/// <summary>Returns the next unique projectile id for a local player's throw.</summary>
	public uint AllocateProjectileId() => _nextLocalProjectileId++;

	/// <summary>Registers a locally owned projectile instance for state replication.</summary>
	public void RegisterOwnedProjectile(uint projectileId, SmokeGrenade g) => _ownedProjectiles[projectileId] = g;
	/// <summary>Removes a locally owned projectile from the registry.</summary>
	public void UnregisterOwnedProjectile(uint projectileId) => _ownedProjectiles.Remove(projectileId);
	/// <summary>Registers a puppet projectile (echo from a remote thrower) for state updates.</summary>
	public void RegisterPuppetProjectile(byte ownerNetId, uint projectileId, SmokeGrenade g) => _puppetProjectiles[PuppetKey(ownerNetId, projectileId)] = g;
	/// <summary>Removes a puppet projectile from the registry.</summary>
	public void UnregisterPuppetProjectile(byte ownerNetId, uint projectileId) => _puppetProjectiles.Remove(PuppetKey(ownerNetId, projectileId));

	/// <summary>Sends a grenade spawn event to the server so other peers can spawn a puppet copy.</summary>
	public void SendGrenadeSpawn(uint projectileId, byte grenadeType, Vector3 origin, Vector3 velocity)
	{
		if (!Connected || !Spawned) return;
		var w = Packets.WriteGrenadeSpawn(OwnNetId, projectileId, grenadeType, origin, velocity);
		_server.Send(w, NetServer.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Sends a periodic position/velocity update for an owner-controlled projectile (unreliable).</summary>
	public void SendProjectileState(uint projectileId, Vector3 pos, Vector3 vel)
	{
		if (!Connected || !Spawned) return;
		var w = Packets.WriteProjectileState(OwnNetId, projectileId, pos, vel);
		_server.Send(w, NetServer.ChannelUnreliable, DeliveryMethod.Unreliable);
	}

	/// <summary>Sends a reliable signal that an owner-controlled projectile has terminated.</summary>
	public void SendProjectileDespawn(uint projectileId, Vector3 finalPos)
	{
		if (!Connected || !Spawned) return;
		var w = Packets.WriteProjectileDespawn(OwnNetId, projectileId, finalPos);
		_server.Send(w, NetServer.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Spawns a puppet grenade for remote throwers; the local thrower's echo is dropped.</summary>
	private void HandleGrenadeSpawn(NetPacketReader r)
	{
		Packets.ReadGrenadeSpawn(r, out byte netId, out uint projectileId, out byte grenadeType, out Vector3 origin, out Vector3 velocity);
		var puppet = LookupPuppet(netId);
		if (puppet == null) return;
		puppet.SpawnGrenade(netId, projectileId, grenadeType, origin, velocity);
	}

	/// <summary>Applies a remote projectile state update to the matching puppet projectile.</summary>
	private void HandleProjectileState(NetPacketReader r)
	{
		Packets.ReadProjectileState(r, out byte ownerNetId, out uint projectileId, out Vector3 pos, out Vector3 vel);
		if (ownerNetId == OwnNetId) return;
		if (_puppetProjectiles.TryGetValue(PuppetKey(ownerNetId, projectileId), out var g) && Godot.GodotObject.IsInstanceValid(g))
			g.ApplyRemoteState(pos, vel);
	}

	/// <summary>Applies a remote projectile despawn signal and removes the puppet from the registry.</summary>
	private void HandleProjectileDespawn(NetPacketReader r)
	{
		Packets.ReadProjectileDespawn(r, out byte ownerNetId, out uint projectileId, out Vector3 finalPos);
		if (ownerNetId == OwnNetId) return;
		var key = PuppetKey(ownerNetId, projectileId);
		if (_puppetProjectiles.TryGetValue(key, out var g) && Godot.GodotObject.IsInstanceValid(g))
		{
			g.ApplyRemoteDespawn(finalPos);
			_puppetProjectiles.Remove(key);
		}
	}

	/// <summary>Reads a RoundState heartbeat (1Hz). Remaining time is exposed via <see cref="RoundTimeRemainingSec"/>.</summary>
	private void HandleRoundState(NetPacketReader r)
	{
		Packets.ReadRoundState(r, out uint startTick, out ushort duration, out ushort number, out ushort total);
		RoundStartTick = startTick;
		RoundDurationSec = duration;
		RoundNumber = number;
		RoundsTotal = total;
	}

	/// <summary>Strips "res://" and the extension to produce a map display name ("res://de_dust2.tscn" → "de_dust2").</summary>
	private static string ExtractMapName(string path)
	{
		if (string.IsNullOrEmpty(path)) return "";
		string s = path;
		if (s.StartsWith("res://")) s = s.Substring(6);
		int dot = s.LastIndexOf('.');
		if (dot > 0) s = s.Substring(0, dot);
		return s;
	}

	/// <summary>Applies the initial spawn assignment from the server, persists the assigned identity token, and fires OnSpawned.</summary>
	private void HandleSpawnAck(NetPacketReader r)
	{
		Packets.ReadSpawnAck(r,
			out byte yourId, out Team yourTeam,
			out string map, out uint serverTick, out ushort tickRate,
			out Vector3 spawn, out float yaw, out InitialPlayerState[] others, out byte[] assignedToken);
		OwnNetId = yourId;
		OwnTeam = yourTeam;
		LastServerTick = serverTick;
		ServerTickRate = tickRate;
		PendingSpawnPos = spawn;
		PendingSpawnYaw = yaw;
		MapPath = map ?? "";
		MapName = ExtractMapName(MapPath);
		RemotePlayers.Clear();
		foreach (var o in others) RemotePlayers[o.NetId] = o;
		Spawned = true;
		SpawnAuthorized = yourTeam != Team.Spectator;

		if (assignedToken != null && assignedToken.Length > 0 && string.IsNullOrEmpty(_cli.IdentityOverride))
		{
			string newToken = System.Text.Encoding.UTF8.GetString(assignedToken);
			if (newToken != Settings.NetIdentityToken)
			{
				Settings.NetIdentityToken = newToken;
				Settings.Save();
				Dbg.Print($"[NetClient] Identity persisted (server-assigned)");
			}
		}

		Dbg.Print($"[NetClient] SpawnAck: netId={yourId} map={map} serverTick={serverTick} tickRate={tickRate} spawn={spawn} yaw={yaw:F2}rad others={others.Length}");
		OnSpawned?.Invoke();
	}

	/// <summary>Records the joining remote player and forwards a join event to subscribers.</summary>
	private void HandlePlayerJoined(NetPacketReader r)
	{
		var p = Packets.ReadPlayerJoined(r);
		RemotePlayers[p.NetId] = p;
		Dbg.Print($"[NetClient] Player joined: netId={p.NetId} name=\"{p.PlayerName}\" pos={p.Position}");
		OnPlayerJoined?.Invoke(p);
	}

	/// <summary>Removes the leaving remote player and forwards a leave event to subscribers.</summary>
	private void HandlePlayerLeft(NetPacketReader r)
	{
		Packets.ReadPlayerLeft(r, out byte id, out byte reason);
		RemotePlayers.Remove(id);
		LastRemoteSnapshots.Remove(id);
		Dbg.Print($"[NetClient] Player left: netId={id} reason={(LeaveReason)reason}");
		OnPlayerLeft?.Invoke(id, (LeaveReason)reason);
	}

	private ulong _lastSnapshotArrivalUsec;
	private float _expectedSnapshotIntervalMs = 15.625f;

	/// <summary>Reused ReadSnapshot buffer (avoids a per-snapshot alloc); grows only when player count increases.</summary>
	private SnapshotPlayer[] _snapshotPlayerBuffer;

	/// <summary>Decodes a snapshot, updates remote/self caches, drives client-side reconciliation, and fires OnSnapshot.</summary>
	private void HandleSnapshot(NetPacketReader r)
	{
		ulong nowUsec = Time.GetTicksUsec();
		if (_lastSnapshotArrivalUsec > 0)
		{
			float intervalMs = (nowUsec - _lastSnapshotArrivalUsec) / 1000f;
			float deviationMs = System.Math.Abs(intervalMs - _expectedSnapshotIntervalMs);
			NetStats.JitterDownMs = NetStats.JitterDownMs * 0.85f + deviationMs * 0.15f;
		}
		_lastSnapshotArrivalUsec = nowUsec;

		bool ok = Packets.ReadSnapshot(r, out uint serverTick, out uint ackedInput, out uint baselineTick,
			LookupBaseline, ref _snapshotPlayerBuffer, out int playerCount);
		if (!ok)
		{
			return;
		}
		LastSnapshotServerTick = serverTick;
		LastAckedInputTick = ackedInput;
		NetStats.ServerTickEstimate = serverTick;

		_receivedSnapshots.Push(serverTick, _snapshotPlayerBuffer, playerCount);
		LastReceivedSnapshotTick = serverTick;

		SnapshotPlayer? selfSnap = null;
		for (int i = 0; i < playerCount; i++)
		{
			var p = _snapshotPlayerBuffer[i];
			if (p.NetId == OwnNetId)
			{
				LastSelfServerYaw = p.Yaw;
				LastSelfSnap = p;
				selfSnap = p;
			}
			else
			{
				LastRemoteSnapshots[p.NetId] = p;
			}
		}

		if (selfSnap.HasValue && ackedInput > 0u)
		{
			var local = NetMain.Instance?.FindLocalPlayer() as LocalPlayer;
			local?.ApplyServerCorrection(ackedInput, selfSnap.Value.Pos, selfSnap.Value.Vel);
		}

		OnSnapshot?.Invoke();
	}

	/// <summary>Baseline lookup for <see cref="Packets.ReadSnapshot"/>; returns the reconstructed states for a tick, or null if aged out.</summary>
	private (SnapshotPlayer[] players, int count)? LookupBaseline(uint tick)
	{
		var e = _receivedSnapshots.Find(tick);
		if (e == null) return null;
		return (e.Players, e.PlayerCount);
	}

	/// <summary>Returns the puppet driver for a given NetId, or null if the id is the local player or unknown.</summary>
	private PuppetPlayer LookupPuppet(byte netId)
	{
		if (netId == OwnNetId) return null;
		var pm = NetMain.Instance?.Puppets;
		if (pm == null) return null;
		pm.Puppets.TryGetValue(netId, out var p);
		return p;
	}

	/// <summary>Routes a shot event to the matching puppet for tracer/impact playback. Own shots have no puppet;
	/// when sv_debug_bullets is on a red marker is spawned at the server hit position to compare against the client prediction.</summary>
	private void HandleShotFired(NetPacketReader r)
	{
		Packets.ReadShotFired(r, out byte netId, out byte weaponId, out Vector3 origin, out Vector3 dir,
			out bool tracer, out bool hit, out Vector3 hitPos, out Vector3 hitNormal, out string material);
		var puppet = LookupPuppet(netId);
		if (puppet != null)
		{
			puppet.PlayShot(weaponId, origin, dir, tracer, hit, hitPos, hitNormal, material);
			return;
		}
		if (netId == OwnNetId && hit && ConVars.Sv.DebugBullets)
		{
			Dbg.Print($"[sv-impact] pos=({hitPos.X:F2},{hitPos.Y:F2},{hitPos.Z:F2}) mat={material}");
			SpawnServerImpactDebugDot(hitPos);
		}
	}

	private static void SpawnServerImpactDebugDot(Vector3 worldPos)
	{
		var tree = NetMain.Instance?.GetTree();
		if (tree?.CurrentScene == null) return;
		var dot = new Godot.MeshInstance3D
		{
			Name = "sv_debug_dot",
			Mesh = new Godot.SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 8, Rings = 4 },
			MaterialOverride = new Godot.StandardMaterial3D
			{
				AlbedoColor = new Godot.Color(1f, 0.05f, 0.05f),
				ShadingMode = Godot.BaseMaterial3D.ShadingModeEnum.Unshaded,
				NoDepthTest = true,
			},
			CastShadow = Godot.GeometryInstance3D.ShadowCastingSetting.Off,
		};
		tree.CurrentScene.AddChild(dot);
		dot.GlobalPosition = worldPos;
		var timer = tree.CreateTimer(5.0);
		timer.Timeout += () => { if (Godot.GodotObject.IsInstanceValid(dot)) dot.QueueFree(); };
	}

	/// <summary>Routes a footstep event to the corresponding puppet for spatial audio playback.</summary>
	private void HandleFootstep(NetPacketReader r)
	{
		Packets.ReadFootstep(r, out byte netId, out Vector3 pos, out string material,
			out byte loudness, out bool leftFoot, out bool sprinting);
		LookupPuppet(netId)?.PlayFootstep(pos, material, loudness, leftFoot, sprinting);
	}

	/// <summary>Routes a jump event to the corresponding puppet.</summary>
	private void HandleJump(NetPacketReader r)
	{
		Packets.ReadJump(r, out byte netId);
		LookupPuppet(netId)?.PlayJump();
	}

	/// <summary>Routes an empty-reload event to the corresponding puppet so it drops its magazine.</summary>
	private void HandleDropMag(NetPacketReader r)
	{
		Packets.ReadDropMag(r, out byte netId);
		LookupPuppet(netId)?.PlayDropMag();
	}

	/// <summary>Routes a landing event to the corresponding puppet.</summary>
	private void HandleLand(NetPacketReader r)
	{
		Packets.ReadLand(r, out byte netId, out float impactSpeed);
		LookupPuppet(netId)?.PlayLand(impactSpeed);
	}

	/// <summary>Killfeed subscribers (HudKillfeed): victim, attacker, weaponId, isHeadshot.</summary>
	public event System.Action<byte, byte, byte, bool> OnDeath;
	private void HandleDeath(NetPacketReader r)
	{
		Packets.ReadDeath(r, out byte victim, out byte attacker, out byte weaponId, out bool isHeadshot);
		Dbg.Print($"[NetClient] Death: netId={victim} killed by netId={attacker} weaponId={weaponId} HS={isHeadshot}");
		if (victim == OwnNetId)
		{
			var local = NetMain.Instance?.FindLocalPlayer();
			if (local != null) local.CanFire = false;
		}
		OnDeath?.Invoke(victim, attacker, weaponId, isHeadshot);
	}

	/// <summary>Server emits hits only to shooter + victim. HudHitmarker subscribes via <see cref="OnHit"/>.</summary>
	public event System.Action<byte, byte, HitboxGroup, byte, byte> OnHit;
	/// <summary>Decodes a hit event and forwards it through the <see cref="OnHit"/> action.</summary>
	private void HandleHit(NetPacketReader r)
	{
		Packets.ReadHit(r, out byte shooter, out byte victim, out HitboxGroup group, out byte damage, out byte hpLeft, out byte _weaponId);
		OnHit?.Invoke(shooter, victim, group, damage, hpLeft);
	}

	/// <summary>Applies an authoritative respawn — teleports the local player and resets transient state.</summary>
	private void HandleRespawn(NetPacketReader r)
	{
		Packets.ReadRespawn(r, out byte netId, out Vector3 pos, out float yaw, out byte hp);
		Dbg.Print($"[NetClient] Respawn: netId={netId} at {pos} hp={hp}");
		if (netId == OwnNetId)
		{
			var local = NetMain.Instance?.FindLocalPlayer();
			if (local != null)
			{
				local.GlobalPosition = pos;
				var rot = local.Rotation; rot.Y = yaw; local.Rotation = rot;
				local.Velocity = Vector3.Zero;
				local.Movement.Velocity = Vector3.Zero;
				local.CanFire = true;
				local.Movement.Stamina = ConVars.Sv.MaxStamina;
				local.Movement.ResetSpawnConsumables();
				local.Movement.InitializeAmmo(ConVars.Weapons.AR15);
				local.ResetInterpToCurrentPos();
				local.Prediction.Clear();
			}
		}
	}
}
