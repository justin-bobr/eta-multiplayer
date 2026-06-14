# MainMenu

`Vantix.UI.MainMenu`

Client-startup main menu: enter a server address, tune settings, or quit. Bypassed (swaps straight to the loading scene) when `AutoConnect` is set or the run mode is Listen/Server.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.AddLabel` | Cached name for the 'AddLabel' method. |
| `MethodName.AttachSettingsMenu` | Cached name for the 'AttachSettingsMenu' method. |
| `MethodName.BuildActions` | Cached name for the 'BuildActions' method. |
| `MethodName.BuildCard` | Cached name for the 'BuildCard' method. |
| `MethodName.BuildFooter` | Cached name for the 'BuildFooter' method. |
| `MethodName.BuildTitle` | Cached name for the 'BuildTitle' method. |
| `MethodName.BuildUi` | Cached name for the 'BuildUi' method. |
| `MethodName.OnConnectPressed` | Cached name for the 'OnConnectPressed' method. |
| `MethodName.OnSettingsPressed` | Cached name for the 'OnSettingsPressed' method. |
| `MethodName.ShowError` | Cached name for the 'ShowError' method. |
| `MethodName.SwapToLoading` | Cached name for the 'SwapToLoading' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName._addressInput` | Cached name for the '_addressInput' field. |
| `PropertyName._errorLabel` | Cached name for the '_errorLabel' field. |
| `PropertyName._nameInput` | Cached name for the '_nameInput' field. |
| `PropertyName._settingsMenu` | Cached name for the '_settingsMenu' field. |

## Methods

| Name | Summary |
|------|---------|
| `AddLabel(VBoxContainer, string, int, Color)` | Helper that appends a small caps-style label above an input. |
| `AttachSettingsMenu()` | Attaches the shared SettingsMenu node. Restores menu-mode mouse + FPS cap afterward because SettingsMenu._Ready calls SetOpen(false), which assumes in-game and would capture the cursor. |
| `BuildActions(VBoxContainer)` | Builds the Connect / Settings / Quit button row. |
| `BuildCard(VBoxContainer)` | Builds the dark card containing the address and name input fields. |
| `BuildFooter(VBoxContainer)` | Builds the version / build label below the action buttons. |
| `BuildTitle(VBoxContainer)` | Adds the title and tagline at the top of the column. |
| `BuildUi()` | Builds the menu UI (background, title, input card, action buttons, footer). |
| `OnConnectPressed()` | Validates the input fields and hands the address to NetMain to start the connect flow. |
| `OnSettingsPressed()` | Opens the embedded settings menu. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `ShowError(string)` | Shows an inline validation error under the input fields. |
| `SwapToLoading()` | Switches to the loading scene that drives the connect / world-load flow. |
| `TryParseAddress(string, string, int)` | Parses "HOST" or "HOST:PORT"; defaults to port 27015 when none is supplied. |
| `_Ready()` | Shows the menu, or advances straight to the loading scene based on CLI mode + auto-connect. |
