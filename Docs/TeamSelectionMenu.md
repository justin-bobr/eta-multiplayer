# TeamSelectionMenu

Team-select overlay shown while in `Spectator` during the competitive handshake: Team1/Team2 buttons plus live rosters from `RemotePlayers`, over a blurred back-buffer. Clicking a side sends `TeamSelect`; auto-removes when `SpawnAuthorized` flips true.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.AddRosterEntry` | Cached name for the 'AddRosterEntry' method. |
| `MethodName.BuildUi` | Cached name for the 'BuildUi' method. |
| `MethodName.MakeTeamButton` | Cached name for the 'MakeTeamButton' method. |
| `MethodName.OnSpectatePressed` | Cached name for the 'OnSpectatePressed' method. |
| `MethodName.OnTeamPressed` | Cached name for the 'OnTeamPressed' method. |
| `MethodName.RefreshRosters` | Cached name for the 'RefreshRosters' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.LayerOrder` | Cached name for the 'LayerOrder' field. |
| `PropertyName._previousMouseMode` | Cached name for the '_previousMouseMode' field. |
| `PropertyName._root` | Cached name for the '_root' field. |
| `PropertyName._rosterRefreshAccum` | Cached name for the '_rosterRefreshAccum' field. |
| `PropertyName._selectionSent` | Cached name for the '_selectionSent' field. |
| `PropertyName._spectateBtn` | Cached name for the '_spectateBtn' field. |
| `PropertyName._spectatorHeader` | Cached name for the '_spectatorHeader' field. |
| `PropertyName._spectatorList` | Cached name for the '_spectatorList' field. |
| `PropertyName._spectatorWrapper` | Cached name for the '_spectatorWrapper' field. |
| `PropertyName._statusLabel` | Cached name for the '_statusLabel' field. |
| `PropertyName._team1Btn` | Cached name for the '_team1Btn' field. |
| `PropertyName._team1Header` | Cached name for the '_team1Header' field. |
| `PropertyName._team1List` | Cached name for the '_team1List' field. |
| `PropertyName._team2Btn` | Cached name for the '_team2Btn' field. |
| `PropertyName._team2Header` | Cached name for the '_team2Header' field. |
| `PropertyName._team2List` | Cached name for the '_team2List' field. |

## Methods

| Name | Summary |
|------|---------|
| `BuildRosterColumn(Label, string, Color)` | Builds one side's roster column (header + VBox); returns the inner VBox for populating entries. |
| `OnSpectatePressed()` | Closes the menu without a TeamSelect packet: stays in Spectator preview-cam mode, no LocalPlayer spawns. |
| `RefreshRosters()` | Rebuilds the Team1/Team2/Spectator lists from NetClient.RemotePlayers (event-driven + 2s polling). |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
