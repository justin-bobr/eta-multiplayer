# HudVitals

Bottom-left vitals strip (no background): health number, armor icon + number, then a health bar and a thinner stamina bar. Rendered via `_Draw`.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.DrawIconCentered` | Cached name for the 'DrawIconCentered' method. |
| `MethodName.DrawNumber` | Cached name for the 'DrawNumber' method. |
| `MethodName.DrawSolidBar` | Cached name for the 'DrawSolidBar' method. |
| `MethodName._Draw` | Cached name for the '_Draw' method. |
| `PropertyName.Armor` | Cached name for the 'Armor' field. |
| `PropertyName.Health` | Cached name for the 'Health' field. |
| `PropertyName.KevlarTex` | Cached name for the 'KevlarTex' property. |
| `PropertyName.Stamina` | Cached name for the 'Stamina' field. |
| `PropertyName.StaminaExhausted` | Cached name for the 'StaminaExhausted' field. |
| `PropertyName._kevlarTex` | Cached name for the '_kevlarTex' field. |

## Methods

| Name | Summary |
|------|---------|
| `DrawIconCentered(Texture2D, Vector2, float, Color)` | Draws a HUD icon texture fitted into maxSize (aspect-preserving), centered at (center), with a soft drop shadow. `mod` modulates color + alpha. |
| `DrawNumber(Font, string, float, float, int, Color)` | Draws a shadowed number; (x, centerY) is the left edge / vertical center. Returns the right edge. |
| `DrawSolidBar(Rect2, float, Color)` | Draws a solid bar: dark track with a proportional fill, no segmenting. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Draw()` | Renders the cross icon, health and armor numbers, and the health and stamina bars. |
