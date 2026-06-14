# SpawnManager

`Vantix.Server.SpawnManager`

Spawn-marker management for round-based modes (CT vs T) and Deathmatch. On map load it reads the active map's `Level` registry and buckets each `Spawn` by `SpawnKind`. Mapper convention: add `Spawn` nodes (set Kind + Size), list them in `SpawnPaths`, provide ~4-10 per team. Falls back to a hard-coded mid-map position when the map has no spawns.

## Fields

| Name | Summary |
|------|---------|
| `DefaultPos` | Default spawn position used when the map has no markers at all. |
| `FreeRadius` | Minimum distance (metres) to an already occupied spawn before a slot is considered free. |

## Methods

| Name | Summary |
|------|---------|
| `AreaToPoint(Vantix.Levels.Spawn)` | Converts a Spawn (Area3D) into a SpawnPoint using the area's centre and yaw. |
| `IsFree(Vector3, IReadOnlyList<Vector3>)` | Returns true when no occupied position lies within FreeRadius of the candidate slot. |
| `PickFreeSpawn(Vantix.Server.Team, IReadOnlyList<Vector3>)` | Picks a free spawn slot for the requested team, falling back to the other team, Deathmatch, or DefaultPos. |
| `PickFromList(List<Vantix.Server.SpawnManager.SpawnPoint>, int, IReadOnlyList<Vector3>)` | Rotates through the list and returns the first slot passing the FreeRadius check; falls back to a rotating slot if all are occupied. |
| `Scan(SceneTree)` | Reads the spawns from the active map's `Level` registry; idempotent and safe to re-call on map reload. The `tree` argument is kept for call-site compatibility but is no longer walked — spawns come from `Level`. |
