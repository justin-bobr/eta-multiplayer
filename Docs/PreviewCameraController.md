# PreviewCameraController

Cycles through the map's preview `Camera3D`s (`PreviewCams`) while the LocalPlayer hasn't spawned. Each is shown for `DwellSec`, then cross-fades to the next: a frozen snapshot of the current viewport is parked on a top TextureRect while the camera switches underneath, and the overlay alpha lerps 1→0 over `CutFadeSec` — no black flash. Auto-retires when a non-preview camera becomes current.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.ActivateCam` | Cached name for the 'ActivateCam' method. |
| `MethodName.BeginCrossfade` | Cached name for the 'BeginCrossfade' method. |
| `MethodName.CleanupCrossfade` | Cached name for the 'CleanupCrossfade' method. |
| `MethodName.RefreshCameraList` | Cached name for the 'RefreshCameraList' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.CutFadeSec` | Cached name for the 'CutFadeSec' field. |
| `PropertyName.DwellSec` | Cached name for the 'DwellSec' field. |
| `PropertyName._crossfadeLayer` | Cached name for the '_crossfadeLayer' field. |
| `PropertyName._crossfadeRect` | Cached name for the '_crossfadeRect' field. |
| `PropertyName._dwellTimer` | Cached name for the '_dwellTimer' field. |
| `PropertyName._fadeRemaining` | Cached name for the '_fadeRemaining' field. |
| `PropertyName._fading` | Cached name for the '_fading' field. |
| `PropertyName._index` | Cached name for the '_index' field. |
| `PropertyName._retired` | Cached name for the '_retired' field. |

## Methods

| Name | Summary |
|------|---------|
| `BeginCrossfade()` | Snapshots the viewport onto a top-layer TextureRect, then switches the camera underneath; `_Process` drives the alpha-lerp from 1→0. |
| `RefreshCameraList()` | Pulls the preview cameras from `PreviewCams`. Re-invokable if the map changes. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
