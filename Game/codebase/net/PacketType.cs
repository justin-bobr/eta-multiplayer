/// <summary>
/// Wire identifier per packet type. Keep stable — when the wire format changes incompatibly,
/// bump <see cref="Packets.ProtocolVersion"/>.
/// </summary>
public enum PacketType : byte
{
	ConnectRequest = 10,
	RespawnRequest = 11,
	/// <summary>C2S Reliable: client requests setting a sv_* ConVar via console. Server validates,
	/// applies, and broadcasts ConVarSync to all clients.</summary>
	ConVarSyncRequest = 12,
	/// <summary>C2S Reliable: client signals it has finished all asset pre-loads (audio, animations) and is
	/// ready to be visible to other players. Server flips per-player WorldReady=true and emits the matching
	/// snapshot bit so peers' PuppetPlayer can switch their TPS body visible.</summary>
	WorldInitComplete = 13,
	/// <summary>C2S Reliable: client picks a team (CT/T) after SpawnAck assigned Spectator in competitive
	/// mode. Server validates, assigns team + spawn pose, and replies with SpawnAuthorize. Deathmatch skips
	/// this (SpawnAck already carries the pose).</summary>
	TeamSelect = 14,
	/// <summary>S2C Reliable: server grants the spawn pose after a successful TeamSelect. Triggers the
	/// deferred LocalPlayer instantiation. Carries the final Team + pose.</summary>
	SpawnAuthorize = 42,

	SpawnAck = 20,
	PlayerJoined = 21,
	PlayerLeft = 22,
	PlayerDisconnected = 23,
	PlayerReconnected = 24,
	ShotFired = 25,
	Reload = 26,
	GrenadeSpawn = 27,
	Footstep = 28,
	Hit = 29,
	Death = 30,
	Respawn = 31,
	SlotSwitch = 32,
	Jump = 33,
	Land = 34,
	Inspect = 35,
	DryFire = 36,
	SlideStart = 37,
	SlideEnd = 38,
	RoundState = 39,
	ProjectileDespawn = 40,
	/// <summary>S2C Reliable: server broadcasts a sv_* ConVar change to all clients (also the initial sync
	/// after SpawnAck so reconnects get the current debug state).</summary>
	ConVarSyncBroadcast = 41,
	/// <summary>S2C Reliable: server broadcasts that a player started an empty reload, so every other
	/// client drops that player's magazine to the floor from its TPS weapon. FoW-gated.</summary>
	DropMag = 43,

	Input = 50,

	Snapshot = 70,
	ProjectileState = 71,
	/// <summary>Debug-only: server hitbox-position broadcast (~10 Hz, only when enabled on the server).
	/// Client renders them as red spheres to visually verify lag-comp.</summary>
	DebugHitboxes = 72,
	/// <summary>S2C Reliable: server-side diagnostic/status string the client prints in its own log.</summary>
	ServerLog = 73,
}
