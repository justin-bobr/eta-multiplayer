# HudWeaponSlots

`Vantix.UI.HudWeaponSlots`

Bottom-right loadout strip (no background): weapon silhouette, ammo, and two equipment slots. Active slot is brighter with a red accent underscore. Rendered via `_Draw`.

## Fields

| Name | Summary |
|------|---------|
| `ActiveSlot` | 0 = primary weapon, 1 = equipment slot 1, 2 = equipment slot 2. |
| `AmmoReserve` | Reserve ammo; -1 renders as the infinity glyph. |
| `GrenadeCharge` | Grenade throw charge in the 0..1 range. |
| `MethodName.DrawAmmo` | Cached name for the 'DrawAmmo' method. |
| `MethodName.DrawCentered` | Cached name for the 'DrawCentered' method. |
| `MethodName.DrawIcon` | Cached name for the 'DrawIcon' method. |
| `MethodName.DrawRightAligned` | Cached name for the 'DrawRightAligned' method. |
| `MethodName.DrawSeparator` | Cached name for the 'DrawSeparator' method. |
| `MethodName.DrawWeaponCell` | Cached name for the 'DrawWeaponCell' method. |
| `MethodName._Draw` | Cached name for the '_Draw' method. |
| `PropertyName.ActiveSlot` | Cached name for the 'ActiveSlot' field. |
| `PropertyName.AmmoCurrent` | Cached name for the 'AmmoCurrent' field. |
| `PropertyName.AmmoReserve` | Cached name for the 'AmmoReserve' field. |
| `PropertyName.Equip2Count` | Cached name for the 'Equip2Count' field. |
| `PropertyName.GrenadeCharge` | Cached name for the 'GrenadeCharge' field. |
| `PropertyName.RifleTex` | Cached name for the 'RifleTex' property. |
| `PropertyName.SmokeCount` | Cached name for the 'SmokeCount' field. |
| `PropertyName.SmokeTex` | Cached name for the 'SmokeTex' property. |
| `PropertyName._rifleTex` | Cached name for the '_rifleTex' field. |
| `PropertyName._smokeTex` | Cached name for the '_smokeTex' field. |
| `SmokeCount` | Smoke count for equipment slot 1; -1 renders as the infinity glyph. |

## Methods

| Name | Summary |
|------|---------|
| `DrawAmmo(Font, float)` | Draws the central ammo block: weapon name caption, current ammo (large), and reserve ammo (small). |
| `DrawCentered(Font, string, Vector2, int, Color)` | Draws horizontally centered text with a soft shadow, no background. |
| `DrawIcon(Texture2D, float, float, float, float, Color)` | Draws a HUD icon texture fitted (aspect-preserving) into maxW × maxH, centered at (cx, cy), with a soft drop shadow. `mod` modulates color + alpha. |
| `DrawRightAligned(Font, string, float, float, int, Color)` | Draws right-aligned text with a soft shadow, no background. |
| `DrawSeparator(float)` | Faint vertical divider separating the weapon + ammo block from the equipment slots. |
| `DrawWeaponCell(Font, float, float, string, bool, bool, int)` | Draws a single weapon or equipment cell, including the active-slot accent and optional charge bar. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Draw()` | Renders the four cells (weapon, ammo, equipment 1, equipment 2) left to right. |
