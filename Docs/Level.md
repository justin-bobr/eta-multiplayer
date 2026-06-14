# Level

Per-map registry placed on each map's root node. Holds `NodePath` arrays to the map's authored markers — `Spawn`s, `Zone`s, `BombSpot`s and preview `Camera3D`s — and resolves them into typed lists once on enter-tree. Wired explicitly in the inspector (or baked via `CollectChildren`); no runtime group scans. Accessed globally through `Level`.

## Properties

| Name | Summary |
|------|---------|
| `BombSpotPaths` | Paths to every `BombSpot` (A / B / C plant regions). |
| `CollectChildren` | Inspector "button": tick to (re)populate the four path arrays from descendants by runtime type. Reads back false so it acts as a one-shot rather than a stored flag. |
| `PreviewCamPaths` | Paths to the cinematic preview `Camera3D`s the team-select screen cycles. |
| `Resolved` | True once `EnsureResolved` has turned the path arrays into live node lists. |
| `SpawnPaths` | Paths (relative to this node) to every `Spawn` marker the map defines. |
| `ZonePaths` | Paths to every `Zone` region (HUD area names + bot nav targets). |

## Fields

| Name | Summary |
|------|---------|
| `MethodName.BakePathsFromDescendants` | Cached name for the 'BakePathsFromDescendants' method. |
| `MethodName.BombSpotForSlot` | Cached name for the 'BombSpotForSlot' method. |
| `MethodName.EnsureResolved` | Cached name for the 'EnsureResolved' method. |
| `MethodName.ZoneAt` | Cached name for the 'ZoneAt' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.BombSpotPaths` | Cached name for the 'BombSpotPaths' property. |
| `PropertyName.CollectChildren` | Cached name for the 'CollectChildren' property. |
| `PropertyName.PreviewCamPaths` | Cached name for the 'PreviewCamPaths' property. |
| `PropertyName.Resolved` | Cached name for the 'Resolved' property. |
| `PropertyName.SpawnPaths` | Cached name for the 'SpawnPaths' property. |
| `PropertyName.ZonePaths` | Cached name for the 'ZonePaths' property. |

## Methods

| Name | Summary |
|------|---------|
| `BakePathsFromDescendants()` | Editor-only: walks descendants and rewrites the four path arrays by runtime type. BombSpot/Spawn extend Zone, so they're tested first. |
| `BombSpotForSlot(BombSpot.BombSlot)` | Returns the first `BombSpot` with the matching slot, or null if the map has none. |
| `EnsureResolved()` | Resolves the exported path arrays into typed node lists. Idempotent. Called from `_Ready` and lazily from `Level` for consumers querying before _Ready. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SpawnsForKind(Spawn.SpawnKind)` | Lazy enumeration of every `Spawn` with the matching kind. |
| `ZoneAt(Vector3)` | Returns the smallest-volume `Zone`/`Spawn` area containing the world position (innermost nested zone wins), or null when outside every region. |
