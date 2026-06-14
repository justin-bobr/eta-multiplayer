# WorldFadeOverlay

`Vantix.WorldFadeOverlay`

Autoload CanvasLayer owning a full-screen black ColorRect that masks the hard cut when SceneLoader switches into world.tscn (hides the first-frame render burst). SceneLoader calls `ShowOpaque` before the switch; NetworkPlayer._Ready calls `RequestFadeOut` once preloads + spawn are done. Survives scene switches as an autoload.

## Properties

| Name | Summary |
|------|---------|
| `Instance` | Singleton, set in `_Ready`. |

## Fields

| Name | Summary |
|------|---------|
| `DefaultFadeDurationSec` | Default fade-out duration (s) when `RequestFadeOut` is called without a value. |
| `MethodName.RequestFadeOut` | Cached name for the 'RequestFadeOut' method. |
| `MethodName.ShowOpaque` | Cached name for the 'ShowOpaque' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.LayerOrder` | Cached name for the 'LayerOrder' field. |
| `PropertyName._fadeRemaining` | Cached name for the '_fadeRemaining' field. |
| `PropertyName._fadeTotal` | Cached name for the '_fadeTotal' field. |
| `PropertyName._fading` | Cached name for the '_fading' field. |
| `PropertyName._rect` | Cached name for the '_rect' field. |

## Methods

| Name | Summary |
|------|---------|
| `RequestFadeOut(float)` | Begins a smooth alpha fade-out over `duration` seconds. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `ShowOpaque()` | Snaps the overlay to opaque black immediately. |
