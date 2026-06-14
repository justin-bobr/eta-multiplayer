# Spawn

A respawn region extending `Zone`; players land at the area centre (or a sampled cell when several spawn together). The `Kind` tag selects the mode/team pool, resolved by `SpawnManager` via `SpawnsForKind`.

## Properties

| Name | Summary |
|------|---------|
| `Kind` | Spawn pool (Deathmatch/Team1/Team2) this region belongs to; resolved via `SpawnsForKind`. |

## Fields

| Name | Summary |
|------|---------|
| `PropertyName.Kind` | Cached name for the 'Kind' property. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
