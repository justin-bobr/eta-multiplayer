# WeaponAudio

`Vantix.Fx.WeaponAudio`

Client-side, cosmetic weapon audio bank. Plays shoot/reload/dry-fire sounds triggered by the controller via `NetworkPlayer`. Clip paths live per weapon in `WeaponStats` and are passed in on playback, so swapping weapon swaps all sounds. A shot layers Body (main, reverb bus) + Mech (dry) + Tail (reverb), sharing one per-shot pitch roll. Reverb uses three environment buses (Outdoor/Indoor/Tunnel) selected via `ReverbEnv`. Clips load async into a process-wide cache shared by all players (`Preload` warms the starting weapon); not-yet-loaded clips are silent. Mirrors `FootstepAudio` for local (non-positional) vs remote (3D + distant clips + occlusion) playback.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.AddReverbBus` | Cached name for the 'AddReverbBus' method. |
| `MethodName.EmitLayer` | Cached name for the 'EmitLayer' method. |
| `MethodName.EnsureHelperBuses` | Cached name for the 'EnsureHelperBuses' method. |
| `MethodName.EnsureLoaded` | Cached name for the 'EnsureLoaded' method. |
| `MethodName.EnsurePool` | Cached name for the 'EnsurePool' method. |
| `MethodName.IsOccluded` | Cached name for the 'IsOccluded' method. |
| `MethodName.ListenerDistance` | Cached name for the 'ListenerDistance' method. |
| `MethodName.ReverbBusFor` | Cached name for the 'ReverbBusFor' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.Bus` | Cached name for the 'Bus' field. |
| `PropertyName.DryFireMaxHearDistance` | Cached name for the 'DryFireMaxHearDistance' field. |
| `PropertyName.IndoorRoom` | Cached name for the 'IndoorRoom' field. |
| `PropertyName.IndoorWet` | Cached name for the 'IndoorWet' field. |
| `PropertyName.IsLocalPlayer` | Cached name for the 'IsLocalPlayer' field. |
| `PropertyName.MaxHearDistance` | Cached name for the 'MaxHearDistance' field. |
| `PropertyName.MechLayerVolumeDb` | Cached name for the 'MechLayerVolumeDb' field. |
| `PropertyName.OcclusionEnabled` | Cached name for the 'OcclusionEnabled' field. |
| `PropertyName.OcclusionLowPassHz` | Cached name for the 'OcclusionLowPassHz' field. |
| `PropertyName.OcclusionMask` | Cached name for the 'OcclusionMask' field. |
| `PropertyName.OcclusionVolumeDb` | Cached name for the 'OcclusionVolumeDb' field. |
| `PropertyName.OutdoorRoom` | Cached name for the 'OutdoorRoom' field. |
| `PropertyName.OutdoorWet` | Cached name for the 'OutdoorWet' field. |
| `PropertyName.PitchRandomness` | Cached name for the 'PitchRandomness' field. |
| `PropertyName.PoolSize` | Cached name for the 'PoolSize' field. |
| `PropertyName.ReloadMaxHearDistance` | Cached name for the 'ReloadMaxHearDistance' field. |
| `PropertyName.ReverbDamping` | Cached name for the 'ReverbDamping' field. |
| `PropertyName.ReverbEnabled` | Cached name for the 'ReverbEnabled' field. |
| `PropertyName.TailLayerVolumeDb` | Cached name for the 'TailLayerVolumeDb' field. |
| `PropertyName.TunnelRoom` | Cached name for the 'TunnelRoom' field. |
| `PropertyName.TunnelWet` | Cached name for the 'TunnelWet' field. |
| `PropertyName.UnitSize` | Cached name for the 'UnitSize' field. |
| `PropertyName.VolumeDb3D` | Cached name for the 'VolumeDb3D' field. |
| `PropertyName._occlusionQuery` | Cached name for the '_occlusionQuery' field. |
| `PropertyName._occlusionResult` | Cached name for the '_occlusionResult' field. |
| `PropertyName._pool` | Cached name for the '_pool' field. |
| `PropertyName._poolCursor` | Cached name for the '_poolCursor' field. |
| `PropertyName._rng` | Cached name for the '_rng' field. |

## Methods

| Name | Summary |
|------|---------|
| `AddReverbBus(string, float, float)` | Adds a reverb bus sending to `Bus`, configured with the given room size and wet level. |
| `EmitLayer(String[], float, StringName, Vector3, float, float)` | Plays a random clip from `clips` on the next pool node; returns the path played or null. |
| `EnsureHelperBuses()` | Creates the occlusion low-pass bus and the three reverb buses once; all send to `Bus`. |
| `EnsureLoaded(String[])` | Kicks off threaded loads for any not-yet-loaded paths, deduplicated process-wide. |
| `EnsurePool()` | Builds the player pool lazily on first sound, so `IsLocalPlayer` (set by NetworkPlayer) is final. |
| `IsOccluded(Vector3)` | True if a raycast from the active camera to the source hits the map well before it (occluded). |
| `ListenerDistance(Vector3)` | Distance from the audio listener (active camera) to a sound source. |
| `PlayDryFire(Vantix.Weapon.WeaponStats, Vector3)` | Plays the dry-fire click (empty magazine). Very short 3D hearing range. |
| `PlayReload(Vantix.Weapon.WeaponStats, Vector3)` | Plays the reload sound (single layer, dry). Triggered on the reload rising edge. |
| `PlayShoot(Vantix.Weapon.WeaponStats, Vector3, Vantix.Fx.ReverbEnv)` | Plays a layered shot (Body + Mech + Tail) with environment reverb; remote players also use the distant clip set and occlusion. |
| `Preload(Vantix.Weapon.WeaponStats)` | Pre-loads all clips referenced by a weapon (typical caller: NetworkPlayer._Ready with the starting weapon). |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `ReverbBusFor(Vantix.Fx.ReverbEnv)` | Maps a `ReverbEnv` value to the matching reverb bus name. |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Process(double)` | Polls in-flight threaded loads and disables itself once the queue is empty. |
| `_Ready()` | Disables _Process until a load is in flight — saves per-tick overhead when idle. |
