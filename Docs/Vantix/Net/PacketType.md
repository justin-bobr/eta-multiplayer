# PacketType

`Vantix.Net.PacketType`

Wire identifier per packet type. Keep values stable; bump `ProtocolVersion` on any incompatible wire change.

## Fields

| Name | Summary |
|------|---------|
| `ConVarSyncBroadcast` | S2C Reliable: broadcasts a sv_* ConVar change (also the initial post-SpawnAck sync). |
| `ConVarSyncRequest` | C2S Reliable: request to set a sv_* ConVar. Server validates, applies, broadcasts ConVarSync. |
| `DebugHitboxes` | Debug-only S2C: server hitbox positions (~10 Hz, when enabled). Client renders red spheres to verify lag-comp. |
| `DropMag` | S2C Reliable: a player started an empty reload; peers drop its magazine from the TPS weapon. FoW-gated. |
| `ServerLog` | S2C Reliable: diagnostic string the client prints in its own log. |
| `SpawnAuthorize` | S2C Reliable: grants the spawn pose + final Team after TeamSelect; triggers deferred LocalPlayer spawn. |
| `TeamSelect` | C2S Reliable: client picks a team (CT/T) after a Spectator SpawnAck in competitive mode. Server replies with SpawnAuthorize. Deathmatch skips this (SpawnAck already carries the pose). |
| `WorldInitComplete` | C2S Reliable: client finished asset pre-loads and is ready to be visible. Server sets per-player WorldReady and emits the snapshot bit so peers show the TPS body. |
