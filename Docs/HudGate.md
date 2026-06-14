# HudGate

Toggles the game-HUD layers (Hitmarker, Killfeed, Crosshair, HudCs2, …) on/off based on whether the local player is in a live state. Hidden during team-select/preload (InputGate.LocalPlayerFrozen) and while dead (LastSelfSnap.Hp == 0). Console, NetGraph, Scoreboard and MiniProfiler are intentionally not gated — meta UI accessible regardless of player state. HUDs register and the gate flips their `Visible` once per frame from `Tick`.

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
