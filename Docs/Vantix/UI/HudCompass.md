# HudCompass

`Vantix.UI.HudCompass`

Floating compass strip (no background box). HUD feeds `HeadingDegrees` each frame; ticks every 5°, numeric labels every 15°, cardinals every 45°, red center marker.

## Fields

| Name | Summary |
|------|---------|
| `HeadingDegrees` | Current heading in degrees (0..360); set by the HUD each frame. |
| `MethodName.DrawSiteMarker` | Cached name for the 'DrawSiteMarker' method. |
| `MethodName.DrawText` | Cached name for the 'DrawText' method. |
| `MethodName._Draw` | Cached name for the '_Draw' method. |
| `PropertyName.HeadingDegrees` | Cached name for the 'HeadingDegrees' field. |
| `PropertyName.SiteABearing` | Cached name for the 'SiteABearing' field. |
| `PropertyName.SiteBBearing` | Cached name for the 'SiteBBearing' field. |
| `PropertyName.SiteCBearing` | Cached name for the 'SiteCBearing' field. |
| `SiteABearing` | Compass bearing to bombsite A; NaN hides the marker. |
| `SiteBBearing` | Compass bearing to bombsite B; NaN hides the marker. |
| `SiteCBearing` | Compass bearing to bombsite C; NaN hides the marker (standard 2-site maps leave this NaN). |
| `VisibleRange` | Degrees of arc visible across the full strip width. |

## Methods

| Name | Summary |
|------|---------|
| `DrawSiteMarker(Font, string, float)` | Draws a bombsite marker at its bearing; off-screen targets clamp to the edge with a direction arrow. |
| `DrawText(Font, string, float, float, int, Color)` | Draws centered text with a shadow; floating, with no box background. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Draw()` | Renders ticks, numeric labels, the center marker, and any active objective markers. |
