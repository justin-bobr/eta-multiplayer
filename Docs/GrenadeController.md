# GrenadeController

Pure-logic grenade charge: longer fire-hold = stronger throw (0..1). Deterministic, so identical input streams yield identical `ThrownCharge` on client and server. Godot-independent (like `MovementController`) so the server can replay it.

## Fields

| Name | Summary |
|------|---------|
| `Sv` | Tuning reference; defaults to `Sv`. |

## Methods

| Name | Summary |
|------|---------|
| `Step(GrenadeInput)` | Server-replayable step. Detects the release edge and triggers the throw. |
