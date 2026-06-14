# ProcessPriorityBoost

Autoload that raises process priority to High on startup (smoother frame-time under load). Realtime is avoided — it can destabilise the OS and needs admin. High needs no admin on Windows; on Linux it maps to nice -10 (requires CAP_SYS_NICE).

## Fields

| Name | Summary |
|------|---------|
| `MethodName._Ready` | Cached name for the '_Ready' method. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
