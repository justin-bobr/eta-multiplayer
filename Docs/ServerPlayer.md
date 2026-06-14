# ServerPlayer

Server-authoritative character (real peer or bot). Runs the sim from replicated net input, resolves hitscan with lag compensation, and poses the TPS skeleton for hitboxes. No FX/audio; a non-headless server adds an eye-level spectate camera. Spawned from `server_player.tscn`.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.DisableExpensiveSubtreeProcessing` | Cached name for the 'DisableExpensiveSubtreeProcessing' method. |
| `MethodName.IsHitObstructedByOpaqueWall` | Cached name for the 'IsHitObstructedByOpaqueWall' method. |
| `MethodName.OnDropMagEvent` | Cached name for the 'OnDropMagEvent' method. |
| `MethodName.OnJumpEvent` | Cached name for the 'OnJumpEvent' method. |
| `MethodName.OnLandEvent` | Cached name for the 'OnLandEvent' method. |
| `MethodName.OnSimReady` | Cached name for the 'OnSimReady' method. |
| `MethodName.OnTickApplied` | Cached name for the 'OnTickApplied' method. |
| `MethodName.ResolveActiveSlot` | Cached name for the 'ResolveActiveSlot' method. |
| `MethodName.ResolveShot` | Cached name for the 'ResolveShot' method. |
| `MethodName.RunAuthoritativeHitscan` | Cached name for the 'RunAuthoritativeHitscan' method. |
| `MethodName.SetupServerSpectateCamera` | Cached name for the 'SetupServerSpectateCamera' method. |
| `PropertyName._lagCompExcludes` | Cached name for the '_lagCompExcludes' field. |

## Methods

| Name | Summary |
|------|---------|
| `IsHitObstructedByOpaqueWall(PhysicsDirectSpaceState3D, Vector3, Vector3)` | True if an opaque wall (not in group "wallhit") sits between the shooter's eye and the impact. Iterates through penetrable walls (e.g. glass), capped at `MaxPenetrableChain`. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `RunAuthoritativeHitscan(PhysicsDirectSpaceState3D)` | Server-authoritative hitscan with lag compensation: other players' hitboxes are rewound to their historical positions before the cast. On a hit, applies damage, triggers death at HP 0, and broadcasts ShotFired. |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SetupServerSpectateCamera()` | Non-headless server only: adds an eye-level camera childed to the head and makes the body visible, so the operator sees what the player sees. The first server body claims the active camera. |
