# FootstepAudio

Client-side, cosmetic footstep audio bank. Plays material/action-specific sounds (Walk/Sprint/ Jump/Land) triggered by `FootstepController` and `NetworkPlayer`. Library is built at _Ready by scanning res://audio/footsteps/<material>/; files bucket into pools by the second-to-last underscore segment (..._walk_NN etc.) and the floor collider's Godot group must match the folder name. Clips load async/lazily into a process-wide cache shared by all players; steps stay silent until loaded. Local player (`IsLocalPlayer`) uses a non-positional AudioStreamPlayer; remote uses AudioStreamPlayer3D with distance attenuation, optional raycast occlusion (low-pass bus) and "tunnel"-group reverb. Helper buses are created once via `AudioServer`.

## Properties

| Name | Summary |
|------|---------|
| `PendingLoadCount` | Count of clips still loading across all instances. NetworkPlayer polls this to gate the world fade-out. |

## Fields

| Name | Summary |
|------|---------|
| `AudioRoot` | Root folder scanned for material subdirectories. The static library cache is keyed by the first root seen; diverging instances log a warning rather than re-scan. |
| `DefaultGroup` | Fallback material group when the floor collider has no recognized Godot group. |
| `MaxFinalizationsPerFrame` | Max threaded-load finalizations per frame; LoadThreadedGet finalizes on the main thread, so this caps the spike. |
| `MethodName.BuildLibrary` | Cached name for the 'BuildLibrary' method. |
| `MethodName.EnsureHelperBuses` | Cached name for the 'EnsureHelperBuses' method. |
| `MethodName.EnsureLibraryBuilt` | Cached name for the 'EnsureLibraryBuilt' method. |
| `MethodName.EnsureMaterialLoaded` | Cached name for the 'EnsureMaterialLoaded' method. |
| `MethodName.EnsurePool` | Cached name for the 'EnsurePool' method. |
| `MethodName.IsOccluded` | Cached name for the 'IsOccluded' method. |
| `MethodName.Play` | Cached name for the 'Play' method. |
| `MethodName.PlayJump` | Cached name for the 'PlayJump' method. |
| `MethodName.PlayLand` | Cached name for the 'PlayLand' method. |
| `MethodName.PlayStep` | Cached name for the 'PlayStep' method. |
| `MethodName.ResolveMaterial` | Cached name for the 'ResolveMaterial' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `MethodName._ValidateProperty` | Cached name for the '_ValidateProperty' method. |
| `PoolSize` | Number of simultaneously sounding steps (overlap). |
| `PropertyName.AudioRoot` | Cached name for the 'AudioRoot' field. |
| `PropertyName.Bus` | Cached name for the 'Bus' field. |
| `PropertyName.DefaultGroup` | Cached name for the 'DefaultGroup' field. |
| `PropertyName.IsLocalPlayer` | Cached name for the 'IsLocalPlayer' field. |
| `PropertyName.LandVolumeDbBoost` | Cached name for the 'LandVolumeDbBoost' field. |
| `PropertyName.MaxHearDistance` | Cached name for the 'MaxHearDistance' field. |
| `PropertyName.MinHearDistance` | Cached name for the 'MinHearDistance' field. |
| `PropertyName.OcclusionEnabled` | Cached name for the 'OcclusionEnabled' field. |
| `PropertyName.OcclusionLowPassHz` | Cached name for the 'OcclusionLowPassHz' field. |
| `PropertyName.OcclusionMask` | Cached name for the 'OcclusionMask' field. |
| `PropertyName.OcclusionVolumeDb` | Cached name for the 'OcclusionVolumeDb' field. |
| `PropertyName.PitchRandomness` | Cached name for the 'PitchRandomness' field. |
| `PropertyName.PoolSize` | Cached name for the 'PoolSize' field. |
| `PropertyName.ReverbDamping` | Cached name for the 'ReverbDamping' field. |
| `PropertyName.ReverbEnabled` | Cached name for the 'ReverbEnabled' field. |
| `PropertyName.ReverbRoomSize` | Cached name for the 'ReverbRoomSize' field. |
| `PropertyName.ReverbWet` | Cached name for the 'ReverbWet' field. |
| `PropertyName.UnitSize` | Cached name for the 'UnitSize' field. |
| `PropertyName.VolumeDb3D` | Cached name for the 'VolumeDb3D' field. |
| `PropertyName.VolumeDbAtFullLoudness` | Cached name for the 'VolumeDbAtFullLoudness' field. |
| `PropertyName.VolumeDbAtMinLoudness` | Cached name for the 'VolumeDbAtMinLoudness' field. |
| `PropertyName._occlusionQuery` | Cached name for the '_occlusionQuery' field. |
| `PropertyName._occlusionResult` | Cached name for the '_occlusionResult' field. |
| `PropertyName._pool` | Cached name for the '_pool' field. |
| `PropertyName._poolCursor` | Cached name for the '_poolCursor' field. |
| `PropertyName._rng` | Cached name for the '_rng' field. |

## Methods

| Name | Summary |
|------|---------|
| `BuildLibrary(string)` | Scans the root for material subdirectories; files bucket by their _walk_/_sprint_/_jump_/_land_ token. |
| `CollectActiveMaterials()` | Returns the material folder names whose Godot group has nodes in the current scene, plus `DefaultGroup`. |
| `EnsureHelperBuses()` | Creates the "FootstepOccluded" (low-pass) and "FootstepReverb" buses once; both send into `Bus`. |
| `EnsureLibraryBuilt(string)` | Runs `BuildLibrary` once per process. A second instance with a different AudioRoot reuses the existing cache and logs a warning. |
| `EnsureMaterialLoaded(string)` | Kicks off threaded loads for a material's clips (deduplicated via `_materialsTriggered`). Returns whether any new path was queued. |
| `EnsurePool()` | Builds the player pool lazily on first step, so `IsLocalPlayer` (set by NetworkPlayer) is final. |
| `IsOccluded(Vector3)` | True if a raycast from the active camera to the source hits geometry well before it (step is occluded). |
| `Play(FootstepAction, Vector3, StringName, float, float, bool)` | Resolves the material, ensures clips are loaded and plays the chosen action pool. |
| `PlayClip(List<String>, Vector3, float, float, bool)` | Picks a random loaded clip from the bank and plays it on the next pool slot. |
| `PlayJump(Vector3, StringName, float, bool)` | Plays a jump take-off sound. `loudness` is 0..1 (sprinting jumps are louder). |
| `PlayLand(Vector3, StringName, float, bool)` | Plays a landing sound. `impact01` 0..1 scales with fall hardness. |
| `PlayStep(Vector3, StringName, float, bool, bool)` | Plays a walking/running step. `sprinting` chooses Walk vs. Sprint pool. |
| `ResolveMaterial(StringName)` | Maps a material to an existing pool set; unknown maps fall back to `DefaultGroup`. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `TryParseAction(string, FootstepAction)` | Maps a filename's second-to-last underscore segment to a `FootstepAction` ("landing" aliases "land"); false if unmatched. |
| `_Process(double)` | Polls pending threaded loads (rate-limited to `MaxFinalizationsPerFrame` per tick to avoid main-thread spikes) and disables itself once nothing is loading. |
| `_Ready()` | Builds the clip library and preloads only the materials actually used by colliders in the current scene (plus the default fallback and any explicit extras). |
| `_ValidateProperty(Dictionary)` | Presents the `Bus` export as a dropdown of the project's audio buses. |
