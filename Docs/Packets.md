# Packets

Central packet read/write helpers. Each packet starts with a `PacketType` byte, then a type-specific body. Channels: Input + Snapshot are Unreliable (channel 0, drops discarded); all gameplay events and the handshake are ReliableOrdered (channel 1). Token is a variable-length byte array (GUID or auth token).

## Fields

| Name | Summary |
|------|---------|
| `MaxInputRedundancy` | Inputs bundled redundantly per packet. 3 covers 2 consecutive drops. |
| `MaxSubtickEventsWire` | Hard wire cap on subtick events per input body. Must match `NetworkPlayer.MaxSubtickEventsPerTick`; server rejects higher counts (cheat + bandwidth guard). |
| `NoBaselineTick` | "No baseline" sentinel (full snapshot / nothing received). The server starts at tick 1 so tick 0 never collides. |
| `ProtocolVersion` | Current protocol version. Bump on any incompatible wire change. v2: snapshot Pos/Vel cm-quantised int16; material a byte id. v3: delta-baseline snapshot compression (baselineTick + per-player field mask; input carries ackedSnapshotTick). v4: input redundancy (N bodies, dedupe by tickIndex). v5: subtick fire-timing (FireSubTick byte). v6: subtick movement (InitialBits + initial yaw/pitch + EventCount + N SubtickEvents). |

## Methods

| Name | Summary |
|------|---------|
| `Begin(PacketType)` | Allocates a new `NetDataWriter` pre-stamped with the given packet type byte. |
| `BuildMaterialIdMap()` | Builds the string-to-id lookup for the material table at type initialisation time. |
| `ComputeFieldMask(SnapshotPlayer, SnapshotPlayer)` | Returns the bitmask of field groups that changed between `cur` and `baseline`. Per-player hot path — struct-by-ref, no LINQ. |
| `DequantizePitch(UInt16)` | Restores a pitch angle in radians from its ushort quantisation. |
| `DequantizeYaw(UInt16)` | Restores a yaw angle in radians from its ushort quantisation. |
| `EncodeInput(uint, MovementInput, bool, bool, bool, bool, byte, SubtickEventEncoded[])` | Quantises + packs a sampled input into wire form, incl. subtick events from `Events`. EventCount is capped at `MaxSubtickEventsWire` (surplus dropped; held state stays correct). Optional `eventBuffer` is a caller-owned scratch array reused to avoid a per-tick alloc; it must outlive the struct's stay in the redundancy ring. |
| `GetVec3(LiteNetLib.NetPacketReader)` | Reads three IEEE-754 floats into a Vector3. |
| `GetVec3Quantized(LiteNetLib.NetPacketReader)` | Reads a cm-quantised Vec3 emitted by `PutVec3Quantized`. |
| `IdToMaterial(byte)` | Returns the material name for a wire-format byte id, falling back to "default" when out of range. |
| `MaterialToId(string)` | Returns the wire-format byte id for a material name, falling back to 0 ("default"). |
| `PutVec3(LiteNetLib.Utils.NetDataWriter, Vector3)` | Writes a Vector3 as three IEEE-754 floats (12 bytes). |
| `PutVec3Quantized(LiteNetLib.Utils.NetDataWriter, Vector3)` | 16-bit cm-quantised Vec3 (6 B, range ±327.67 m, 1 cm precision). For snapshot Pos/Vel only — tracers/directions need full float precision. |
| `QuantizePitch(float)` | Quantises a pitch angle in radians to a ushort (range [-π/2..π/2] mapped to 0..65535). |
| `QuantizeYaw(float)` | Quantises a yaw angle in radians to a ushort (range [-π..π] mapped to 0..65535). |
| `ReadConnectRequest(LiteNetLib.NetPacketReader, UInt16, string, Byte[])` | Reads a ConnectRequest body into protocol version, player name, and identity token. |
| `ReadDeath(LiteNetLib.NetPacketReader, byte, byte, byte, bool)` | Reads a Death packet into victim + attacker + weaponId + headshot-flag. |
| `ReadDropMag(LiteNetLib.NetPacketReader, byte)` | Reads a DropMag packet's NetId. |
| `ReadFootstep(LiteNetLib.NetPacketReader, byte, Vector3, string, byte, bool, bool)` | Reads a Footstep packet into out parameters and resolves the material id to a name. |
| `ReadGrenadeSpawn(LiteNetLib.NetPacketReader, byte, uint, byte, Vector3, Vector3)` | Reads a GrenadeSpawn packet into owner/projectile ids and motion data. |
| `ReadHit(LiteNetLib.NetPacketReader, byte, byte, HitboxGroup, byte, byte, byte)` | Reads a Hit packet into out parameters describing the shooter, victim and damage details. |
| `ReadInputBody(LiteNetLib.NetPacketReader, InputPacket)` | Reads one input body (single client tick). EventCount is clamped to `MaxSubtickEventsWire` (cheat guard); a monotonic-violation event list is dropped (see below). |
| `ReadInputHeader(LiteNetLib.NetPacketReader, byte, uint)` | Reads the input-packet header (count + ackedSnapshotTick); caller then loops `ReadInputBody`. |
| `ReadJump(LiteNetLib.NetPacketReader, byte)` | Reads a Jump packet's NetId. |
| `ReadLand(LiteNetLib.NetPacketReader, byte, float)` | Reads a Land packet into NetId and impact-speed out parameters. |
| `ReadPlayerJoined(LiteNetLib.NetPacketReader)` | Reads a PlayerJoined body into an `InitialPlayerState`. |
| `ReadPlayerLeft(LiteNetLib.NetPacketReader, byte, byte)` | Reads a PlayerLeft packet into NetId and reason out parameters. |
| `ReadProjectileDespawn(LiteNetLib.NetPacketReader, byte, uint, Vector3)` | Reads a ProjectileDespawn packet into owner, projectile id and final position. |
| `ReadProjectileState(LiteNetLib.NetPacketReader, byte, uint, Vector3, Vector3)` | Reads a ProjectileState packet's owner, projectile id and dequantised pose data. |
| `ReadRespawn(LiteNetLib.NetPacketReader, byte, Vector3, float, byte)` | Reads a Respawn packet into pose and HP out parameters. |
| `ReadRoundState(LiteNetLib.NetPacketReader, uint, UInt16, UInt16, UInt16)` | Reads a RoundState packet payload into out params. |
| `ReadServerLog(LiteNetLib.NetPacketReader, string)` | Reads a ServerLog string (capped 512 chars to bound a rogue server's bandwidth). |
| `ReadShotFired(LiteNetLib.NetPacketReader, byte, byte, Vector3, Vector3, bool, bool, Vector3, Vector3, string)` | Reads a ShotFired packet, including the optional hit position/normal/material when present. |
| `ReadSnapshot(LiteNetLib.NetPacketReader, uint, uint, uint, Func<UInt32,Nullable<ValueTuple<SnapshotPlayer[],Int32>>>, SnapshotPlayer[], int)` | Reads a delta-snapshot packet via a caller-supplied baseline lookup (tick → players, or null). Full snapshot when `baselineTick ==`. Returns false (drop + don't ack) if a needed baseline is missing. |
| `ReadSpawnAck(LiteNetLib.NetPacketReader, byte, Team, string, uint, UInt16, Vector3, float, InitialPlayerState[], Byte[])` | Reads a SpawnAck body into out parameters, including the array of already-spawned players. |
| `TryFindBaselinePlayer(SnapshotPlayer[], int, byte, SnapshotPlayer)` | Linear NetId lookup over the baseline players (n ≤ 16, so no dictionary). |
| `WriteConnectRequest(string, Byte[])` | Writes a ConnectRequest packet with the player name and identity token. |
| `WriteDeath(byte, byte, byte, bool)` | Writes a Death packet (victim, attacker, weaponId, headshot flag). weaponId = 0 for world damage. |
| `WriteDebugHitboxes(uint, DebugHitboxAgent)` | One agent per packet (~640 B), under LiteNetLib's 1023 B unreliable MTU. |
| `WriteDropMag(byte)` | Writes a DropMag packet carrying just the reloading player's NetId. |
| `WriteFootstep(byte, Vector3, string, byte, bool, bool)` | Writes a Footstep packet with position, material id, loudness and flags. |
| `WriteGrenadeSpawn(byte, uint, byte, Vector3, Vector3)` | Writes a GrenadeSpawn packet with owner/projectile ids, type, origin and velocity. |
| `WriteHit(byte, byte, HitboxGroup, byte, byte, byte)` | Writes a Hit packet (shooter, victim, hitbox group, damage, hp left, weapon). |
| `WriteInputPacketInto(LiteNetLib.Utils.NetDataWriter, uint, EncodedInput[], int, int)` | Writes a full input packet [type\|count\|ackedSnapshotTick\|N×body]. ackedSnapshotTick is once-per-packet; inputs must be oldest→newest for sequential server dedupe. |
| `WriteJump(byte)` | Writes a Jump packet carrying just the jumper's NetId. |
| `WriteLand(byte, float)` | Writes a Land packet with the landing player's NetId and impact speed. |
| `WritePlayerJoined(byte, string, Vector3, float, byte, byte, byte, byte, byte)` | Writes a PlayerJoined packet announcing a new peer's NetId, name and initial state. |
| `WritePlayerLeft(byte, byte)` | Writes a PlayerLeft packet with the NetId of the leaver and a reason byte. |
| `WriteProjectileDespawn(byte, uint, Vector3)` | Writes a ProjectileDespawn packet carrying the final resting position. |
| `WriteProjectileState(byte, uint, Vector3, Vector3)` | Writes a ProjectileState packet with cm-quantised position and velocity. |
| `WriteRespawn(byte, Vector3, float, byte)` | Writes a Respawn packet with the new pose and HP for the respawning player. |
| `WriteRoundState(uint, UInt16, UInt16, UInt16)` | Writes a RoundState packet (start tick, duration, round number, total). Clients derive `RoundTimeRemainingSec` from it. |
| `WriteServerLog(string)` | Writes a ServerLog packet — a UTF-8 string the client prints to its own log. |
| `WriteShotFired(byte, byte, Vector3, Vector3, bool, bool, Vector3, Vector3, string)` | Writes a ShotFired packet — origin, direction and optional authoritative hit data. |
| `WriteSnapshotInto(LiteNetLib.Utils.NetDataWriter, uint, uint, uint, IReadOnlyList<SnapshotPlayer>, SnapshotPlayer[], int)` | Writes a delta-baseline-compressed snapshot. `baselineTick` = `NoBaselineTick` forces a full snapshot (mask = All); otherwise each player is delta'd against the matching baseline entry. Caller must pass a baseline from the same PVS view. |
| `WriteSpawnAck(byte, Team, string, uint, UInt16, Vector3, float, IReadOnlyList<InitialPlayerState>, Byte[])` | Writes a SpawnAck (joiner's NetId, world info, spawn pose, initial roster). In competitive mode `yourTeam` is `Spectator` and the pose is ignored until `TeamSelect`. |
| `WriteSpawnAuthorize(Team, Vector3, float)` | S2C spawn grant after TeamSelect, carrying the resolved Team (may differ if rebalanced) and pose. |
| `WriteTeamSelect(Team)` | C2S team choice (CT/T); Spectator is invalid. Server replies with SpawnAuthorize. |
| `WriteWorldInitComplete()` | Empty payload — the type byte alone flips WorldReady on the peer. |
