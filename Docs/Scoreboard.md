# Scoreboard

Tab-activated scoreboard: header plus per-team sections (badge with score/name/alive + player rows); single-list layout for Deathmatch. Data from `LastSelfSnap` and `LastRemoteSnapshots`. Action `scoreboard` (default Tab), refreshed at 4 Hz.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.BuildColumnHeader` | Cached name for the 'BuildColumnHeader' method. |
| `MethodName.BuildHeader` | Cached name for the 'BuildHeader' method. |
| `MethodName.BuildUi` | Cached name for the 'BuildUi' method. |
| `MethodName.MakeHeaderCell` | Cached name for the 'MakeHeaderCell' method. |
| `MethodName.MakeRowCell` | Cached name for the 'MakeRowCell' method. |
| `MethodName.RefreshRows` | Cached name for the 'RefreshRows' method. |
| `MethodName.TeamBadgeBgColor` | Cached name for the 'TeamBadgeBgColor' method. |
| `MethodName.TeamLabelText` | Cached name for the 'TeamLabelText' method. |
| `MethodName._Input` | Cached name for the '_Input' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName._headerModeLabel` | Cached name for the '_headerModeLabel' field. |
| `PropertyName._headerTimerLabel` | Cached name for the '_headerTimerLabel' field. |
| `PropertyName._master` | Cached name for the '_master' field. |
| `PropertyName._panel` | Cached name for the '_panel' field. |
| `PropertyName._refreshTimer` | Cached name for the '_refreshTimer' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `TeamBadgeBgColor(byte)` | Badge background per team: Team1 blue, Team2 orange, DM grey. |
| `_Input(InputEvent)` | Opens the board on the scoreboard-action press; input still fires while _Process is off. |
