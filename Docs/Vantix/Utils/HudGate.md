# HudGate

`Vantix.Utils.HudGate`

Auto-hides the game-HUD layers (Hitmarker, Killfeed, Crosshair, HudCs2) while the local player is frozen (team-select/preload) or dead. Meta UI (Console, NetGraph, Scoreboard, MiniProfiler) is not gated. Registered HUDs are refreshed once per frame from `Tick`.

## Properties

| Name | Summary |
|------|---------|
| `ShouldShow` | True when the game-HUD should be visible: a spawned, alive local player exists and no preload/team-select phase is active. |

## Methods

| Name | Summary |
|------|---------|
| `Register(Node)` | Registers a HUD root (CanvasLayer or Control) for auto-hide. Idempotent; stale references drop on next `Tick`. |
| `Reset()` | Clears all registrations — called by NetMain on disconnect / scene-reload. |
| `Tick()` | Per-frame visibility refresh (from NetMain._PhysicsProcess). Drops invalid handles as it goes. |
