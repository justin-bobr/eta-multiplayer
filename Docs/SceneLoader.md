# SceneLoader

Loading screen and startup scene. Loads world.tscn in the background (threaded ResourceLoader) with a progress bar, then switches the tree once ready. Code-driven UI; the .tscn only holds the root node.

## Fields

| Name | Summary |
|------|---------|
| `DefaultFootstepGroup` | Fallback footstep material; mirrors FootstepAudio.DefaultGroup so its clips preload even when the map has no "dirt" nodes. |
| `MethodName.BeginAudioPreload` | Cached name for the 'BeginAudioPreload' method. |
| `MethodName.BeginWorldLoad` | Cached name for the 'BeginWorldLoad' method. |
| `MethodName.BuildUi` | Cached name for the 'BuildUi' method. |
| `MethodName.PollAudioPreload` | Cached name for the 'PollAudioPreload' method. |
| `MethodName.PollLoad` | Cached name for the 'PollLoad' method. |
| `MethodName.SetPhase` | Cached name for the 'SetPhase' method. |
| `MethodName.StyleBar` | Cached name for the 'StyleBar' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName._audioFinalizedCount` | Cached name for the '_audioFinalizedCount' field. |
| `PropertyName._bar` | Cached name for the '_bar' field. |
| `PropertyName._failed` | Cached name for the '_failed' field. |
| `PropertyName._loaded` | Cached name for the '_loaded' field. |
| `PropertyName._loadedScene` | Cached name for the '_loadedScene' field. |
| `PropertyName._percent` | Cached name for the '_percent' field. |
| `PropertyName._phase` | Cached name for the '_phase' field. |
| `PropertyName._phaseTimer` | Cached name for the '_phaseTimer' field. |
| `PropertyName._progress` | Cached name for the '_progress' field. |
| `PropertyName._shownRatio` | Cached name for the '_shownRatio' field. |
| `PropertyName._statusLabel` | Cached name for the '_statusLabel' field. |
| `PropertyName._switched` | Cached name for the '_switched' field. |
| `PropertyName._targetRatio` | Cached name for the '_targetRatio' field. |
| `PropertyName._worldLoadStartMs` | Cached name for the '_worldLoadStartMs' field. |

## Methods

| Name | Summary |
|------|---------|
| `BeginAudioPreload()` | Scans res://audio/footsteps/ and threaded-loads only the surface groups actually referenced by colliders in the loaded PackedScene (recursively across sub-scenes), plus `DefaultFootstepGroup` as the fallback. Skipped entirely on a dedicated server (never plays footstep audio). |
| `BeginWorldLoad()` | Kicks off the threaded world load. |
| `BuildUi()` | Builds the black loading screen with the logo top-right and a centered white bar (code-driven UI). |
| `ExtractSceneGroups(PackedScene)` | Recursively walks a PackedScene's SceneState (and instanced sub-scenes) to collect every group name without instantiating; callers match these against res://audio/footsteps/ folders. Recursion is needed because sub-scenes are opaque instance pointers in the parent state. Cycle-guarded via a visited set of PackedScene instance ids. |
| `PollAudioPreload()` | Counts queued audio paths that have reached a terminal state; drives the percent label and phase completion. |
| `PollLoad()` | Polls the background load and updates target progress/status. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SetPhase(SceneLoader.LoadPhase, string)` | Switches to the given phase and resets per-phase progress state. |
| `StyleBar(ProgressBar)` | White bar: subtle track, opaque white fill — both rounded. |
| `_Process(double)` | Per-frame phase driver: polls load progress, animates pulsing bars and triggers the scene switch. |
| `_Ready()` | Builds the UI and decides whether to start connecting (client) or loading directly (listen/server). |
