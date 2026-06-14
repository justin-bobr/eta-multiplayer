# World

`Vantix.World`

Root script of world.tscn. Owns the active map's single `Level` registry and exposes it statically (via `Instance`) to the HUD, bot AI, spawn system, and preview-camera cycler. `LevelPath` points at the instanced map node; if unset, `ResolveLevel` falls back to the first `Level` descendant.

## Properties

| Name | Summary |
|------|---------|
| `Instance` | The live World root, or null between scene switches. |
| `Level` | The active map's `Level` registry (lazily resolved), or null before the world is ready. |
| `LevelPath` | Path to the instanced map root (the node carrying the `Level` script). Unset = auto-discover. |

## Fields

| Name | Summary |
|------|---------|
| `MethodName.FindFirstLevel` | Cached name for the 'FindFirstLevel' method. |
| `MethodName.ResolveLevel` | Cached name for the 'ResolveLevel' method. |
| `MethodName._EnterTree` | Cached name for the '_EnterTree' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `PropertyName.LevelPath` | Cached name for the 'LevelPath' property. |
| `PropertyName._level` | Cached name for the '_level' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
