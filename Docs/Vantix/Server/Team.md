# Team

`Vantix.Server.Team`

Which spawn pool a player uses. Enum byte values are stable wire-format — do not renumber. Display names live in `Teams`.

## Fields

| Name | Summary |
|------|---------|
| `Deathmatch` | Deathmatch / Free-for-All — marker group "spawn_deathmatch". |
| `Spectator` | Initial state in competitive mode while the player is choosing a team. No spawn pose is assigned, the LocalPlayer is not instantiated, and the client cycles through preview cameras. Switches to Team1/Team2 via `TeamSelect` after which the server replies with `SpawnAuthorize` carrying the real spawn pose. |
| `Team1` | Team 1 — lore-name "VEKTOR". Spawn-marker group "spawn_team1". |
| `Team2` | Team 2 — lore-name "ATLAS-9". Spawn-marker group "spawn_team2". |
