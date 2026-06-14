# NetServer

Server side of the netcode stack. Listens, accepts peers, runs the ConnectRequest handshake, allocates NetIds, broadcasts SpawnAck/PlayerJoined, runs the sim, and emits snapshots.

## Properties

| Name | Summary |
|------|---------|
| `AllPeers` | Cached peers + bots, stable for the tick. Mid-Poll mutations re-call `RefreshAllPeersCache`. |
| `Spawns` | Spawn points loaded from the map markers. |

## Fields

| Name | Summary |
|------|---------|
| `BotProbeMask` | Obstacle probe mask for bot steering: world geometry (bit 0) + server agents (bit 4). |
| `FoWBuildRaysPerPoll` | Raycast budget per Poll while the PVS builds (~10ms hitch/tick); stays under the 30s loopback DisconnectTimeout so the client survives the build. |
| `ProfilerWriteEveryTicks` | = 10s at 128Hz. |
| `RegenCapHp` | Max regenerable HP (kevlar does not regen). |
| `RegenDelayMs` | Delay after the last damage hit before HP regen starts. |
| `RegenTickMs` | Regen interval — grants +1 HP every this-many ms (~12 hp/s). |
| `RespawnDelaySeconds` | Auto-respawn delay in seconds — countdown begins when HP reaches 0. |
| `RoundStateBroadcastEveryTicks` | 1Hz heartbeat at 128tps — keeps late-joining clients in sync within ~1s and corrects client-side drift. |
| `_allPeersCache` | All active agent states (peers + bots) — anywhere "all hittable players" is meant. Concrete `List` so foreach uses the struct enumerator. Refilled per Poll via `RefreshAllPeersCache`. |
| `_botTargetCandidates` | Reused list of Zone/BombSpot centres = the bot controller's long-range target pool. Rebuilt only when its size changes (map reload). |
| `_disconnectedPool` | Disconnect pool: token → frozen state. If a reconnect arrives with the same token, resume. |
| `_perReceiverSnapBuf` | Reused per-receiver snapshot buffer for PVS filtering — single allocation, cleared/refilled per peer per tick. |
| `_profilerPathPrinted` | Periodic profiler dump to user://server.profile ([SV]-prefixed samples); logs the resolved path on first write. |
| `_pvs` | Voxel-PVS for line-of-sight Fog of War, built lazily once the world is loaded and `sv_fog_of_war` is on (checked independently of `TryScanSpawns` so a runtime toggle still triggers a build). Queried per-receiver per-snapshot/per-event to gate enemy visibility. |
| `_respawnIterBuf` | Per tick: decrements respawn countdowns and respawns at zero (removal-flagged bots despawn). AllPeers is copied into a local buffer first because DoRespawn re-calls AllPeers (which clears+refills the shared list) and would otherwise corrupt the enumeration. |
| `_writeBuf` | Reused `NetDataWriter` for BroadcastSnapshots — avoids ~1k allocs/s on a full server. |

## Methods

| Name | Summary |
|------|---------|
| `#ctor(NetCli)` | Creates a new server bound to the given CLI configuration. |
| `AllocateNetId()` | Returns the lowest free NetId in 1..254 by scanning the live + bot id map; 0 means no slot left. |
| `AssignFreeTeamSlot(Team)` | Lowest unused team-slot (0..15) for a team; slots free only on permanent leave so the colour is session-stable. Falls back to 0 when all 16 are taken. |
| `Broadcast(LiteNetLib.Utils.NetDataWriter, LiteNetLib.DeliveryMethod, byte, LiteNetLib.NetPeer)` | Sends the given packet to every peer except an optional one. |
| `BroadcastConVarSync(string, string)` | Broadcasts a ConVar change to all clients. |
| `BroadcastDeath(byte, byte, byte, bool)` | Broadcasts a death event so every client can update the kill feed and victim state. |
| `BroadcastDebugHitboxes()` | Broadcasts rewound hitbox poses to all peers for visual lag-comp verification. `vizTick` uses `_serverTick + 1` because NetworkPlayer._currentTick runs one ahead of _serverTick within the same physics frame; without the +1 the viz trails the actual hitscan query tick by one. |
| `BroadcastDropMag(byte)` | Broadcasts an empty-reload event so peers drop the player's magazine, FoW-gated on its position. |
| `BroadcastFootstep(byte, Vector3, string, byte, bool, bool)` | Broadcasts a footstep event for spatial audio, FoW-gated on the walker's position. |
| `BroadcastJump(byte)` | Broadcasts a jump event for puppet audio, FoW-gated on the jumper's position. |
| `BroadcastLand(byte, float)` | Broadcasts a landing event with impact speed for puppet audio, FoW-gated on the player's position. |
| `BroadcastRespawn(byte, Vector3, float, byte)` | Broadcasts a respawn event so every client can teleport the player back in. |
| `BroadcastShotFired(byte, byte, Vector3, Vector3, bool, bool, Vector3, Vector3, string)` | Broadcasts an authoritative shot event (origin, dir, resolved hit), FoW-gated on the shooter's origin. |
| `BroadcastSnapshots()` | Sends a SnapshotPacket to every peer, every second tick (64 Hz against the 128 Hz sim). Per receiver: `ackedInputTick` = its last consumed input tick (for reconciliation); FoW/PVS or distance gate strips non-teammate players (teammates + self always included); delta-baseline compression emits only changed fields against the acked baseline in `SentSnapshots`, falling back to a full snapshot when the baseline ages out (self-healing). `ActiveSlot` is live-read from the ServerAgent each tick so puppets see the thrower's grenade slot. |
| `BroadcastWithFoW(LiteNetLib.Utils.NetDataWriter, byte, Vector3, LiteNetLib.DeliveryMethod, byte)` | Sends a packet only to peers with FoW line-of-sight to `originatorPos`, plus always to the originator and teammates (deathmatch = everyone an enemy). Gates position-leaking events (shots/footsteps/jumps/lands) against wall-hacks. Falls back to a plain Broadcast when FoW is off/unbuilt. |
| `DoRespawn(PeerState)` | Picks a fresh spawn slot, resets the authoritative agent state, and broadcasts the respawn event. |
| `EnsureBotFill()` | Adjusts the bot count up or down so that it matches the configured target, respecting spawn slot and player caps. |
| `EnsureServerAgent(PeerState)` | Spawns a ServerAgent into the Players container; idempotent and retried per poll until the world is ready. |
| `EvictExpiredDisconnects()` | Frees disconnected-pool entries whose reconnect grace has expired and notifies remaining peers. |
| `FeedInputsToAgents()` | Copies each peer's latest input into its ServerAgent and clears edge-triggered flags to prevent replay on packet loss. |
| `FinalizePendingHandshakes()` | Retries pending handshakes each poll iteration once the world is loaded and creates missing ServerAgents. |
| `FindVoxelPvsInstance(Node)` | Finds the first `VoxelPvsInstance` under `root`, or null (runtime-build fallback). |
| `GetPeerStateForNetId(byte)` | Lookup for ServerAgent → PeerState (e.g. from NetworkPlayer.HandleHitscan for lag-comp). |
| `GroundSnap(Vector3)` | Snaps a spawn position to the ground via a downward raycast, with a small offset so the body doesn't clip the floor. Falls back to the original Y on no hit. |
| `HandleConVarSyncRequest(LiteNetLib.NetPeer, LiteNetLib.NetPacketReader)` | Client requests an sv_* ConVar set; server validates the prefix + whitelist, applies, broadcasts. |
| `HandleConnectRequest(LiteNetLib.NetPeer, LiteNetLib.NetPacketReader)` | Validates the ConnectRequest, resumes a matching disconnect-pool entry or allocates a fresh peer. On resume the per-peer snapshot baseline is cleared so the first post-reconnect snapshot is full (the fresh NetClient can't decode deltas against the pre-disconnect baseline). |
| `HandleGrenadeSpawn(LiteNetLib.NetPeer, LiteNetLib.NetPacketReader)` | Relays a client-initiated grenade spawn to other peers with the NetId rewritten to prevent spoofing. |
| `HandleInput(LiteNetLib.NetPeer, LiteNetLib.NetPacketReader)` | Validates and stores the peer's latest input bundle and unfreezes its ServerAgent on first input. The packet carries the last N inputs (oldest→newest); dedupes by tickIndex and OR-merges edge intents (Jump/Crouch/Reload/Inspect) across the bundle so a press-edge in a middle input isn't lost. Continuous fields take the newest values. FireSubTick is propagated from the most recent input with it > 0. Snapshot-ack is per-packet; max()-guarded against out-of-order unreliable inputs. |
| `HandleProjectileDespawn(LiteNetLib.NetPeer, LiteNetLib.NetPacketReader)` | Relays the owner's projectile despawn signal to other peers so they can finalize the visual. |
| `HandleProjectileState(LiteNetLib.NetPeer, LiteNetLib.NetPacketReader)` | Relays the owner's periodic projectile position/velocity update to other peers (unreliable, NetId rewritten). |
| `HandleTeamSelect(LiteNetLib.NetPeer, LiteNetLib.NetPacketReader)` | Competitive client picked CT/T. Rejects Spectator/Deathmatch/already-in-team (stale reconnect or lag). On success: assigns team, allocates a spawn pose, ensures a ServerAgent, and replies with `SpawnAuthorize`. |
| `HandleWorldInitComplete(LiteNetLib.NetPeer)` | Client finished its world preload — sets WorldReady on its PeerState so subsequent snapshots emit `WorldReady` and peers show its TPS body. |
| `LogToServerAndClients(string)` | Prints `message` to the server stdout and broadcasts it as a ServerLog so clients echo it too. |
| `OnConnectionRequest(LiteNetLib.ConnectionRequest)` | Accepts incoming connections that supply the correct protocol key, rejecting when full. |
| `OnNetworkError(Net.IPEndPoint, Net.Sockets.SocketError)` | Logs LiteNetLib socket-level errors. |
| `OnNetworkReceive(LiteNetLib.NetPeer, LiteNetLib.NetPacketReader, byte, LiteNetLib.DeliveryMethod)` | Dispatches an incoming packet to its typed handler based on the leading PacketType byte. |
| `OnPeerConnected(LiteNetLib.NetPeer)` | Notes the transport connection; the real handshake follows on ConnectRequest. |
| `OnPeerDisconnected(LiteNetLib.NetPeer, LiteNetLib.DisconnectInfo)` | Moves the peer's state into the reconnect grace pool, or frees it immediately when no token is present. |
| `PickBotName()` | Returns the next bot display name via a deterministic round-robin over `BotNameOptions`. |
| `Poll()` | Runs one server tick: drains events, drives the sim/snapshot/respawn pipelines, bumps the tick. The AllPeers cache is refreshed twice (top + after handshake finalise) so fresh peers make this tick's snapshot. |
| `PushPositionsToRewind()` | Records each agent's authoritative tick position + bone-pose snapshot so lag-comp can rewind both the body and the animated hitbox transforms. Also runs the anti-cheat position-delta check. |
| `RefreshAllPeersCache()` | Rebuilds the AllPeers cache. Called per Poll and after any peer-state mutation. |
| `RegisterAntiCheatViolation(PeerState, string)` | Records an anti-cheat violation: pushes a timestamp into the sliding-window ring, bumps the lifetime counter, and kicks when the windowed count exceeds the threshold and auto-kick is on. |
| `RemoveBot(PeerState)` | Despawns a bot: frees its ServerAgent, removes it from internal lists and sends PlayerLeft to all real peers. |
| `SampleBandwidth()` | Samples LiteNetLib byte counters every 500 ms and feeds NetStats with smoothed rates. |
| `SendHitTo(byte, byte, HitboxGroup, byte, byte, byte)` | Sends a Hit event only to shooter + victim; other peers get nothing (prevents a wallhack leak). |
| `SendInitialConVarSync(LiteNetLib.NetPeer)` | Sends all sv_* ConVar values to one peer after SpawnAck so a reconnect keeps its debug-toggle state. |
| `ShortestYawDelta(float, float)` | Signed shortest-arc yaw delta in radians, wrapping across 0/2π. Result in [-π, π]. |
| `SpawnBot()` | Spawns a single bot as a server-driven test dummy with a ServerAgent and HitboxRig. |
| `Start()` | Binds the UDP listener and wires LiteNetLib handlers. Starts `_serverTick` at 1 so tick 0 stays the `NoBaselineTick` sentinel. |
| `Stop()` | Stops the UDP listener and clears the server-running NetStats flag. |
| `TickHpRegen()` | Regens HP towards `RegenCapHp` for peers past the `RegenDelayMs` grace window. Skips peers that never took damage (`LastDamageTickMs == 0`). |
| `TickRoundState()` | Ticks the round timer, advancing to the next round on expiry (no score reset yet — no win-condition system). Rebroadcasts round state at 1Hz to re-sync late joiners and drifting clients. |
| `TriggerDeath(byte, byte, byte, bool)` | Marks a player dead (IsDead on the ServerAgent), starts the auto-respawn countdown, awards the attacker a kill, and broadcasts the death event (weaponId + isHeadshot feed the killfeed). |
| `TryFinalizeHandshake(PeerState)` | Finalises one pending handshake once the world is loaded: picks a spawn slot, sends SpawnAck, the initial sv_* ConVar sync, and broadcasts PlayerJoined. |
| `TryScanSpawns()` | Scans the SpawnManager once world.tscn is active and applies headless settings on dedicated servers. |
| `UpdateBotInputs()` | Per-tick AI driver for all bots, run just before `FeedInputsToAgents` so the next physics step picks up the fresh InputPacket. Skips dead/frozen bots and non-NetworkPlayer bodies. |
| `UpdatePeerPings()` | Refreshes cached round-trip times and back-references for each connected peer. |
