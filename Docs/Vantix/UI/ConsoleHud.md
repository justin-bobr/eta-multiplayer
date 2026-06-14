# ConsoleHud

`Vantix.UI.ConsoleHud`

Quake-style dev console (toggle hotkey default ^). Routes "sv_*" to server ConVars (via ConVarSync packet), "cl_*" to client ConVars, plus built-ins (echo/help/clear/quit/history); else ConVars.TrySet. ↑/↓ history (max 64); typeahead lists top-10 matching ConVars, Tab completes, Enter selects.

## Properties

| Name | Summary |
|------|---------|
| `Instance` | Most recently created ConsoleHud; used by `HandleServerLog` to echo server-broadcast messages into the panel. Cleared in `_ExitTree`. |
| `IsAnyOpen` | True while the console is open; InputGate reads this to block movement/fire/look during typing. |

## Fields

| Name | Summary |
|------|---------|
| `MethodName.ApplySuggestion` | Cached name for the 'ApplySuggestion' method. |
| `MethodName.BuildUi` | Cached name for the 'BuildUi' method. |
| `MethodName.Execute` | Cached name for the 'Execute' method. |
| `MethodName.HideSuggestions` | Cached name for the 'HideSuggestions' method. |
| `MethodName.OnInputGuiEvent` | Cached name for the 'OnInputGuiEvent' method. |
| `MethodName.OnInputSubmitted` | Cached name for the 'OnInputSubmitted' method. |
| `MethodName.OnInputTextChanged` | Cached name for the 'OnInputTextChanged' method. |
| `MethodName.OnSuggestionActivated` | Cached name for the 'OnSuggestionActivated' method. |
| `MethodName.PrintLine` | Cached name for the 'PrintLine' method. |
| `MethodName.SetOpen` | Cached name for the 'SetOpen' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `MethodName._UnhandledInput` | Cached name for the '_UnhandledInput' method. |
| `PropertyName.LayerOrder` | Cached name for the 'LayerOrder' field. |
| `PropertyName._historyIdx` | Cached name for the '_historyIdx' field. |
| `PropertyName._input` | Cached name for the '_input' field. |
| `PropertyName._isOpen` | Cached name for the '_isOpen' field. |
| `PropertyName._log` | Cached name for the '_log' field. |
| `PropertyName._root` | Cached name for the '_root' field. |
| `PropertyName._suggestions` | Cached name for the '_suggestions' field. |

## Methods

| Name | Summary |
|------|---------|
| `ApplySuggestion(string)` | Writes the ConVar name plus a trailing space into the LineEdit and closes the suggestion list. |
| `OnInputGuiEvent(InputEvent)` | ↑/↓ scrolls history or navigates the typeahead list when visible; Tab autocompletes; Esc closes it. |
| `OnInputTextChanged(string)` | Refreshes the typeahead from the command token before the first space; ignores arguments. |
| `PrintLine(string)` | Appends a line to the log and trims it to `MaxLogLines`. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
