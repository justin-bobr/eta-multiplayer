# NetClient

Client side of the netcode stack. Handles the handshake, sends ConnectRequest, receives SpawnAck, and dispatches player joined/left events.

## Properties

| Name | Summary |
|------|---------|
| `MapName` | Map display name without "res://"/".tscn" (e.g. "de_dust2"), derived from `MapPath`. |
| `MapPath` | Full resource path of the loaded map (e.g. "res://de_dust2.tscn"). Sent in SpawnAck. |
| `OwnNetId` | Own NetId after successful SpawnAck. 0 = not yet assigned. |
| `RoundStartTick` | Server-broadcast round state, updated via `RoundState` (1Hz + on transitions). |
| `RoundTimeRemainingSec` | Seconds left in the current round = duration - (now_tick - startTick)/tickRate. 0 if no round state yet or expired. |
| `SpawnAuthorized` | True once the server authorized a real spawn (deathmatch SpawnAck, or competitive SpawnAuthorize after TeamSelect). NetMain instantiates the LocalPlayer only when set; otherwise it shows preview-cams + team-select UI. |

## Fields

| Name | Summary |
|------|---------|
| `LastAckedInputTick` | Tick index of our last input the server has acknowledged (used for reconciliation). |
| `LastReceivedSnapshotTick` | Most recent reconstructed snapshot tick. Sent in every input packet as `ackedSnapshotTick` (the server's baseline key). `NoBaselineTick` = nothing received yet. |
| `LastRemoteSnapshots` | Last snapshots for all remote players. PuppetPlayer reads from here. |
| `LastSelfServerYaw` | Last server-reported authority yaw for the OWN player. |
| `LastSelfSnap` | Self-snapshot stats (Kills, Deaths, Hp, Ping) — the scoreboard reads from this. |
| `LastSnapshotServerTick` | Server tick of the most recently received snapshot (used for server-time sync). |
| `OnDisconnected` | Fires when the transport drops (timeout, kick, server shutdown). Carries the human-readable disconnect reason so the UI can display it on the reconnect screen. |
| `OnSnapshot` | Fires after each received SnapshotPacket. |
| `RemotePlayers` | Active snapshot of the world state: all other players. |
| `ServerHitboxTransforms` | Latest server-reported hitbox transforms per agent NetId (~10Hz when debug is on). `HudServerHitboxesDebug` renders them as red shapes. |
| `_eventPool` | Round-robin pool of subtick-event scratch buffers (one per ring slot) so EncodeInput avoids a per-tick alloc. Pool size == ring capacity guarantees no two live ring entries alias the same array. |
| `_inputRing` | Input redundancy ring: last `MaxInputRedundancy` sent inputs, oldest→newest. Every SendInput resends all buffered inputs so one lost packet can't drop an edge-triggered intent (Jump/Reload). |
| `_receivedSnapshots` | Ring of the last ~64 reconstructed received snapshots; baseline source for delta packets (baselineTick != 0). |
| `_snapshotPlayerBuffer` | Reused ReadSnapshot buffer (avoids a per-snapshot alloc); grows only when player count increases. |

## Methods

| Name | Summary |
|------|---------|
| `#ctor(NetCli)` | Creates a new client bound to the given CLI configuration. |
| `AllocateProjectileId()` | Returns the next unique projectile id for a local player's throw. |
| `ExtractMapName(string)` | Strips "res://" and the extension to produce a map display name ("res://de_dust2.tscn" → "de_dust2"). |
| `HandleConVarSyncBroadcast(LiteNetLib.NetPacketReader)` | Applies a broadcast/initial-sync ConVar update so visualization gates reflect server state. |
| `HandleDropMag(LiteNetLib.NetPacketReader)` | Routes an empty-reload event to the corresponding puppet so it drops its magazine. |
| `HandleFootstep(LiteNetLib.NetPacketReader)` | Routes a footstep event to the corresponding puppet for spatial audio playback. |
| `HandleGrenadeSpawn(LiteNetLib.NetPacketReader)` | Spawns a puppet grenade for remote throwers; the local thrower's echo is dropped. |
| `HandleHit(LiteNetLib.NetPacketReader)` | Decodes a hit event and forwards it through the `OnHit` action. |
| `HandleJump(LiteNetLib.NetPacketReader)` | Routes a jump event to the corresponding puppet. |
| `HandleLand(LiteNetLib.NetPacketReader)` | Routes a landing event to the corresponding puppet. |
| `HandlePlayerJoined(LiteNetLib.NetPacketReader)` | Records the joining remote player and forwards a join event to subscribers. |
| `HandlePlayerLeft(LiteNetLib.NetPacketReader)` | Removes the leaving remote player and forwards a leave event to subscribers. |
| `HandleProjectileDespawn(LiteNetLib.NetPacketReader)` | Applies a remote projectile despawn signal and removes the puppet from the registry. |
| `HandleProjectileState(LiteNetLib.NetPacketReader)` | Applies a remote projectile state update to the matching puppet projectile. |
| `HandleRespawn(LiteNetLib.NetPacketReader)` | Applies an authoritative respawn — teleports the local player and resets transient state. |
| `HandleRoundState(LiteNetLib.NetPacketReader)` | Reads a RoundState heartbeat (1Hz). Remaining time is exposed via `RoundTimeRemainingSec`. |
| `HandleServerLog(LiteNetLib.NetPacketReader)` | Prints a server-side log message into the client's stdout and the in-game ConsoleHud, prefixed [SV]. |
| `HandleShotFired(LiteNetLib.NetPacketReader)` | Routes a shot event to the matching puppet for tracer/impact playback. Own shots have no puppet; when sv_debug_bullets is on a red marker is spawned at the server hit position to compare against the client prediction. |
| `HandleSnapshot(LiteNetLib.NetPacketReader)` | Decodes a snapshot, updates remote/self caches, drives client-side reconciliation, and fires OnSnapshot. |
| `HandleSpawnAck(LiteNetLib.NetPacketReader)` | Applies the initial spawn assignment from the server, persists the assigned identity token, and fires OnSpawned. |
| `HandleSpawnAuthorize(LiteNetLib.NetPacketReader)` | Receives the post-TeamSelect spawn authorization; sets the pending pose and SpawnAuthorized=true. |
| `LookupBaseline(uint)` | Baseline lookup for `ReadSnapshot`; returns the reconstructed states for a tick, or null if aged out. |
| `LookupPuppet(byte)` | Returns the puppet driver for a given NetId, or null if the id is the local player or unknown. |
| `OnNetworkError(Net.IPEndPoint, Net.Sockets.SocketError)` | Logs a warning for low-level socket errors reported by LiteNetLib. |
| `OnNetworkReceive(LiteNetLib.NetPeer, LiteNetLib.NetPacketReader, byte, LiteNetLib.DeliveryMethod)` | Dispatches an incoming packet to its typed handler based on the leading PacketType byte. |
| `OnPeerConnected(LiteNetLib.NetPeer)` | Fires once the transport-level connection is established; sends the ConnectRequest. |
| `OnPeerDisconnected(LiteNetLib.NetPeer, LiteNetLib.DisconnectInfo)` | Resets all session-scoped state when the transport disconnects and notifies subscribers (e.g. NetMain swaps to the reconnect screen). |
| `Poll()` | Pumps the LiteNetLib event loop and updates client-side NetStats. |
| `PuppetKey(byte, uint)` | Builds a combined dictionary key from owner NetId and projectile id. |
| `PushInputToRing(EncodedInput)` | Appends an encoded input to the redundancy ring, shifting out the oldest when at capacity. |
| `RegisterOwnedProjectile(uint, SmokeGrenade)` | Registers a locally owned projectile instance for state replication. |
| `RegisterPuppetProjectile(byte, uint, SmokeGrenade)` | Registers a puppet projectile (echo from a remote thrower) for state updates. |
| `SampleBandwidth()` | Samples LiteNetLib byte/packet counters every 500 ms and feeds NetStats with smoothed rates. |
| `SendConVarSyncRequest(string, string)` | Requests an sv_* ConVar change; the server validates and broadcasts the new value to all clients. |
| `SendGrenadeSpawn(uint, byte, Vector3, Vector3)` | Sends a grenade spawn event to the server so other peers can spawn a puppet copy. |
| `SendInput(uint, MovementInput, bool, bool, bool, bool, byte)` | Sends the last `MaxInputRedundancy` input frames (unreliable, channel 0); server dedupes by tickIndex. `fireSubTick` is the quantised sub-tick offset (0..255) of the fire-press edge, passed verbatim onto the wire. |
| `SendProjectileDespawn(uint, Vector3)` | Sends a reliable signal that an owner-controlled projectile has terminated. |
| `SendProjectileState(uint, Vector3, Vector3)` | Sends a periodic position/velocity update for an owner-controlled projectile (unreliable). |
| `SendTeamSelect(Team)` | C2S: requests a spawn after the user picks CT/T (competitive). Idempotent — the server silently drops invalid requests (already in team, bad value, world not ready). Reliable channel. |
| `Start()` | Starts the UDP socket and initiates a connection to the configured host/port. |
| `Stop()` | Stops the UDP socket and resets the connected flag. |
| `UnregisterOwnedProjectile(uint)` | Removes a locally owned projectile from the registry. |
| `UnregisterPuppetProjectile(byte, uint)` | Removes a puppet projectile from the registry. |

## Events

| Name | Summary |
|------|---------|
| `OnDeath` | Killfeed subscribers (HudKillfeed): victim, attacker, weaponId, isHeadshot. |
| `OnHit` | Server emits hits only to shooter + victim. HudHitmarker subscribes via `OnHit`. |
