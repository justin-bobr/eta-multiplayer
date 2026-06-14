# BombSpot

A named bomb plant region (A/B/C), extending `Zone` and adding a `Slot` tag. Used for HUD compass markers (via `BombSpotForSlot`) and as bot navigation targets. Resolved through the `Level` registry by slot, not by groups.

## Properties

| Name | Summary |
|------|---------|
| `Slot` | Plant slot (A/B/C) this spot represents; resolved via the `Level` registry. |

## Fields

| Name | Summary |
|------|---------|
| `PropertyName.Slot` | Cached name for the 'Slot' property. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
