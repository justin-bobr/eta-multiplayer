# PeerState

`Vantix.Server.PeerState`

Server-side state stored per connected peer (and per bot).

## Fields

| Name | Summary |
|------|---------|
| `AntiCheatKicked` | True once auto-kick has fired - prevents repeated kicks while disconnect propagates. |
| `Armor` | Kevlar (0..50). Body hits drain it at 50% of damage; headshots bypass it. Does not regen. |
| `LastAckedSnapshotTick` | Most recent snapshot tick the client ACK'd via input packet. `NoBaselineTick` = none yet. |
| `LastDamageTickMs` | Time.GetTicksMsec() of the last damage hit; HP regen starts `RegenDelayMs` after. |
| `LastValidatedPos` | Last validated server position + its tick - basis for the position-delta check. |
| `LastViewYawSample` | Last validated ViewYaw + the tick it came from - basis for the angular-velocity check. |
| `PacketsThisServerTick` | InputPackets accepted this server tick (for the per-peer flood cap); reset when the tick changes. |
| `RecentViolationMs` | Ring of recent violation timestamps for the sliding-window kick check. |
| `SentSnapshots` | Ring of the last ~64 snapshots sent to this peer (post-PVS); delta source keyed by `LastAckedSnapshotTick`. |
| `TeamSlot` | Persistent per-team slot (0..15), drives the per-player colour. Freed only on permanent leave. |
| `WorldReady` | Set on `WorldInitComplete` (asset pre-loads done); broadcast via `WorldReady` so peers show the TPS body. Persists until reconnect. |
