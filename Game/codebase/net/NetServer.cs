using Godot;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;

/// <summary>Server side of the netcode stack. Listens, accepts peers, runs the ConnectRequest handshake,
/// allocates NetIds, broadcasts SpawnAck/PlayerJoined, runs the sim, and emits snapshots.</summary>
public class NetServer
{
	public const string ProtocolKey = "eta_proto_v1";
	public const int ChannelUnreliable = 0;
	public const int ChannelReliable = 1;

	private NetManager _net;
	private EventBasedNetListener _listener;
	private readonly NetCli _cli;
	private readonly Dictionary<NetPeer, PeerState> _peers = new();
	private readonly Dictionary<byte, PeerState> _peersByNetId = new();
	private readonly List<PeerState> _bots = new();
	/// <summary>Disconnect pool: token → frozen state. If a reconnect arrives with the same token, resume.</summary>
	private readonly Dictionary<string, PeerState> _disconnectedPool = new();
	private uint _serverTick;
	private readonly SpawnManager _spawns = new();
	private PackedScene _characterScene;
	private Node3D _playersContainer;

	/// <summary>Spawn points loaded from the map markers.</summary>
	public SpawnManager Spawns => _spawns;

	public bool Running => _net != null && _net.IsRunning;
	public int PeerCount => _peers.Count;
	public uint ServerTick => _serverTick;

	/// <summary>Creates a new server bound to the given CLI configuration.</summary>
	public NetServer(NetCli cli)
	{
		_cli = cli;
	}

	/// <summary>Binds the UDP listener and wires LiteNetLib handlers. Starts <c>_serverTick</c> at 1 so tick 0
	/// stays the <see cref="Packets.NoBaselineTick"/> sentinel.</summary>
	public void Start()
	{
		_listener = new EventBasedNetListener();
		_net = new NetManager(_listener)
		{
			AutoRecycle = true,
			ChannelsCount = 2,
			UpdateTime = 1,
			BroadcastReceiveEnabled = false,
			EnableStatistics = true,
			DisconnectTimeout = 30000,
		};

		_listener.ConnectionRequestEvent += OnConnectionRequest;
		_listener.PeerConnectedEvent += OnPeerConnected;
		_listener.PeerDisconnectedEvent += OnPeerDisconnected;
		_listener.NetworkErrorEvent += OnNetworkError;
		_listener.NetworkReceiveEvent += OnNetworkReceive;

		if (!_net.Start(_cli.Port))
		{
			GD.PushError($"[NetServer] Failed to bind UDP port {_cli.Port} — already in use?");
			NetStats.ServerRunning = false;
			return;
		}

		_serverTick = 1;

		Dbg.Print($"[NetServer] UDP bound :{_cli.Port}  max-players={_cli.MaxPlayers}  proto={ProtocolKey}");
		NetStats.ServerRunning = true;
		NetStats.MaxPlayers = _cli.MaxPlayers;
	}

	private long _lastBytesSent;
	private long _lastBytesReceived;
	private long _lastStatsTickMs;

	/// <summary>Runs one server tick: drains events, drives the sim/snapshot/respawn pipelines, bumps the tick.
	/// The AllPeers cache is refreshed twice (top + after handshake finalise) so fresh peers make this tick's snapshot.</summary>
	public void Poll()
	{
		MiniProfiler.ProfilingEnabled = ConVars.Cl.Profiler || ConVars.Sv.Profiler;

		using var _prof = MiniProfiler.SampleServer("NetServer.Poll (total)");
		using (MiniProfiler.SampleServer("NetServer.PollEvents")) _net?.PollEvents();
		NetStats.PeerCount = PeerCount;
		NetStats.ServerTick = _serverTick;
		RefreshAllPeersCache();
		using (MiniProfiler.SampleServer("NetServer.PushPositionsToRewind")) PushPositionsToRewind();
		using (MiniProfiler.SampleServer("NetServer.UpdatePeerPings")) UpdatePeerPings();
		using (MiniProfiler.SampleServer("NetServer.TryScanSpawns")) TryScanSpawns();
		using (MiniProfiler.SampleServer("NetServer.BuildVoxelPvsIfNeeded")) TryBuildVoxelPvs();
		using (MiniProfiler.SampleServer("NetServer.FinalizePendingHandshakes")) FinalizePendingHandshakes();
		RefreshAllPeersCache();
		using (MiniProfiler.SampleServer("NetServer.UpdateBotInputs")) UpdateBotInputs();
		using (MiniProfiler.SampleServer("NetServer.FeedInputsToAgents")) FeedInputsToAgents();
		using (MiniProfiler.SampleServer("NetServer.BroadcastSnapshots")) BroadcastSnapshots();
		using (MiniProfiler.SampleServer("NetServer.BroadcastDebugHitboxes")) BroadcastDebugHitboxes();
		using (MiniProfiler.SampleServer("NetServer.TickRespawn")) TickRespawn();
		using (MiniProfiler.SampleServer("NetServer.TickHpRegen")) TickHpRegen();
		using (MiniProfiler.SampleServer("NetServer.EvictExpiredDisconnects")) EvictExpiredDisconnects();
		using (MiniProfiler.SampleServer("NetServer.SampleBandwidth")) SampleBandwidth();
		using (MiniProfiler.SampleServer("NetServer.TickRoundState")) TickRoundState();
		ReportFoWActivityIfDue();
		WriteProfilerReportIfDue();
		_serverTick++;
	}

	private uint _roundStartTick;
	private ushort _roundDurationSec = 115;
	private ushort _roundNumber = 1;
	private ushort _roundsTotal = 9;
	private uint _lastRoundStateBroadcastTick;
	/// <summary>1Hz heartbeat at 128tps — keeps late-joining clients in sync within ~1s and corrects client-side drift.</summary>
	private const int RoundStateBroadcastEveryTicks = 128;

	/// <summary>Ticks the round timer, advancing to the next round on expiry (no score reset yet — no
	/// win-condition system). Rebroadcasts round state at 1Hz to re-sync late joiners and drifting clients.</summary>
	private void TickRoundState()
	{
		uint elapsedTicks = _serverTick - _roundStartTick;
		int elapsedSec = (int)(elapsedTicks / (uint)Mathf.Max(1, _cli.TickRate));
		if (elapsedSec >= _roundDurationSec)
		{
			_roundStartTick = _serverTick;
			_roundNumber = (ushort)Mathf.Min(_roundsTotal, _roundNumber + 1);
			BroadcastRoundState();
			return;
		}
		if (_serverTick - _lastRoundStateBroadcastTick >= RoundStateBroadcastEveryTicks)
		{
			BroadcastRoundState();
		}
	}

	private void BroadcastRoundState()
	{
		_lastRoundStateBroadcastTick = _serverTick;
		var writer = Packets.WriteRoundState(_roundStartTick, _roundDurationSec, _roundNumber, _roundsTotal);
		Broadcast(writer, DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: null);
	}

	/// <summary>= 10s at 128Hz.</summary>
	private const int ProfilerWriteEveryTicks = 1280;
	/// <summary>Periodic profiler dump to user://server.profile ([SV]-prefixed samples); logs the resolved path on first write.</summary>
	private bool _profilerPathPrinted;
	private void WriteProfilerReportIfDue()
	{
		if (!ConVars.Sv.Profiler) return;
		if ((_serverTick % ProfilerWriteEveryTicks) != 0) return;
		MiniProfiler.WriteReport("user://server.profile", "[SV]", ConVars.Sv.ProfilerThresholdMs);
		if (!_profilerPathPrinted)
		{
			_profilerPathPrinted = true;
			string abs = ProjectSettings.GlobalizePath("user://server.profile");
			GD.PushWarning($"[Profiler] sv_profiler ON — server report will be written every 10s to: {abs}");
		}
	}

	private const int DebugHitboxBroadcastEvery = 1;
	private const int DebugHitboxInterpDelayTicks = 6;

	/// <summary>Broadcasts rewound hitbox poses to all peers for visual lag-comp verification. <c>vizTick</c>
	/// uses <c>_serverTick + 1</c> because NetworkPlayer._currentTick runs one ahead of _serverTick within the
	/// same physics frame; without the +1 the viz trails the actual hitscan query tick by one.</summary>
	private void BroadcastDebugHitboxes()
	{
		if (!ConVars.Sv.DebugHitboxes) return;
		if ((_serverTick % DebugHitboxBroadcastEvery) != 0) return;
		if (_peers.Count == 0) return;

		uint vizTick = (uint)Mathf.Max(0L, (long)_serverTick + 1 - DebugHitboxInterpDelayTicks);

		bool useRewind = !ConVars.Sv.NoRewind;
		foreach (var receiverKv in _peers)
		{
			var receiverPeer = receiverKv.Key;
			var receiverState = receiverKv.Value;
			if (receiverState.HandshakePending) continue;

			foreach (var agentState in AllPeers)
			{
				if (agentState == receiverState) continue;
				if (agentState.ServerAgent is not NetworkPlayer pc) continue;
				var rig = pc.GetHitboxRig();
				if (rig == null || rig.CollisionShapes.Count == 0) continue;

				Transform3D[] transforms = useRewind ? pc.BoneHistory?.Query(vizTick) : null;
				if (transforms == null || transforms.Length != rig.CollisionShapes.Count)
				{
					var shapes = rig.CollisionShapes;
					transforms = new Transform3D[shapes.Count];
					for (int i = 0; i < shapes.Count; i++)
						transforms[i] = shapes[i] != null ? shapes[i].GlobalTransform : Transform3D.Identity;
				}

				var data = new DebugHitboxAgent { NetId = agentState.NetId, Transforms = transforms };
				var writer = Packets.WriteDebugHitboxes(_serverTick, data);
				receiverPeer.Send(writer, ChannelUnreliable, DeliveryMethod.Unreliable);
			}
		}
	}

	/// <summary>Delay after the last damage hit before HP regen starts.</summary>
	public const long RegenDelayMs = 8000;
	/// <summary>Regen interval — grants +1 HP every this-many ms (~12 hp/s).</summary>
	private const long RegenTickMs = 80;
	/// <summary>Max regenerable HP (kevlar does not regen).</summary>
	private const byte RegenCapHp = 100;
	private long _lastRegenTickMs;

	/// <summary>Regens HP towards <see cref="RegenCapHp"/> for peers past the <see cref="RegenDelayMs"/> grace
	/// window. Skips peers that never took damage (<c>LastDamageTickMs == 0</c>).</summary>
	private void TickHpRegen()
	{
		long now = (long)Time.GetTicksMsec();
		if (now - _lastRegenTickMs < RegenTickMs) return;
		_lastRegenTickMs = now;
		foreach (var s in AllPeers)
		{
			if (s.Hp == 0 || s.Hp >= RegenCapHp) continue;
			if (s.LastDamageTickMs == 0) continue;
			if (now - s.LastDamageTickMs < RegenDelayMs) continue;
			s.Hp = (byte)Mathf.Min(RegenCapHp, s.Hp + 1);
		}
	}

	/// <summary>Frees disconnected-pool entries whose reconnect grace has expired and notifies remaining peers.</summary>
	private void EvictExpiredDisconnects()
	{
		if (_disconnectedPool.Count == 0) return;
		long nowMs = (long)Time.GetTicksMsec();
		long graceMs = (long)(_cli.ReconnectGraceSec * 1000f);
		List<string> evict = null;
		foreach (var kv in _disconnectedPool)
		{
			if (nowMs - kv.Value.DisconnectedAtTickMs <= graceMs) continue;
			(evict ??= new List<string>()).Add(kv.Key);
		}
		if (evict == null) return;
		foreach (var k in evict)
		{
			var s = _disconnectedPool[k];
			_disconnectedPool.Remove(k);
			_peersByNetId.Remove(s.NetId);
			if (GodotObject.IsInstanceValid(s.ServerAgent)) s.ServerAgent.QueueFree();
			Broadcast(Packets.WritePlayerLeft(s.NetId, (byte)LeaveReason.Quit), DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: null);
			Dbg.Print($"[NetServer] Disconnect-Pool grace expired → freed netId={s.NetId}");
		}
	}

	/// <summary>Samples LiteNetLib byte counters every 500 ms and feeds NetStats with smoothed rates.</summary>
	private void SampleBandwidth()
	{
		if (_net == null) return;
		long now = (long)Time.GetTicksMsec();
		long dtMs = now - _lastStatsTickMs;
		if (dtMs < 500) return;
		_lastStatsTickMs = now;
		var s = _net.Statistics;
		long sentNow = (long)s.BytesSent;
		long recvNow = (long)s.BytesReceived;
		long dSent = sentNow - _lastBytesSent;
		long dRecv = recvNow - _lastBytesReceived;
		_lastBytesSent = sentNow;
		_lastBytesReceived = recvNow;
		NetStats.BytesPerSecUp = (int)(dSent * 1000L / dtMs);
		NetStats.BytesPerSecDown = (int)(dRecv * 1000L / dtMs);
	}

	/// <summary>Records each agent's authoritative tick position + bone-pose snapshot so lag-comp can rewind
	/// both the body and the animated hitbox transforms. Also runs the anti-cheat position-delta check.</summary>
	private void PushPositionsToRewind()
	{
		float maxMps = Mathf.Max(0.1f, ConVars.Sv.MaxClientPositionDeltaMps);
		float tickRate = Mathf.Max(1f, _cli.TickRate);
		foreach (var s in AllPeers)
		{
			if (s.ServerAgent == null) continue;
			Vector3 pos = s.ServerAgent.AuthorityPosition;
			s.Rewind.Push(_serverTick, pos);
			if (s.ServerAgent is NetworkPlayer pc) pc.PushBoneHistory(_serverTick);

			if (!s.IsBot && ConVars.Sv.AntiCheatEnabled && s.HasValidatedPos && _serverTick > s.LastValidatedTick && !s.ServerAgent.IsFrozen)
			{
				uint dTick = _serverTick - s.LastValidatedTick;
				float secs = (float)dTick / tickRate;
				float distance = pos.DistanceTo(s.LastValidatedPos);
				float mps = distance / Mathf.Max(0.0001f, secs);
				if (distance > maxMps * secs + 1.0f)
				{
					RegisterAntiCheatViolation(s, $"position-delta {distance:F2}m in {dTick}t ({mps:F1} m/s)");
				}
			}
			s.LastValidatedPos = pos;
			s.LastValidatedTick = _serverTick;
			s.HasValidatedPos = true;
		}
	}

	/// <summary>Refreshes cached round-trip times and back-references for each connected peer.</summary>
	private void UpdatePeerPings()
	{
		foreach (var kv in _peers)
		{
			kv.Value.LastPingMs = kv.Key.RoundTripTime;
			kv.Value.Peer = kv.Key;
		}
	}

	/// <summary>Lookup for ServerAgent → PeerState (e.g. from NetworkPlayer.HandleHitscan for lag-comp).</summary>
	public PeerState GetPeerStateForNetId(byte netId)
	{
		_peersByNetId.TryGetValue(netId, out var s);
		return s;
	}

	/// <summary>All active agent states (peers + bots) — anywhere "all hittable players" is meant. Concrete
	/// <c>List</c> so foreach uses the struct enumerator. Refilled per Poll via <see cref="RefreshAllPeersCache"/>.</summary>
	private readonly List<PeerState> _allPeersCache = new(32);

	/// <summary>Cached peers + bots, stable for the tick. Mid-Poll mutations re-call <see cref="RefreshAllPeersCache"/>.</summary>
	public List<PeerState> AllPeers => _allPeersCache;

	/// <summary>Rebuilds the AllPeers cache. Called per Poll and after any peer-state mutation.</summary>
	private void RefreshAllPeersCache()
	{
		_allPeersCache.Clear();
		foreach (var s in _peers.Values) _allPeersCache.Add(s);
		foreach (var s in _bots) _allPeersCache.Add(s);
	}

	/// <summary>Copies each peer's latest input into its ServerAgent and clears edge-triggered flags to prevent replay on packet loss.</summary>
	private void FeedInputsToAgents()
	{
		foreach (var state in _peers.Values)
		{
			if (state.ServerAgent == null) continue;
			if (!state.HasLatestInput) continue;
			state.ServerAgent.NetInputSource = state.LatestInput;
			state.LatestInput.JumpPressed = false;
			state.LatestInput.CrouchPressed = false;
			state.LatestInput.ReloadPressed = false;
			state.LatestInput.InspectPressed = false;
		}
	}

	private readonly System.Collections.Generic.List<SnapshotPlayer> _snapBuf = new();

	/// <summary>Sends a SnapshotPacket to every peer, every second tick (64 Hz against the 128 Hz sim).
	/// Per receiver: <c>ackedInputTick</c> = its last consumed input tick (for reconciliation); FoW/PVS or
	/// distance gate strips non-teammate players (teammates + self always included); delta-baseline
	/// compression emits only changed fields against the acked baseline in <see cref="PeerState.SentSnapshots"/>,
	/// falling back to a full snapshot when the baseline ages out (self-healing).
	/// <c>ActiveSlot</c> is live-read from the ServerAgent each tick so puppets see the thrower's grenade slot.</summary>
	private void BroadcastSnapshots()
	{
		if ((_serverTick & 1u) != 0u) return;
		if (_peers.Count == 0) return;

		_snapBuf.Clear();
		foreach (var s in AllPeers)
		{
			if (s.ServerAgent == null) continue;
			var agent = s.ServerAgent;
			var mc = agent.Movement;
			if (agent is NetworkPlayer agentCore) s.ActiveSlot = (byte)agentCore.ActiveSlot;
			byte flags = 0;
			if (mc.IsSliding)          flags |= (byte)SnapshotFlags.Sliding;
			if (mc.IsAirborne)         flags |= (byte)SnapshotFlags.Airborne;
			if (mc.IsReloading)        flags |= (byte)SnapshotFlags.Reloading;
			if (mc.ActuallySprinting)  flags |= (byte)SnapshotFlags.Sprinting;
			if (mc.IsWallClinging)     flags |= (byte)SnapshotFlags.WallClinging;
			if (mc.IsInspecting)       flags |= (byte)SnapshotFlags.Inspecting;
			if (s.WorldReady)          flags |= (byte)SnapshotFlags.WorldReady;
			if (s.Hp == 0)             flags |= (byte)SnapshotFlags.Dead;

			_snapBuf.Add(new SnapshotPlayer
			{
				NetId = s.NetId,
				Flags = flags,
				Pos = agent.AuthorityPosition,
				Vel = mc.Velocity,
				Yaw = agent.Rotation.Y,
				Pitch = (agent is NetworkPlayer lc1 && lc1.HeadPitch != null) ? lc1.HeadPitch.Rotation.X : 0f,
				AdsBlend = (byte)Mathf.Clamp(Mathf.RoundToInt(mc.AdsBlend * 255f), 0, 255),
				CrouchBlend = (byte)Mathf.Clamp(Mathf.RoundToInt(mc.CrouchBlend * 255f), 0, 255),
				RaiseBlend = (byte)Mathf.Clamp(Mathf.RoundToInt(mc.WeaponRaiseBlend * 255f), 0, 255),
				ShotIndex = (ushort)Mathf.Clamp(mc.ShotIndex, 0, ushort.MaxValue),
				Hp = s.Hp,
				Armor = s.Armor,
				ActiveSlot = s.ActiveSlot,
				WeaponId = s.WeaponId,
				AimPunchX = (sbyte)Mathf.Clamp(Mathf.RoundToInt(mc.AimPunch.X * 16f), -128, 127),
				AimPunchY = (sbyte)Mathf.Clamp(Mathf.RoundToInt(mc.AimPunch.Y * 16f), -128, 127),
				FootstepPhase = (ushort)Mathf.Clamp(Mathf.RoundToInt((agent is NetworkPlayer lc2 ? lc2.FootstepLogic.ContinuousPhase : 0f) / 2f * 65535f), 0, 65535),
				Kills = s.Kills,
				Deaths = s.Deaths,
				PingMs = (byte)Mathf.Clamp(s.LastPingMs, 0, 255),
				Team = (byte)s.Team,
				TeamSlot = s.TeamSlot,
			});
		}

		bool fowActive = ConVars.Sv.FogOfWar && _pvs.Built;
		float pvsCutoff = ConVars.Sv.PvsCutoffDistance;
		float pvsCutoffSq = pvsCutoff * pvsCutoff;
		bool distancePvsActive = !fowActive && pvsCutoff > 0.01f;
		foreach (var kv in _peers)
		{
			var peer = kv.Key;
			var state = kv.Value;
			if (state.HandshakePending) continue;
			uint ackedTick = state.ServerAgent != null ? state.ServerAgent.LastAppliedInputTick : 0u;
			_writeBuf.Reset();
			System.Collections.Generic.List<SnapshotPlayer> outBuf = _snapBuf;
			if ((fowActive || distancePvsActive) && state.ServerAgent != null)
			{
				outBuf = _perReceiverSnapBuf;
				outBuf.Clear();
				Vector3 rxPos = state.ServerAgent.AuthorityPosition;
				bool isDeathmatch = state.Team == Team.Deathmatch;
				for (int i = 0; i < _snapBuf.Count; i++)
				{
					var snap = _snapBuf[i];
					if (snap.NetId == state.NetId) { outBuf.Add(snap); continue; }
					bool isTeammate = !isDeathmatch && snap.Team == (byte)state.Team;
					if (isTeammate) { outBuf.Add(snap); continue; }
					if (fowActive)
					{
						if (!_pvs.CanSee(rxPos, snap.Pos)) { _fowStrippedPlayersWindow++; continue; }
					}
					else
					{
						float dx = snap.Pos.X - rxPos.X;
						float dy = snap.Pos.Y - rxPos.Y;
						float dz = snap.Pos.Z - rxPos.Z;
						if (dx * dx + dy * dy + dz * dz > pvsCutoffSq) continue;
					}
					outBuf.Add(snap);
				}
			}

			uint baselineTick = Packets.NoBaselineTick;
			SnapshotPlayer[] baselinePlayers = null;
			int baselineCount = 0;
			if (state.LastAckedSnapshotTick != Packets.NoBaselineTick)
			{
				var baseline = state.SentSnapshots.Find(state.LastAckedSnapshotTick);
				if (baseline != null)
				{
					baselineTick = baseline.Tick;
					baselinePlayers = baseline.Players;
					baselineCount = baseline.PlayerCount;
				}
			}
			Packets.WriteSnapshotInto(_writeBuf, _serverTick, ackedTick, baselineTick, outBuf, baselinePlayers, baselineCount);
			peer.Send(_writeBuf, ChannelUnreliable, DeliveryMethod.Unreliable);
			state.SentSnapshots.Push(_serverTick, outBuf);
		}
	}

	/// <summary>Reused per-receiver snapshot buffer for PVS filtering — single allocation, cleared/refilled per peer per tick.</summary>
	private readonly System.Collections.Generic.List<SnapshotPlayer> _perReceiverSnapBuf = new();

	/// <summary>Reused <see cref="NetDataWriter"/> for BroadcastSnapshots — avoids ~1k allocs/s on a full server.</summary>
	private readonly NetDataWriter _writeBuf = new();

	/// <summary>Scans the SpawnManager once world.tscn is active and applies headless settings on dedicated servers.</summary>
	private void TryScanSpawns()
	{
		if (_spawns.Initialized) return;
		if (Engine.GetMainLoop() is not SceneTree tree) return;
		if (tree.CurrentScene == null) return;
		if (tree.CurrentScene.Name != "World") return;
		_spawns.Scan(tree);
	}

	/// <summary>Voxel-PVS for line-of-sight Fog of War, built lazily once the world is loaded and
	/// <c>sv_fog_of_war</c> is on (checked independently of <see cref="TryScanSpawns"/> so a runtime toggle
	/// still triggers a build). Queried per-receiver per-snapshot/per-event to gate enemy visibility.</summary>
	private readonly VoxelPvs _pvs = new();

	private long _fowWaitDiagMs;
	private long _fowProgressDiagMs;
	private ulong _fowBuildStartUsec;

	/// <summary>Raycast budget per Poll while the PVS builds (~10ms hitch/tick); stays under the 30s loopback
	/// DisconnectTimeout so the client survives the build.</summary>
	private const int FoWBuildRaysPerPoll = 1000;

	private void TryBuildVoxelPvs()
	{
		if (_pvs.Built) return;
		if (!ConVars.Sv.FogOfWar) { _fowWaitDiagMs = 0; return; }

		if (_pvs.IsBuilding)
		{
			using (MiniProfiler.SampleServer("VoxelPvs.StepBuild"))
				_pvs.StepBuild(FoWBuildRaysPerPoll);
			long nowMs = (long)Time.GetTicksMsec();
			if (_fowProgressDiagMs == 0 || nowMs - _fowProgressDiagMs >= 2000)
			{
				_fowProgressDiagMs = nowMs;
				LogToServerAndClients($"[NetServer] FoW build progress: {_pvs.BuildProgress01 * 100f:F0}% ({_pvs.BuildRaysDone} rays done)");
			}
			if (_pvs.Built)
			{
				double elapsedSec = (Time.GetTicksUsec() - _fowBuildStartUsec) / 1_000_000.0;
				long visible = _pvs.CountVisible();
				int total = _pvs.TotalVoxels;
				double density = total > 0 ? (double)visible / ((double)total * total) : 0.0;
				LogToServerAndClients($"[NetServer] Fog of War ACTIVE — {_pvs.Dims.X}×{_pvs.Dims.Y}×{_pvs.Dims.Z}={total} voxels @ {_pvs.VoxelSize:F1}m, {_pvs.BuildRaysDone} rays in {elapsedSec:F1}s, {visible} visible pairs ({density * 100.0:F1}% density)");
			}
			return;
		}

		string blockReason = null;
		SceneTree tree = null;
		if (Engine.GetMainLoop() is SceneTree t) tree = t;
		else blockReason = "no SceneTree";

		string sceneName = tree?.CurrentScene?.Name;
		if (blockReason == null && tree.CurrentScene == null) blockReason = "no CurrentScene loaded";
		else if (blockReason == null && sceneName != "World") blockReason = $"CurrentScene='{sceneName}' (expected 'World')";

		var space = tree?.Root?.World3D?.DirectSpaceState;
		if (blockReason == null && space == null) blockReason = "no DirectSpaceState (physics not ready)";

		if (blockReason != null)
		{
			long now = (long)Time.GetTicksMsec();
			if (_fowWaitDiagMs == 0 || now - _fowWaitDiagMs >= 2000)
			{
				LogToServerAndClients($"[NetServer] FoW waiting to build: {blockReason}");
				_fowWaitDiagMs = now;
			}
			return;
		}

		var bakedInstance = FindVoxelPvsInstance(tree.CurrentScene);
		if (bakedInstance != null && bakedInstance.HasBakedData)
		{
			_pvs.LoadFromData(bakedInstance.Data);
			LogToServerAndClients($"[NetServer] Fog of War ACTIVE — loaded pre-baked PVS from scene VoxelPvsInstance ({_pvs.Dims.X}×{_pvs.Dims.Y}×{_pvs.Dims.Z}={_pvs.TotalVoxels} voxels @ {_pvs.VoxelSize:F1}m). No runtime build needed.");
			_fowWaitDiagMs = 0;
			return;
		}

		Aabb worldAabb = VoxelPvs.ComputeWorldAabb(tree.CurrentScene);
		float voxelSize = Mathf.Max(0.5f, ConVars.Sv.FowVoxelSize);
		_pvs.BeginBuild(space, worldAabb, voxelSize);
		_fowBuildStartUsec = Time.GetTicksUsec();
		_fowProgressDiagMs = 0;
		float autoTuned = _pvs.VoxelSize;
		string voxelTuneNote = Mathf.IsEqualApprox(autoTuned, voxelSize) ? "" : $" (auto-coarsened from {voxelSize:F1}m to fit voxel budget)";
		string bakeHint = bakedInstance == null
			? " Tip: add a VoxelPvsInstance node to the map scene and click 'Bake PVS' to pre-bake this once and skip the runtime build entirely."
			: " Tip: click 'Bake PVS' on the existing VoxelPvsInstance to skip this on future server starts.";
		LogToServerAndClients($"[NetServer] Building voxel PVS — scene='{sceneName}', AABB={worldAabb}, {_pvs.Dims.X}×{_pvs.Dims.Y}×{_pvs.Dims.Z}={_pvs.TotalVoxels} voxels @ {autoTuned:F1}m{voxelTuneNote}. Spreading raycasts across multiple ticks ({FoWBuildRaysPerPoll}/poll) — server stays responsive.{bakeHint}");
		_fowWaitDiagMs = 0;
	}

	/// <summary>Finds the first <see cref="VoxelPvsInstance"/> under <paramref name="root"/>, or null (runtime-build fallback).</summary>
	private static VoxelPvsInstance FindVoxelPvsInstance(Node root)
	{
		if (root == null) return null;
		if (root is VoxelPvsInstance inst) return inst;
		foreach (var child in root.GetChildren())
		{
			var found = FindVoxelPvsInstance(child);
			if (found != null) return found;
		}
		return null;
	}

	private int _fowStrippedPlayersWindow;
	private int _fowStrippedEventsWindow;
	private long _fowReportTickMs;

	private void ReportFoWActivityIfDue()
	{
		if (!ConVars.Sv.FogOfWar || !_pvs.Built) return;
		long now = (long)Time.GetTicksMsec();
		if (_fowReportTickMs == 0) { _fowReportTickMs = now; return; }
		if (now - _fowReportTickMs < 5000) return;
		float intervalSec = (now - _fowReportTickMs) / 1000f;
		if (_fowStrippedPlayersWindow > 0 || _fowStrippedEventsWindow > 0)
		{
			GD.Print($"[NetServer] FoW activity (last {intervalSec:F0}s): {_fowStrippedPlayersWindow} snapshot-player strips, {_fowStrippedEventsWindow} event-broadcast strips");
		}
		_fowStrippedPlayersWindow = 0;
		_fowStrippedEventsWindow = 0;
		_fowReportTickMs = now;
	}

	/// <summary>Stops the UDP listener and clears the server-running NetStats flag.</summary>
	public void Stop()
	{
		_net?.Stop();
		_net = null;
		NetStats.ServerRunning = false;
	}

	/// <summary>Accepts incoming connections that supply the correct protocol key, rejecting when full.</summary>
	private void OnConnectionRequest(ConnectionRequest request)
	{
		if (PeerCount >= _cli.MaxPlayers)
		{
			Dbg.Print($"[NetServer] Reject — server full ({PeerCount}/{_cli.MaxPlayers})");
			request.Reject();
			return;
		}
		request.AcceptIfKey(ProtocolKey);
	}

	/// <summary>Notes the transport connection; the real handshake follows on ConnectRequest.</summary>
	private void OnPeerConnected(NetPeer peer)
	{
		Dbg.Print($"[NetServer] Peer transport ready: id={peer.Id} from={peer.Address}  (transport-level)");
	}

	/// <summary>Moves the peer's state into the reconnect grace pool, or frees it immediately when no token is present.</summary>
	private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
	{
		if (_peers.TryGetValue(peer, out var state))
		{
			Dbg.Print($"[NetServer] Peer left: netId={state.NetId} name=\"{state.PlayerName}\" reason={info.Reason} (grace pool {_cli.ReconnectGraceSec:F0}s)");
			_peers.Remove(peer);
			string tokenKey = state.Token != null ? System.Text.Encoding.UTF8.GetString(state.Token) : null;
			if (!string.IsNullOrEmpty(tokenKey))
			{
				if (state.ServerAgent != null) state.ServerAgent.IsFrozen = true;
				state.DisconnectedAtTickMs = (long)Time.GetTicksMsec();
				_disconnectedPool[tokenKey] = state;
				Broadcast(Packets.WritePlayerLeft(state.NetId, (byte)LeaveReason.Timeout), DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: null);
			}
			else
			{
				_peersByNetId.Remove(state.NetId);
				if (GodotObject.IsInstanceValid(state.ServerAgent)) state.ServerAgent.QueueFree();
				state.ServerAgent = null;
				Broadcast(Packets.WritePlayerLeft(state.NetId, (byte)LeaveReason.Quit), DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: null);
			}
			EnsureBotFill();
		}
		else
		{
			Dbg.Print($"[NetServer] Pre-handshake peer disconnected: id={peer.Id} reason={info.Reason}");
		}
	}

	/// <summary>Logs LiteNetLib socket-level errors.</summary>
	private void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError error)
	{
		GD.PushWarning($"[NetServer] Network error from {endPoint}: {error}");
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
			case PacketType.ConnectRequest:
				HandleConnectRequest(peer, reader);
				break;
			case PacketType.Input:
				HandleInput(peer, reader);
				break;
			case PacketType.GrenadeSpawn:
				HandleGrenadeSpawn(peer, reader);
				break;
			case PacketType.ProjectileState:
				HandleProjectileState(peer, reader);
				break;
			case PacketType.ProjectileDespawn:
				HandleProjectileDespawn(peer, reader);
				break;
			case PacketType.ConVarSyncRequest:
				HandleConVarSyncRequest(peer, reader);
				break;
			case PacketType.WorldInitComplete:
				HandleWorldInitComplete(peer);
				break;
			case PacketType.TeamSelect:
				HandleTeamSelect(peer, reader);
				break;
			default:
				break;
		}
		reader.Recycle();
	}

	/// <summary>Relays a client-initiated grenade spawn to other peers with the NetId rewritten to prevent spoofing.</summary>
	private void HandleGrenadeSpawn(NetPeer sender, NetPacketReader r)
	{
		if (!_peers.TryGetValue(sender, out var state)) return;
		Packets.ReadGrenadeSpawn(r, out _, out uint projectileId, out byte grenadeType, out Vector3 origin, out Vector3 velocity);
		var writer = Packets.WriteGrenadeSpawn(state.NetId, projectileId, grenadeType, origin, velocity);
		Broadcast(writer, DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: sender);
	}

	/// <summary>Relays the owner's periodic projectile position/velocity update to other peers (unreliable, NetId rewritten).</summary>
	private void HandleProjectileState(NetPeer sender, NetPacketReader r)
	{
		if (!_peers.TryGetValue(sender, out var state)) return;
		Packets.ReadProjectileState(r, out _, out uint projectileId, out Vector3 pos, out Vector3 vel);
		var writer = Packets.WriteProjectileState(state.NetId, projectileId, pos, vel);
		Broadcast(writer, DeliveryMethod.Unreliable, ChannelUnreliable, excludePeer: sender);
	}

	/// <summary>Relays the owner's projectile despawn signal to other peers so they can finalize the visual.</summary>
	private void HandleProjectileDespawn(NetPeer sender, NetPacketReader r)
	{
		if (!_peers.TryGetValue(sender, out var state)) return;
		Packets.ReadProjectileDespawn(r, out _, out uint projectileId, out Vector3 finalPos);
		var writer = Packets.WriteProjectileDespawn(state.NetId, projectileId, finalPos);
		Broadcast(writer, DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: sender);
	}

	/// <summary>Client finished its world preload — sets WorldReady on its PeerState so subsequent snapshots
	/// emit <see cref="SnapshotFlags.WorldReady"/> and peers show its TPS body.</summary>
	private void HandleWorldInitComplete(NetPeer sender)
	{
		if (!_peers.TryGetValue(sender, out var state)) return;
		if (state.WorldReady) return;
		state.WorldReady = true;
		Dbg.Print($"[NetServer] WorldInitComplete netId={state.NetId} name=\"{state.PlayerName}\" — TPS now visible to peers");
	}

	/// <summary>Competitive client picked CT/T. Rejects Spectator/Deathmatch/already-in-team (stale reconnect
	/// or lag). On success: assigns team, allocates a spawn pose, ensures a ServerAgent, and replies with
	/// <see cref="PacketType.SpawnAuthorize"/>.</summary>
	private void HandleTeamSelect(NetPeer sender, NetPacketReader r)
	{
		if (!_peers.TryGetValue(sender, out var state)) return;
		Team chosen = Packets.ReadTeamSelect(r);
		if (chosen != Team.Team1 && chosen != Team.Team2)
		{
			Dbg.Print($"[NetServer] TeamSelect from netId={state.NetId} → invalid team {chosen}, dropped");
			return;
		}
		if (state.Team != Team.Spectator)
		{
			Dbg.Print($"[NetServer] TeamSelect from netId={state.NetId} ignored — already in team {state.Team}");
			return;
		}
		state.Team = chosen;
		state.TeamSlot = AssignFreeTeamSlot(chosen);

		var tree = Engine.GetMainLoop() as SceneTree;
		if (tree?.CurrentScene == null || tree.CurrentScene.Name != "World" || !_spawns.Initialized)
		{
			Dbg.Print($"[NetServer] TeamSelect netId={state.NetId}: world not ready, deferring spawn");
			return;
		}
		_playersContainer ??= tree.CurrentScene.GetNodeOrNull<Node3D>("Players");

		var occupied = new System.Collections.Generic.List<Vector3>();
		foreach (var s in AllPeers)
		{
			if (s == state || s.HandshakePending) continue;
			occupied.Add(s.ServerAgent != null && GodotObject.IsInstanceValid(s.ServerAgent)
				? s.ServerAgent.GlobalPosition
				: s.SpawnPos);
		}
		var (spawnPos, spawnYaw) = _spawns.PickFreeSpawn(state.Team, occupied);
		spawnPos = GroundSnap(spawnPos);
		state.SpawnPos = spawnPos;
		state.SpawnYaw = spawnYaw;
		EnsureServerAgent(state);

		sender.Send(Packets.WriteSpawnAuthorize(state.Team, spawnPos, spawnYaw),
			DeliveryMethod.ReliableOrdered);
		Dbg.Print($"[NetServer] TeamSelect netId={state.NetId} → team={state.Team} spawn={spawnPos} yaw={spawnYaw:F2}");
	}

	/// <summary>Client requests an sv_* ConVar set; server validates the prefix + whitelist, applies, broadcasts.</summary>
	private void HandleConVarSyncRequest(NetPeer sender, NetPacketReader r)
	{
		Packets.ReadConVarSyncRequest(r, out string name, out string value);
		string nameLow = name?.ToLowerInvariant() ?? "";
		if (!nameLow.StartsWith("sv_"))
		{
			GD.PushWarning($"[NetServer] ConVarSync ignored (no sv_* prefix): '{name}'");
			return;
		}
		if (!ConVars.TrySet(nameLow, value ?? ""))
		{
			GD.PushWarning($"[NetServer] ConVarSync failed to apply: {nameLow} = {value}");
			return;
		}
		GD.Print($"[NetServer] ConVarSync apply + broadcast: {nameLow} = {value}");
		BroadcastConVarSync(nameLow, value);
	}

	/// <summary>Broadcasts a ConVar change to all clients.</summary>
	private void BroadcastConVarSync(string name, string value)
	{
		var writer = Packets.WriteConVarSyncBroadcast(name, value);
		Broadcast(writer, DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: null);
	}

	/// <summary>Sends all sv_* ConVar values to one peer after SpawnAck so a reconnect keeps its debug-toggle state.</summary>
	public void SendInitialConVarSync(NetPeer peer)
	{
		peer.Send(Packets.WriteConVarSyncBroadcast("sv_debug_hitboxes", ConVars.Sv.DebugHitboxes ? "1" : "0"), ChannelReliable, DeliveryMethod.ReliableOrdered);
		peer.Send(Packets.WriteConVarSyncBroadcast("sv_debug_capsule",  ConVars.Sv.DebugCapsule  ? "1" : "0"), ChannelReliable, DeliveryMethod.ReliableOrdered);
		peer.Send(Packets.WriteConVarSyncBroadcast("sv_debug_aimray",   ConVars.Sv.DebugAimRay   ? "1" : "0"), ChannelReliable, DeliveryMethod.ReliableOrdered);
		peer.Send(Packets.WriteConVarSyncBroadcast("sv_debug_bullets",  ConVars.Sv.DebugBullets  ? "1" : "0"), ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Validates the ConnectRequest, resumes a matching disconnect-pool entry or allocates a fresh peer.
	/// On resume the per-peer snapshot baseline is cleared so the first post-reconnect snapshot is full (the
	/// fresh NetClient can't decode deltas against the pre-disconnect baseline).</summary>
	private void HandleConnectRequest(NetPeer peer, NetPacketReader r)
	{
		if (_peers.ContainsKey(peer))
		{
			GD.PushWarning($"[NetServer] Duplicate ConnectRequest from peer.id={peer.Id} — ignored");
			return;
		}

		Packets.ReadConnectRequest(r, out ushort proto, out string name, out byte[] token);
		if (proto != Packets.ProtocolVersion)
		{
			GD.PushWarning($"[NetServer] Protocol mismatch: client={proto}, server={Packets.ProtocolVersion} — disconnect");
			peer.Disconnect();
			return;
		}

		string tokenKey = token != null && token.Length > 0 ? System.Text.Encoding.UTF8.GetString(token) : null;
		if (!string.IsNullOrEmpty(tokenKey) && _disconnectedPool.TryGetValue(tokenKey, out var pooled))
		{
			_disconnectedPool.Remove(tokenKey);
			pooled.Peer = peer;
			pooled.PlayerName = string.IsNullOrEmpty(name) ? pooled.PlayerName : name;
			pooled.HandshakePending = false;
			if (pooled.ServerAgent != null) pooled.ServerAgent.IsFrozen = false;
			pooled.LastAckedSnapshotTick = Packets.NoBaselineTick;
			pooled.SentSnapshots.Clear();
			_peers[peer] = pooled;
			_peersByNetId[pooled.NetId] = pooled;
			Dbg.Print($"[NetServer] RECONNECT netId={pooled.NetId} name=\"{pooled.PlayerName}\"");

			Vector3 nowPos = pooled.ServerAgent?.GlobalPosition ?? pooled.SpawnPos;
			float nowYaw = pooled.ServerAgent?.Rotation.Y ?? pooled.SpawnYaw;
			var othersReconnect = new List<InitialPlayerState>();
			foreach (var kv in _peers)
			{
				if (kv.Key == peer) continue;
				var s = kv.Value;
				othersReconnect.Add(new InitialPlayerState
				{
					NetId = s.NetId, PlayerName = s.PlayerName,
					Position = s.ServerAgent?.GlobalPosition ?? s.SpawnPos,
					Yaw = s.ServerAgent?.Rotation.Y ?? s.SpawnYaw,
					Hp = s.Hp, ActiveSlot = s.ActiveSlot, WeaponId = s.WeaponId,
					Team = (byte)s.Team, TeamSlot = s.TeamSlot,
				});
			}
			peer.Send(Packets.WriteSpawnAck(pooled.NetId, pooled.Team, "res://world.tscn", _serverTick, (ushort)_cli.TickRate,
				nowPos, nowYaw, othersReconnect, pooled.Token), ChannelReliable, DeliveryMethod.ReliableOrdered);
			Broadcast(Packets.WritePlayerJoined(pooled.NetId, pooled.PlayerName, nowPos, nowYaw, pooled.Hp, pooled.ActiveSlot, pooled.WeaponId, (byte)pooled.Team, pooled.TeamSlot),
				DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: peer);
			return;
		}

		if (token == null || token.Length < 4)
		{
			token = System.Text.Encoding.UTF8.GetBytes(System.Guid.NewGuid().ToString());
			Dbg.Print($"[NetServer] Identity assigned to new peer (no token sent)");
		}

		byte netId = AllocateNetId();
		if (netId == 0)
		{
			GD.PushError("[NetServer] No free NetId left — disconnect");
			peer.Disconnect();
			return;
		}

		Team team;
		if (_cli.GameMode == GameMode.Deathmatch)
		{
			team = Team.Deathmatch;
		}
		else
		{
			team = Team.Spectator;
		}

		var state = new PeerState
		{
			NetId = netId,
			PlayerName = string.IsNullOrEmpty(name) ? $"Player_{netId}" : name,
			Token = token,
			Team = team,
			TeamSlot = AssignFreeTeamSlot(team),
			Hp = 100,
			Armor = 50,
			ActiveSlot = 0,
			WeaponId = 0,
			Peer = peer,
			HandshakePending = true,
		};
		_peers[peer] = state;
		_peersByNetId[netId] = state;
		Dbg.Print($"[NetServer] Handshake accepted (deferred): netId={netId} name=\"{state.PlayerName}\" token={token.Length}b");

		TryFinalizeHandshake(state);
	}

	/// <summary>Retries pending handshakes each poll iteration once the world is loaded and creates missing ServerAgents.</summary>
	private void FinalizePendingHandshakes()
	{
		foreach (var s in _peers.Values)
		{
			if (s.HandshakePending) TryFinalizeHandshake(s);
			else if (s.ServerAgent == null && s.Team != Team.Spectator) EnsureServerAgent(s);
		}
	}

	/// <summary>Finalises one pending handshake once the world is loaded: picks a spawn slot, sends SpawnAck,
	/// the initial sv_* ConVar sync, and broadcasts PlayerJoined.</summary>
	private void TryFinalizeHandshake(PeerState state)
	{
		if (!state.HandshakePending) return;
		if (state.Peer == null) return;

		var tree = Engine.GetMainLoop() as SceneTree;
		if (tree?.CurrentScene == null || tree.CurrentScene.Name != "World") return;
		if (!_spawns.Initialized) return;
		_playersContainer ??= tree.CurrentScene.GetNodeOrNull<Node3D>("Players");
		if (_playersContainer == null) return;

		Vector3 spawnPos;
		float spawnYaw;
		if (state.Team == Team.Spectator)
		{
			spawnPos = Vector3.Zero;
			spawnYaw = 0f;
			state.SpawnPos = spawnPos;
			state.SpawnYaw = spawnYaw;
		}
		else
		{
			var occupied = new List<Vector3>();
			foreach (var s in AllPeers)
			{
				if (s == state || s.HandshakePending) continue;
				occupied.Add(s.ServerAgent != null && GodotObject.IsInstanceValid(s.ServerAgent)
					? s.ServerAgent.GlobalPosition
					: s.SpawnPos);
			}
			(spawnPos, spawnYaw) = _spawns.PickFreeSpawn(state.Team, occupied);
			spawnPos = GroundSnap(spawnPos);
			state.SpawnPos = spawnPos;
			state.SpawnYaw = spawnYaw;

			EnsureServerAgent(state);
		}

		var others = new List<InitialPlayerState>();
		foreach (var kv in _peers)
		{
			if (kv.Key == state.Peer) continue;
			var s = kv.Value;
			if (s.HandshakePending) continue;
			others.Add(new InitialPlayerState
			{
				NetId = s.NetId,
				PlayerName = s.PlayerName,
				Position = s.ServerAgent != null && GodotObject.IsInstanceValid(s.ServerAgent) ? s.ServerAgent.GlobalPosition : s.SpawnPos,
				Yaw = s.ServerAgent != null && GodotObject.IsInstanceValid(s.ServerAgent) ? s.ServerAgent.Rotation.Y : s.SpawnYaw,
				Hp = s.Hp,
				ActiveSlot = s.ActiveSlot,
				WeaponId = s.WeaponId,
				Team = (byte)s.Team,
				TeamSlot = s.TeamSlot,
			});
		}

		var ackWriter = Packets.WriteSpawnAck(state.NetId, state.Team, "res://world.tscn", _serverTick, (ushort)_cli.TickRate, spawnPos, spawnYaw, others, state.Token);
		state.Peer.Send(ackWriter, ChannelReliable, DeliveryMethod.ReliableOrdered);

		SendInitialConVarSync(state.Peer);

		var joinedWriter = Packets.WritePlayerJoined(state.NetId, state.PlayerName, spawnPos, spawnYaw, state.Hp, state.ActiveSlot, state.WeaponId, (byte)state.Team, state.TeamSlot);
		foreach (var kv in _peers)
		{
			if (kv.Key == state.Peer) continue;
			if (kv.Value.HandshakePending) continue;
			kv.Key.Send(joinedWriter, ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		state.HandshakePending = false;
		Dbg.Print($"[NetServer] Handshake finalized: netId={state.NetId} spawn={spawnPos} yaw={spawnYaw:F2}");

		EnsureBotFill();
	}

	private static readonly string[] BotNameOptions = new[]
	{
		"Ghost", "Phantom", "Reaper", "Hunter", "Ninja", "Wolf", "Falcon", "Viper",
		"Raven", "Shadow", "Hawk", "Cobra", "Wraith", "Spectre", "Fang", "Echo",
		"Bruno", "Klaus", "Heinz", "Otto", "Wilhelm", "Friedrich",
	};
	private static int _botNameSeed = 0;
	/// <summary>Returns the next bot display name via a deterministic round-robin over <see cref="BotNameOptions"/>.</summary>
	private static string PickBotName()
	{
		string n = BotNameOptions[_botNameSeed % BotNameOptions.Length];
		_botNameSeed++;
		return $"[Bot] {n}";
	}

	/// <summary>Adjusts the bot count up or down so that it matches the configured target, respecting spawn slot and player caps.</summary>
	public void EnsureBotFill()
	{
		if (!_spawns.Initialized) return;

		int realPeers = 0;
		foreach (var s in _peers.Values)
			if (!s.HandshakePending) realPeers++;

		int activeBots = 0;
		foreach (var b in _bots)
			if (!b.PendingRemoval) activeBots++;

		bool isRoundMode = _spawns.CtCount > 0 || _spawns.TCount > 0;
		int relevantSpawns = isRoundMode ? (_spawns.CtCount + _spawns.TCount) : _spawns.DmCount;
		int spawnSlotsRemaining = Mathf.Max(0, relevantSpawns - realPeers);
		int playerSlotsRemaining = Mathf.Max(0, _cli.MaxPlayers - realPeers);
		int targetBots = Mathf.Min(_cli.MaxBots, Mathf.Min(spawnSlotsRemaining, playerSlotsRemaining));
		int diff = targetBots - activeBots;

		Dbg.Print($"[NetServer] EnsureBotFill: maxBots={_cli.MaxBots} spawns(CT={_spawns.CtCount} T={_spawns.TCount} DM={_spawns.DmCount}) realPeers={realPeers} activeBots={activeBots} target={targetBots} diff={diff}");

		if (diff > 0)
		{
			for (int i = 0; i < diff; i++) SpawnBot();
		}
		else if (diff < 0)
		{
			int toRemove = -diff;
			var removeList = new List<PeerState>();
			foreach (var b in _bots)
			{
				if (removeList.Count >= toRemove) break;
				if (!b.PendingRemoval) removeList.Add(b);
			}
			foreach (var b in removeList) RemoveBot(b);
		}
	}

	/// <summary>Spawns a single bot as a server-driven test dummy with a ServerAgent and HitboxRig.</summary>
	public void SpawnBot()
	{
		var tree = Engine.GetMainLoop() as SceneTree;
		if (tree?.CurrentScene == null || tree.CurrentScene.Name != "World") return;
		if (!_spawns.Initialized) return;

		byte netId = AllocateNetId();
		if (netId == 0)
		{
			GD.PushWarning("[NetServer] SpawnBot: no free NetIds");
			return;
		}

		Team team;
		if (_spawns.CtCount > 0 && _spawns.TCount > 0)
		{
			int ctBots = 0, tBots = 0;
			foreach (var b in _bots)
				if (!b.PendingRemoval) { if (b.Team == Team.Team1) ctBots++; else if (b.Team == Team.Team2) tBots++; }
			team = ctBots <= tBots ? Team.Team1 : Team.Team2;
		}
		else if (_spawns.CtCount > 0) team = Team.Team1;
		else if (_spawns.TCount > 0) team = Team.Team2;
		else team = Team.Deathmatch;

		var occupied = new List<Vector3>();
		foreach (var other in AllPeers)
			if (other.ServerAgent != null) occupied.Add(other.ServerAgent.GlobalPosition);
		var (spawnPos, spawnYaw) = _spawns.PickFreeSpawn(team, occupied);
		spawnPos = GroundSnap(spawnPos);

		var bot = new PeerState
		{
			NetId = netId,
			PlayerName = PickBotName(),
			Token = System.Array.Empty<byte>(),
			Team = team,
			TeamSlot = AssignFreeTeamSlot(team),
			Hp = 100,
			Armor = 50,
			ActiveSlot = 0,
			WeaponId = 0,
			Peer = null,
			HandshakePending = false,
			SpawnPos = spawnPos,
			SpawnYaw = spawnYaw,
			IsBot = true,
			WorldReady = true,
		};
		_bots.Add(bot);
		_peersByNetId[netId] = bot;
		EnsureServerAgent(bot);

		if (bot.ServerAgent != null)
		{
			bot.ServerAgent.NetInputSource = new InputPacket
			{
				TickIndex = 0,
				ViewYaw = spawnYaw,
				ViewPitch = 0f,
			};
			if (bot.ServerAgent is NetworkPlayer botBody)
				botBody.BotController.Init(spawnPos, spawnYaw, _serverTick);
		}

		var joinedWriter = Packets.WritePlayerJoined(bot.NetId, bot.PlayerName, spawnPos, spawnYaw, bot.Hp, bot.ActiveSlot, bot.WeaponId, (byte)bot.Team, bot.TeamSlot);
		foreach (var kv in _peers)
			if (!kv.Value.HandshakePending)
				kv.Key.Send(joinedWriter, ChannelReliable, DeliveryMethod.ReliableOrdered);

		Dbg.Print($"[NetServer] Bot spawned: netId={netId} name=\"{bot.PlayerName}\" at {spawnPos}");
	}

	/// <summary>Despawns a bot: frees its ServerAgent, removes it from internal lists and sends PlayerLeft to all real peers.</summary>
	private void RemoveBot(PeerState bot)
	{
		if (bot.ServerAgent != null && GodotObject.IsInstanceValid(bot.ServerAgent))
			bot.ServerAgent.QueueFree();
		_bots.Remove(bot);
		_peersByNetId.Remove(bot.NetId);

		var leftWriter = Packets.WritePlayerLeft(bot.NetId, (byte)LeaveReason.Quit);
		foreach (var kv in _peers)
			if (!kv.Value.HandshakePending)
				kv.Key.Send(leftWriter, ChannelReliable, DeliveryMethod.ReliableOrdered);

		Dbg.Print($"[NetServer] Bot removed: netId={bot.NetId} name=\"{bot.PlayerName}\"");
	}

	/// <summary>Obstacle probe mask for bot steering: world geometry (bit 0) + server agents (bit 4).</summary>
	private const uint BotProbeMask = 1u | (1u << 4);
	/// <summary>Reused list of Zone/BombSpot centres = the bot controller's long-range target pool.
	/// Rebuilt only when its size changes (map reload).</summary>
	private readonly List<Vector3> _botTargetCandidates = new();

	private IReadOnlyList<Vector3> ResolveBotTargets()
	{
		var level = World.Level;
		if (level == null) return _botTargetCandidates;
		int expected = level.Zones.Count + level.BombSpots.Count;
		if (_botTargetCandidates.Count == expected && expected > 0) return _botTargetCandidates;
		_botTargetCandidates.Clear();
		foreach (var z in level.Zones) _botTargetCandidates.Add(z.GlobalPosition);
		foreach (var bs in level.BombSpots) _botTargetCandidates.Add(bs.GlobalPosition);
		return _botTargetCandidates;
	}

	/// <summary>Per-tick AI driver for all bots, run just before <c>FeedInputsToAgents</c> so the next physics
	/// step picks up the fresh InputPacket. Skips dead/frozen bots and non-NetworkPlayer bodies.</summary>
	private void UpdateBotInputs()
	{
		if (_bots.Count == 0) return;
		var targets = ResolveBotTargets();
		int difficulty = Mathf.Clamp(ConVars.Sv.BotDifficulty, 0, 3);
		int tickRate = Mathf.Max(1, _cli.TickRate);
		foreach (var bot in _bots)
		{
			if (bot.PendingRemoval) continue;
			var agent = bot.ServerAgent;
			if (agent == null || agent.IsDead || agent.IsFrozen) continue;
			if (agent is not NetworkPlayer botBody) continue;
			var world = agent.GetWorld3D();
			var space = world?.DirectSpaceState;
			var navMap = world?.NavigationMap ?? new Rid();
			Vector3 pos = agent.GlobalPosition;
			var combat = new BotCombatContext
			{
				AllPeers = _allPeersCache,
				OwnNetId = bot.NetId,
				OwnTeam = bot.Team,
				SelfBodyRid = agent.GetRid(),
				Difficulty = difficulty,
				TickRate = tickRate,
				NeedsReload = (agent is NetworkPlayer pc) && pc.NeedsReload,
			};
			agent.NetInputSource = botBody.BotController.Tick(_serverTick, pos, navMap, targets, space, BotProbeMask, combat);
		}
	}

	/// <summary>Signed shortest-arc yaw delta in radians, wrapping across 0/2π. Result in [-π, π].</summary>
	private static float ShortestYawDelta(float a, float b)
	{
		float d = a - b;
		while (d > Mathf.Pi) d -= Mathf.Tau;
		while (d < -Mathf.Pi) d += Mathf.Tau;
		return d;
	}

	/// <summary>Records an anti-cheat violation: pushes a timestamp into the sliding-window ring, bumps the
	/// lifetime counter, and kicks when the windowed count exceeds the threshold and auto-kick is on.</summary>
	private void RegisterAntiCheatViolation(PeerState state, string reason)
	{
		if (!ConVars.Sv.AntiCheatEnabled) return;
		long now = (long)Time.GetTicksMsec();
		state.RecentViolationMs[state.RecentViolationHead] = now;
		state.RecentViolationHead = (state.RecentViolationHead + 1) % state.RecentViolationMs.Length;
		state.AntiCheatViolations++;

		int windowMs = Mathf.Max(1000, ConVars.Sv.AntiCheatViolationWindowMs);
		int recent = 0;
		for (int i = 0; i < state.RecentViolationMs.Length; i++)
		{
			if (state.RecentViolationMs[i] > 0 && now - state.RecentViolationMs[i] <= windowMs) recent++;
		}

		GD.Print($"[anti-cheat] netId={state.NetId} name=\"{state.PlayerName}\" {reason} (lifetime={state.AntiCheatViolations}, recent={recent}/{ConVars.Sv.AntiCheatKickThreshold})");

		if (ConVars.Sv.AntiCheatAutoKick && !state.AntiCheatKicked && recent >= ConVars.Sv.AntiCheatKickThreshold)
		{
			state.AntiCheatKicked = true;
			GD.PushWarning($"[anti-cheat] KICK netId={state.NetId} name=\"{state.PlayerName}\" — {recent} violations in {windowMs}ms window");
			state.Peer?.Disconnect();
		}
	}

	/// <summary>Validates and stores the peer's latest input bundle and unfreezes its ServerAgent on first input.
	/// The packet carries the last N inputs (oldest→newest); dedupes by tickIndex and OR-merges edge intents
	/// (Jump/Crouch/Reload/Inspect) across the bundle so a press-edge in a middle input isn't lost. Continuous
	/// fields take the newest values. FireSubTick is propagated from the most recent input with it &gt; 0.
	/// Snapshot-ack is per-packet; max()-guarded against out-of-order unreliable inputs.</summary>
	private void HandleInput(NetPeer peer, NetPacketReader r)
	{
		if (!_peers.TryGetValue(peer, out var state))
		{
			return;
		}

		if (state.LastPacketCountServerTick != _serverTick)
		{
			state.LastPacketCountServerTick = _serverTick;
			state.PacketsThisServerTick = 0;
		}
		state.PacketsThisServerTick++;
		int maxPackets = Mathf.Max(1, ConVars.Sv.MaxClientPacketsPerServerTick);
		if (state.PacketsThisServerTick > maxPackets)
		{
			if (state.PacketsThisServerTick == maxPackets + 1)
				RegisterAntiCheatViolation(state, $"packet-flood {state.PacketsThisServerTick} pkts in 1 tick");
			return;
		}

		Packets.ReadInputHeader(r, out byte inputCount, out uint ackedSnapshotTick);

		if (ackedSnapshotTick != Packets.NoBaselineTick && ackedSnapshotTick > state.LastAckedSnapshotTick)
			state.LastAckedSnapshotTick = ackedSnapshotTick;

		bool anyNewInput = false;
		InputPacket newest = default;
		bool jumpMerge = state.HasLatestInput && state.LatestInput.JumpPressed;
		bool crouchMerge = state.HasLatestInput && state.LatestInput.CrouchPressed;
		bool reloadMerge = state.HasLatestInput && state.LatestInput.ReloadPressed;
		bool inspectMerge = state.HasLatestInput && state.LatestInput.InspectPressed;
		byte fireSubTickPropagated = 0;

		for (int i = 0; i < inputCount; i++)
		{
			Packets.ReadInputBody(r, out InputPacket pkt);

			float wishLen2 = pkt.WishX * pkt.WishX + pkt.WishZ * pkt.WishZ;
			if (wishLen2 > 1.1f * 1.1f)
			{
				float inv = 1f / Mathf.Sqrt(wishLen2);
				pkt.WishX *= inv;
				pkt.WishZ *= inv;
				RegisterAntiCheatViolation(state, $"wish magnitude {Mathf.Sqrt(wishLen2):F2}");
			}
			pkt.ViewPitch = Mathf.Clamp(pkt.ViewPitch, -Mathf.Pi * 0.5f, Mathf.Pi * 0.5f);

			if (pkt.TickIndex > _serverTick + (uint)Mathf.Max(0, ConVars.Sv.MaxClientTickAheadOfServer))
			{
				RegisterAntiCheatViolation(state, $"tick-too-future client={pkt.TickIndex} server={_serverTick}");
				continue;
			}

			if (state.HasViewYawSample && pkt.TickIndex > state.LastViewYawSampleTick)
			{
				uint dTick = pkt.TickIndex - state.LastViewYawSampleTick;
				float secs = (float)dTick / Mathf.Max(1f, _cli.TickRate);
				float dYaw = Mathf.Abs(ShortestYawDelta(pkt.ViewYaw, state.LastViewYawSample));
				float rate = dYaw / Mathf.Max(0.0001f, secs);
				if (rate > ConVars.Sv.MaxClientYawRateRadPerSec)
				{
					RegisterAntiCheatViolation(state, $"yaw-rate {Mathf.RadToDeg(rate):F0}°/s over {dTick}t");
				}
			}
			state.LastViewYawSample = pkt.ViewYaw;
			state.LastViewYawSampleTick = pkt.TickIndex;
			state.HasViewYawSample = true;

			uint last = state.LastInputTick;
			if (last != 0 && pkt.TickIndex <= last && (last - pkt.TickIndex) < 256u)
				continue;

			state.LastInputTick = pkt.TickIndex;
			jumpMerge    |= pkt.JumpPressed;
			crouchMerge  |= pkt.CrouchPressed;
			reloadMerge  |= pkt.ReloadPressed;
			inspectMerge |= pkt.InspectPressed;
			if (pkt.FireSubTick > 0) fireSubTickPropagated = pkt.FireSubTick;
			newest = pkt;
			anyNewInput = true;
		}

		if (!anyNewInput) return;

		newest.JumpPressed    = jumpMerge;
		newest.CrouchPressed  = crouchMerge;
		newest.ReloadPressed  = reloadMerge;
		newest.InspectPressed = inspectMerge;
		if (newest.FirePressed && fireSubTickPropagated > 0)
			newest.FireSubTick = fireSubTickPropagated;

		if (state.ServerAgent != null && state.ServerAgent.IsFrozen)
		{
			state.ServerAgent.IsFrozen = false;
			Dbg.Print($"[NetServer] ServerAgent {state.NetId} unfrozen — first input packet received (tick={newest.TickIndex})");
		}
		state.LatestInput = newest;
		state.HasLatestInput = true;
		state.InputPacketsReceived++;
	}

	/// <summary>Spawns a ServerAgent into the Players container; idempotent and retried per poll until the world is ready.</summary>
	private void EnsureServerAgent(PeerState state)
	{
		if (state.ServerAgent != null) return;
		var tree = Engine.GetMainLoop() as SceneTree;
		if (tree?.CurrentScene == null) return;
		if (tree.CurrentScene.Name != "World") return;

		_playersContainer ??= tree.CurrentScene.GetNodeOrNull<Node3D>("Players");
		if (_playersContainer == null)
		{
			GD.PushError("[NetServer] World/Players Node3D missing in world.tscn — cannot spawn ServerAgent");
			return;
		}
		PackedScene scene = _characterScene ??= GD.Load<PackedScene>("res://character/server_player.tscn");
		if (scene == null)
		{
			GD.PushError("[NetServer] server_player.tscn could not be loaded");
			return;
		}

		var inst = scene.Instantiate<NetworkPlayer>();
		inst.CurrentGameMode = PresentationMode.Server;
		inst.NetId = state.NetId;
		inst.Name = $"sv_agent_{state.NetId}";
		inst.Position = state.SpawnPos;
		var rot = inst.Rotation;
		rot.Y = state.SpawnYaw;
		inst.Rotation = rot;

		_playersContainer.AddChild(inst);
		state.ServerAgent = inst;
		if (!state.IsBot && state.Peer != null)
		{
			inst.IsFrozen = true;
			Dbg.Print($"[NetServer] ServerAgent {state.NetId} frozen — waiting for first input packet");
		}
		Dbg.Print($"[NetServer] ServerAgent spawned: netId={state.NetId} at {state.SpawnPos}");
	}

	/// <summary>Snaps a spawn position to the ground via a downward raycast, with a small offset so the body
	/// doesn't clip the floor. Falls back to the original Y on no hit.</summary>
	private Vector3 GroundSnap(Vector3 pos)
	{
		if (Engine.GetMainLoop() is not SceneTree tree) return pos;
		var world = tree.Root?.World3D;
		if (world == null) return pos;
		var space = world.DirectSpaceState;
		if (space == null) return pos;

		var from = pos + Vector3.Up * 0.5f;
		var to = pos + Vector3.Down * 5f;
		var q = PhysicsRayQueryParameters3D.Create(from, to, collisionMask: 1u);
		var result = space.IntersectRay(q);
		if (result.Count == 0) return pos;
		var hit = (Vector3)result["position"];
		return new Vector3(pos.X, hit.Y + 0.1f, pos.Z);
	}

	/// <summary>Returns the lowest free NetId in 1..254 by scanning the live + bot id map; 0 means no slot left.</summary>
	private byte AllocateNetId()
	{
		for (byte i = 1; i < 255; i++)
			if (!_peersByNetId.ContainsKey(i)) return i;
		return 0;
	}

	/// <summary>Lowest unused team-slot (0..15) for a team; slots free only on permanent leave so the colour is
	/// session-stable. Falls back to 0 when all 16 are taken.</summary>
	private byte AssignFreeTeamSlot(Team team)
	{
		System.Span<bool> used = stackalloc bool[16];
		foreach (var p in AllPeers)
		{
			if (p.Team != team || p.PendingRemoval) continue;
			if (p.TeamSlot < 16) used[p.TeamSlot] = true;
		}
		for (byte i = 0; i < 16; i++) if (!used[i]) return i;
		return 0;
	}

	/// <summary>Prints <paramref name="message"/> to the server stdout and broadcasts it as a ServerLog so clients echo it too.</summary>
	private void LogToServerAndClients(string message)
	{
		GD.Print(message);
		if (_net == null) return;
		var writer = Packets.WriteServerLog(message);
		foreach (var p in _peers.Keys)
			p.Send(writer, ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Sends the given packet to every peer except an optional one.</summary>
	private void Broadcast(NetDataWriter writer, DeliveryMethod method, byte channel, NetPeer excludePeer)
	{
		foreach (var p in _peers.Keys)
		{
			if (p == excludePeer) continue;
			p.Send(writer, channel, method);
		}
	}

	/// <summary>Sends a packet only to peers with FoW line-of-sight to <paramref name="originatorPos"/>, plus
	/// always to the originator and teammates (deathmatch = everyone an enemy). Gates position-leaking events
	/// (shots/footsteps/jumps/lands) against wall-hacks. Falls back to a plain Broadcast when FoW is off/unbuilt.</summary>
	private void BroadcastWithFoW(NetDataWriter writer, byte originatorNetId, Vector3 originatorPos, DeliveryMethod method, byte channel)
	{
		if (!ConVars.Sv.FogOfWar || !_pvs.Built)
		{
			Broadcast(writer, method, channel, excludePeer: null);
			return;
		}
		var originator = GetPeerStateForNetId(originatorNetId);
		bool isDeathmatch = originator != null && originator.Team == Team.Deathmatch;
		byte originatorTeam = originator != null ? (byte)originator.Team : (byte)0;
		foreach (var kv in _peers)
		{
			var rxPeer = kv.Key;
			var rxState = kv.Value;
			if (rxState.HandshakePending) continue;
			if (rxState.NetId == originatorNetId) { rxPeer.Send(writer, channel, method); continue; }
			bool isTeammate = !isDeathmatch && (byte)rxState.Team == originatorTeam;
			if (isTeammate) { rxPeer.Send(writer, channel, method); continue; }
			if (rxState.ServerAgent == null) { rxPeer.Send(writer, channel, method); continue; }
			Vector3 rxPos = rxState.ServerAgent.AuthorityPosition;
			if (!_pvs.CanSee(rxPos, originatorPos)) { _fowStrippedEventsWindow++; continue; }
			rxPeer.Send(writer, channel, method);
		}
	}

	/// <summary>Broadcasts an authoritative shot event (origin, dir, resolved hit), FoW-gated on the shooter's origin.</summary>
	public void BroadcastShotFired(byte netId, byte weaponId, Vector3 origin, Vector3 dir,
		bool tracer, bool hit, Vector3 hitPos, Vector3 hitNormal, string material)
	{
		BroadcastWithFoW(Packets.WriteShotFired(netId, weaponId, origin, dir, tracer, hit, hitPos, hitNormal, material),
			netId, origin, DeliveryMethod.ReliableOrdered, ChannelReliable);
	}

	/// <summary>Sends a Hit event only to shooter + victim; other peers get nothing (prevents a wallhack leak).</summary>
	public void SendHitTo(byte shooterNetId, byte victimNetId, HitboxGroup group, byte damage, byte hpLeft, byte weaponId)
	{
		var writer = Packets.WriteHit(shooterNetId, victimNetId, group, damage, hpLeft, weaponId);
		foreach (var kv in _peers)
		{
			if (kv.Value.NetId == shooterNetId || kv.Value.NetId == victimNetId)
				kv.Key.Send(writer, ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}

	/// <summary>Broadcasts a footstep event for spatial audio, FoW-gated on the walker's position.</summary>
	public void BroadcastFootstep(byte netId, Vector3 pos, string material, byte loudness, bool leftFoot, bool sprinting)
	{
		BroadcastWithFoW(Packets.WriteFootstep(netId, pos, material, loudness, leftFoot, sprinting),
			netId, pos, DeliveryMethod.ReliableOrdered, ChannelReliable);
	}

	/// <summary>Broadcasts a jump event for puppet audio, FoW-gated on the jumper's position.</summary>
	public void BroadcastJump(byte netId)
	{
		var writer = Packets.WriteJump(netId);
		var s = GetPeerStateForNetId(netId);
		Vector3 pos = s?.ServerAgent != null ? s.ServerAgent.AuthorityPosition : Vector3.Zero;
		BroadcastWithFoW(writer, netId, pos, DeliveryMethod.ReliableOrdered, ChannelReliable);
	}

	/// <summary>Broadcasts an empty-reload event so peers drop the player's magazine, FoW-gated on its position.</summary>
	public void BroadcastDropMag(byte netId)
	{
		var writer = Packets.WriteDropMag(netId);
		var s = GetPeerStateForNetId(netId);
		Vector3 pos = s?.ServerAgent != null ? s.ServerAgent.AuthorityPosition : Vector3.Zero;
		BroadcastWithFoW(writer, netId, pos, DeliveryMethod.ReliableOrdered, ChannelReliable);
	}

	/// <summary>Broadcasts a landing event with impact speed for puppet audio, FoW-gated on the player's position.</summary>
	public void BroadcastLand(byte netId, float impactSpeed)
	{
		var writer = Packets.WriteLand(netId, impactSpeed);
		var s = GetPeerStateForNetId(netId);
		Vector3 pos = s?.ServerAgent != null ? s.ServerAgent.AuthorityPosition : Vector3.Zero;
		BroadcastWithFoW(writer, netId, pos, DeliveryMethod.ReliableOrdered, ChannelReliable);
	}

	/// <summary>Broadcasts a death event so every client can update the kill feed and victim state.</summary>
	public void BroadcastDeath(byte victimNetId, byte attackerNetId, byte weaponId, bool isHeadshot)
	{
		Broadcast(Packets.WriteDeath(victimNetId, attackerNetId, weaponId, isHeadshot), DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: null);
	}

	/// <summary>Broadcasts a respawn event so every client can teleport the player back in.</summary>
	public void BroadcastRespawn(byte netId, Vector3 pos, float yaw, byte hp)
	{
		Broadcast(Packets.WriteRespawn(netId, pos, yaw, hp), DeliveryMethod.ReliableOrdered, ChannelReliable, excludePeer: null);
	}

	/// <summary>Auto-respawn delay in seconds — countdown begins when HP reaches 0.</summary>
	private const int RespawnDelaySeconds = 5;

	/// <summary>Marks a player dead (IsDead on the ServerAgent), starts the auto-respawn countdown, awards the
	/// attacker a kill, and broadcasts the death event (weaponId + isHeadshot feed the killfeed).</summary>
	public void TriggerDeath(byte victimNetId, byte attackerNetId, byte weaponId = 0, bool isHeadshot = false)
	{
		var s = GetPeerStateForNetId(victimNetId);
		if (s == null || s.RespawnCountdownTicks > 0) return;
		s.RespawnCountdownTicks = RespawnDelaySeconds * _cli.TickRate;
		if (s.ServerAgent != null)
		{
			s.ServerAgent.IsDead = true;
			if (s.ServerAgent is NetworkPlayer lcDeath) lcDeath.CanFire = false;
		}
		if (s.Deaths < byte.MaxValue) s.Deaths++;
		if (attackerNetId != 0 && attackerNetId != victimNetId)
		{
			var atk = GetPeerStateForNetId(attackerNetId);
			if (atk != null && atk.Kills < byte.MaxValue) atk.Kills++;
		}
		BroadcastDeath(victimNetId, attackerNetId, weaponId, isHeadshot);
		Dbg.Print($"[NetServer] Death triggered netId={victimNetId} (respawn in {RespawnDelaySeconds}s) | victim K/D={s.Kills}/{s.Deaths}");
	}

	/// <summary>Per tick: decrements respawn countdowns and respawns at zero (removal-flagged bots despawn).
	/// AllPeers is copied into a local buffer first because DoRespawn re-calls AllPeers (which clears+refills
	/// the shared list) and would otherwise corrupt the enumeration.</summary>
	private readonly List<PeerState> _respawnIterBuf = new(32);
	private void TickRespawn()
	{
		_respawnIterBuf.Clear();
		foreach (var p in AllPeers) _respawnIterBuf.Add(p);
		List<PeerState> toRemove = null;
		foreach (var s in _respawnIterBuf)
		{
			if (s.RespawnCountdownTicks <= 0) continue;
			s.RespawnCountdownTicks--;
			if (s.RespawnCountdownTicks <= 0)
			{
				if (s.IsBot && s.PendingRemoval)
				{
					toRemove ??= new List<PeerState>();
					toRemove.Add(s);
				}
				else
				{
					DoRespawn(s);
				}
			}
		}
		if (toRemove != null)
			foreach (var b in toRemove) RemoveBot(b);
	}

	/// <summary>Picks a fresh spawn slot, resets the authoritative agent state, and broadcasts the respawn event.</summary>
	private void DoRespawn(PeerState s)
	{
		if (s.ServerAgent == null) return;
		Vector3 spawnPos = s.SpawnPos;
		float spawnYaw = s.SpawnYaw;
		if (_spawns.Initialized)
		{
			var occupied = new List<Vector3>();
			foreach (var other in AllPeers)
				if (other != s && other.ServerAgent != null) occupied.Add(other.ServerAgent.GlobalPosition);
			(spawnPos, spawnYaw) = _spawns.PickFreeSpawn(s.Team, occupied);
		}
		spawnPos = GroundSnap(spawnPos);

		s.Hp = 100;
		s.Armor = 50;
		s.LastDamageTickMs = 0;
		s.SpawnPos = spawnPos;
		s.SpawnYaw = spawnYaw;
		var agent = s.ServerAgent;
		agent.IsDead = false;
		agent.GlobalPosition = spawnPos;
		var rot = agent.Rotation;
		rot.Y = spawnYaw;
		agent.Rotation = rot;
		agent.Velocity = Vector3.Zero;
		s.HasValidatedPos = false;
		agent.Movement.Stamina = ConVars.Sv.MaxStamina;
		agent.Movement.ResetSpawnConsumables();
		if (agent is NetworkPlayer lcRespawn)
		{
			lcRespawn.CanFire = true;
			lcRespawn.Movement.InitializeAmmo(ConVars.Weapons.AR15);
			lcRespawn.ResetInterpToCurrentPos();
		}
		s.Rewind.Clear();

		BroadcastRespawn(s.NetId, spawnPos, spawnYaw, s.Hp);
		Dbg.Print($"[NetServer] Respawn netId={s.NetId} at {spawnPos}");
	}
}

/// <summary>Server-side state stored per connected peer (and per bot).</summary>
public class PeerState
{
	public byte NetId;
	public string PlayerName;
	public byte[] Token;
	/// <summary>Set on <see cref="PacketType.WorldInitComplete"/> (asset pre-loads done); broadcast via
	/// <see cref="SnapshotFlags.WorldReady"/> so peers show the TPS body. Persists until reconnect.</summary>
	public bool WorldReady;
	public Team Team;
	/// <summary>Persistent per-team slot (0..15), drives the per-player colour. Freed only on permanent leave.</summary>
	public byte TeamSlot;
	public Vector3 SpawnPos;
	public float SpawnYaw;
	public byte Hp;
	/// <summary>Kevlar (0..50). Body hits drain it at 50% of damage; headshots bypass it. Does not regen.</summary>
	public byte Armor;
	/// <summary>Time.GetTicksMsec() of the last damage hit; HP regen starts <see cref="NetServer.RegenDelayMs"/> after.</summary>
	public long LastDamageTickMs;
	public byte ActiveSlot;
	public byte WeaponId;

	public InputPacket LatestInput;
	public bool HasLatestInput;
	public uint LastInputTick;
	public ulong InputPacketsReceived;
	public int AntiCheatViolations;

	/// <summary>InputPackets accepted this server tick (for the per-peer flood cap); reset when the tick changes.</summary>
	public int PacketsThisServerTick;
	public uint LastPacketCountServerTick;
	/// <summary>Last validated ViewYaw + the tick it came from — basis for the angular-velocity check.</summary>
	public float LastViewYawSample;
	public uint LastViewYawSampleTick;
	public bool HasViewYawSample;
	/// <summary>Last validated server position + its tick — basis for the position-delta check.</summary>
	public Vector3 LastValidatedPos;
	public uint LastValidatedTick;
	public bool HasValidatedPos;
	/// <summary>Ring of recent violation timestamps for the sliding-window kick check.</summary>
	public readonly long[] RecentViolationMs = new long[8];
	public int RecentViolationHead;
	/// <summary>True once auto-kick has fired — prevents repeated kicks while disconnect propagates.</summary>
	public bool AntiCheatKicked;

	public NetworkPlayer ServerAgent;

	public RewindBuffer Rewind = new();
	public int LastPingMs;
	public NetPeer Peer;

	/// <summary>Ring of the last ~64 snapshots sent to this peer (post-PVS); delta source keyed by <see cref="LastAckedSnapshotTick"/>.</summary>
	public readonly SnapshotBaselineRing SentSnapshots = new();
	/// <summary>Most recent snapshot tick the client ACK'd via input packet. <see cref="Packets.NoBaselineTick"/> = none yet.</summary>
	public uint LastAckedSnapshotTick;

	public int RespawnCountdownTicks;
	public long DisconnectedAtTickMs;
	public byte Kills;
	public byte Deaths;
	public bool HandshakePending;

	public bool IsBot;
	public bool PendingRemoval;
}
