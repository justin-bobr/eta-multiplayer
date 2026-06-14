# NetMain

`Vantix.Net.NetMain`

Top-level netcode boot, loaded as an autoload so it exists before any scene. Spawns `NetServer` and/or `NetClient` per the parsed `NetCli`. Polls LiteNetLib every physics tick at ProcessPriority = -100 so inputs/snapshots arrive before `_PhysicsProcess`.

## Properties

| Name | Summary |
|------|---------|
| `LocalPlayer` | The local player — instantiated by NetMain into the Players container after SpawnAck. |

## Fields

| Name | Summary |
|------|---------|
| `MethodName.BuildViewportTimesReport` | Cached name for the 'BuildViewportTimesReport' method. |
| `MethodName.ConnectToServer` | Cached name for the 'ConnectToServer' method. |
| `MethodName.CreateAndStartClient` | Cached name for the 'CreateAndStartClient' method. |
| `MethodName.EnsureViewportMeasurement` | Cached name for the 'EnsureViewportMeasurement' method. |
| `MethodName.FindLocalPlayer` | Cached name for the 'FindLocalPlayer' method. |
| `MethodName.HandleDisconnect` | Cached name for the 'HandleDisconnect' method. |
| `MethodName.MeasuredGpuMs` | Cached name for the 'MeasuredGpuMs' method. |
| `MethodName.MeasuredRenderCpuMs` | Cached name for the 'MeasuredRenderCpuMs' method. |
| `MethodName.OnClientSpawned` | Cached name for the 'OnClientSpawned' method. |
| `MethodName.RequestDisconnect` | Cached name for the 'RequestDisconnect' method. |
| `MethodName.RequestReconnect` | Cached name for the 'RequestReconnect' method. |
| `MethodName.TrackFrameSpike` | Cached name for the 'TrackFrameSpike' method. |
| `MethodName.TryInitializeLocalPlayer` | Cached name for the 'TryInitializeLocalPlayer' method. |
| `MethodName.TryInitializeTeamSelectFlow` | Cached name for the 'TryInitializeTeamSelectFlow' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._PhysicsProcess` | Cached name for the '_PhysicsProcess' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PostDisconnectIdle` | True in the post-disconnect idle state (set on disconnect, cleared on Reconnect/Quit). SceneLoader checks this to suppress its auto-connect logic. |
| `PropertyName.LocalPlayer` | Cached name for the 'LocalPlayer' property. |
| `PropertyName._characterScene` | Cached name for the '_characterScene' field. |
| `PropertyName._disconnectScreen` | Cached name for the '_disconnectScreen' field. |
| `PropertyName._drawCallsLast` | Cached name for the '_drawCallsLast' field. |
| `PropertyName._gen0Last` | Cached name for the '_gen0Last' field. |
| `PropertyName._gen1Last` | Cached name for the '_gen1Last' field. |
| `PropertyName._gen2Last` | Cached name for the '_gen2Last' field. |
| `PropertyName._heapLast` | Cached name for the '_heapLast' field. |
| `PropertyName._localPlayerInitialized` | Cached name for the '_localPlayerInitialized' field. |
| `PropertyName._nextViewportScanAt` | Cached name for the '_nextViewportScanAt' field. |
| `PropertyName._nodeCountLast` | Cached name for the '_nodeCountLast' field. |
| `PropertyName._objCountLast` | Cached name for the '_objCountLast' field. |
| `PropertyName._orphanLast` | Cached name for the '_orphanLast' field. |
| `PropertyName._physActiveLast` | Cached name for the '_physActiveLast' field. |
| `PropertyName._physIslandsLast` | Cached name for the '_physIslandsLast' field. |
| `PropertyName._physPairsLast` | Cached name for the '_physPairsLast' field. |
| `PropertyName._spikeTrackerInited` | Cached name for the '_spikeTrackerInited' field. |
| `PropertyName._teamSelectFlowInitialized` | Cached name for the '_teamSelectFlowInitialized' field. |
| `PropertyName._timePhysProcessLast` | Cached name for the '_timePhysProcessLast' field. |
| `PropertyName._timeProcessLast` | Cached name for the '_timeProcessLast' field. |
| `PropertyName._vramLast` | Cached name for the '_vramLast' field. |

## Methods

| Name | Summary |
|------|---------|
| `ConnectToServer(string, int)` | Main-menu Connect button: applies host/port, starts the client, switches to the loading scene. |
| `CreateAndStartClient()` | Creates a NetClient, wires its event subscribers, and starts it. |
| `FindLocalPlayer()` | Returns `LocalPlayer` for other systems (Crosshair, DebugOverlay, reconcile). |
| `MeasuredGpuMs()` | GPU frame time (ms): sum of measured render times across all tracked viewports. 0 until measurement is enabled. |
| `MeasuredRenderCpuMs()` | Render-thread CPU time (ms) summed over all tracked viewports. Companion to `MeasuredGpuMs`. |
| `OnClientSpawned()` | Called from `NetClient` as soon as SpawnAck has arrived. |
| `RequestDisconnect(string)` | User-initiated disconnect: stops the NetClient and routes through the same cleanup as a transport drop. Unsubscribes from `OnDisconnected` first so cleanup runs once. |
| `RequestReconnect()` | `DisconnectScreen` reconnect button: tears down the old client, starts a fresh one, and re-enters loading.tscn to rerun the connect flow. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `TrackFrameSpike(double)` | Logs frame spikes above `SpikeThresholdSec` with GC stats and Godot perf deltas. Gated on Dbg.Enabled so production builds carry no overhead. |
| `TryInitializeLocalPlayer()` | Instantiates the LocalPlayer + HUD into the Players container once SpawnAck has arrived, the spawn is authorized, and world.tscn is active. |
| `TryInitializeTeamSelectFlow()` | One-shot: on a Spectator SpawnAck (competitive, no spawn yet), spawns the PreviewCameraController and TeamSelectionMenu. Both self-destruct once SpawnAuthorize arrives. Skipped for deathmatch. |
| `_ExitTree()` | Tears down networking resources on shutdown. |
| `_PhysicsProcess(double)` | Pumps server and client every physics tick and lazily spawns the local player when ready. |
| `_Ready()` | Parses the CLI and applies settings. Server/Listen/auto-connect Client start immediately; a Client without `--connect` waits for the menu to call `ConnectToServer`. |
