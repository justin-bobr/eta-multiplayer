# HitFeed

Top-center hit feed listing server-confirmed hits involving the local player as shooter or victim ("Shooter -> (PART) -> Victim (-DMG -> HP)"). The server sends `WriteHit` only to shooter and victim. Auto-attached via NetMain.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.FormatLine` | Cached name for the 'FormatLine' method. |
| `MethodName.NameOf` | Cached name for the 'NameOf' method. |
| `MethodName.OnHit` | Cached name for the 'OnHit' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName._list` | Cached name for the '_list' field. |

## Methods

| Name | Summary |
|------|---------|
| `FormatLine(byte, byte, HitboxGroup, byte, byte)` | Formats a single hit row including the kill marker when HP drops to zero. |
| `NameOf(byte)` | Returns the display name for a net id; falls back to "Player {netId}" when no cached name is known. |
| `OnHit(byte, byte, HitboxGroup, byte, byte)` | Adds a new hit row at the top of the feed and trims older rows beyond the cap. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_ExitTree()` | Unsubscribes from the client hit event when leaving the scene tree. |
| `_Process(double)` | Ages each entry, fades it out after the hold window, and removes fully transparent rows. |
| `_Ready()` | Builds the centered top strip and subscribes to the client's hit event. |
