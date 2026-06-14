namespace Vantix.Net;

/// <summary>Wire identifier per packet type. Keep values stable; bump <see cref="Packets.ProtocolVersion"/>
/// on any incompatible wire change.</summary>
public enum PacketType : byte
{
	ConnectRequest = 10,
	RespawnRequest = 11,
	/// <summary>C2S Reliable: request to set a sv_* ConVar. Server validates, applies, broadcasts ConVarSync.</summary>
	ConVarSyncRequest = 12,
	/// <summary>C2S Reliable: client finished asset pre-loads and is ready to be visible. Server sets
	/// per-player WorldReady and emits the snapshot bit so peers show the TPS body.</summary>
	WorldInitComplete = 13,
	/// <summary>C2S Reliable: client picks a team (CT/T) after a Spectator SpawnAck in competitive mode.
	/// Server replies with SpawnAuthorize. Deathmatch skips this (SpawnAck already carries the pose).</summary>
	TeamSelect = 14,
	/// <summary>S2C Reliable: grants the spawn pose + final Team after TeamSelect; triggers deferred LocalPlayer spawn.</summary>
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
	/// <summary>S2C Reliable: broadcasts a sv_* ConVar change (also the initial post-SpawnAck sync).</summary>
	ConVarSyncBroadcast = 41,
	/// <summary>S2C Reliable: a player started an empty reload; peers drop its magazine from the TPS weapon. FoW-gated.</summary>
	DropMag = 43,

	Input = 50,

	Snapshot = 70,
	ProjectileState = 71,
	/// <summary>Debug-only S2C: server hitbox positions (~10 Hz, when enabled). Client renders red spheres to verify lag-comp.</summary>
	DebugHitboxes = 72,
	/// <summary>S2C Reliable: diagnostic string the client prints in its own log.</summary>
	ServerLog = 73,
}
