# HudCs2

`Vantix.UI.HudCs2`

Competitive-shooter HUD: vitals, money, score/round, compass, loadout, bomb banner. Values exposed as properties for wiring real data. Creates its own CanvasLayer and layout.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.AnchorCorner` | Cached name for the 'AnchorCorner' method. |
| `MethodName.ApplyHudMargins` | Cached name for the 'ApplyHudMargins' method. |
| `MethodName.BearingToBombSpot` | Cached name for the 'BearingToBombSpot' method. |
| `MethodName.BuildBombBanner` | Cached name for the 'BuildBombBanner' method. |
| `MethodName.BuildBottomRight` | Cached name for the 'BuildBottomRight' method. |
| `MethodName.BuildMoneyBlock` | Cached name for the 'BuildMoneyBlock' method. |
| `MethodName.BuildTopBar` | Cached name for the 'BuildTopBar' method. |
| `MethodName.BuildVitals` | Cached name for the 'BuildVitals' method. |
| `MethodName.MakeLabel` | Cached name for the 'MakeLabel' method. |
| `MethodName.MakeScoreColumn` | Cached name for the 'MakeScoreColumn' method. |
| `MethodName.MakeSoftPanel` | Cached name for the 'MakeSoftPanel' method. |
| `MethodName.NearlyEqualBearing` | Cached name for the 'NearlyEqualBearing' method. |
| `MethodName.Punchy` | Cached name for the 'Punchy' method. |
| `MethodName.SetCornerOffset` | Cached name for the 'SetCornerOffset' method. |
| `MethodName.UpdateAll` | Cached name for the 'UpdateAll' method. |
| `MethodName.UpdateLoadout` | Cached name for the 'UpdateLoadout' method. |
| `MethodName.UpdateZoneLabel` | Cached name for the 'UpdateZoneLabel' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `Player` | Optional player reference. When set, stamina is read from the movement controller. |
| `PropertyName.AmmoCurrent` | Cached name for the 'AmmoCurrent' field. |
| `PropertyName.AmmoReserve` | Cached name for the 'AmmoReserve' field. |
| `PropertyName.Armor` | Cached name for the 'Armor' field. |
| `PropertyName.BombPlanted` | Cached name for the 'BombPlanted' field. |
| `PropertyName.BombTimer` | Cached name for the 'BombTimer' field. |
| `PropertyName.CanvasLayerOrder` | Cached name for the 'CanvasLayerOrder' field. |
| `PropertyName.Health` | Cached name for the 'Health' field. |
| `PropertyName.MaxRounds` | Cached name for the 'MaxRounds' field. |
| `PropertyName.Money` | Cached name for the 'Money' field. |
| `PropertyName.Player` | Cached name for the 'Player' field. |
| `PropertyName.RoundNumber` | Cached name for the 'RoundNumber' field. |
| `PropertyName.RoundTimeSec` | Cached name for the 'RoundTimeSec' field. |
| `PropertyName.ScoreCT` | Cached name for the 'ScoreCT' field. |
| `PropertyName.ScoreT` | Cached name for the 'ScoreT' field. |
| `PropertyName.SmokeCount` | Cached name for the 'SmokeCount' field. |
| `PropertyName.Stamina` | Cached name for the 'Stamina' field. |
| `PropertyName.WeaponName` | Cached name for the 'WeaponName' field. |
| `PropertyName._bombTimerLabel` | Cached name for the '_bombTimerLabel' field. |
| `PropertyName._compass` | Cached name for the '_compass' field. |
| `PropertyName._hudRefreshTimer` | Cached name for the '_hudRefreshTimer' field. |
| `PropertyName._lastActiveSlot` | Cached name for the '_lastActiveSlot' field. |
| `PropertyName._lastAmmoCurrent` | Cached name for the '_lastAmmoCurrent' field. |
| `PropertyName._lastAmmoReserve` | Cached name for the '_lastAmmoReserve' field. |
| `PropertyName._lastArmor` | Cached name for the '_lastArmor' field. |
| `PropertyName._lastBombPlanted` | Cached name for the '_lastBombPlanted' field. |
| `PropertyName._lastBombTimer` | Cached name for the '_lastBombTimer' field. |
| `PropertyName._lastGrenadeCharge` | Cached name for the '_lastGrenadeCharge' field. |
| `PropertyName._lastHeading` | Cached name for the '_lastHeading' field. |
| `PropertyName._lastHealth` | Cached name for the '_lastHealth' field. |
| `PropertyName._lastMarginH` | Cached name for the '_lastMarginH' field. |
| `PropertyName._lastMarginV` | Cached name for the '_lastMarginV' field. |
| `PropertyName._lastMaxRounds` | Cached name for the '_lastMaxRounds' field. |
| `PropertyName._lastMoney` | Cached name for the '_lastMoney' field. |
| `PropertyName._lastRoundNumber` | Cached name for the '_lastRoundNumber' field. |
| `PropertyName._lastRoundTimeSec` | Cached name for the '_lastRoundTimeSec' field. |
| `PropertyName._lastScoreCT` | Cached name for the '_lastScoreCT' field. |
| `PropertyName._lastScoreT` | Cached name for the '_lastScoreT' field. |
| `PropertyName._lastSiteABearing` | Cached name for the '_lastSiteABearing' field. |
| `PropertyName._lastSiteBBearing` | Cached name for the '_lastSiteBBearing' field. |
| `PropertyName._lastSiteCBearing` | Cached name for the '_lastSiteCBearing' field. |
| `PropertyName._lastSmokeCount` | Cached name for the '_lastSmokeCount' field. |
| `PropertyName._lastStamina` | Cached name for the '_lastStamina' field. |
| `PropertyName._lastStaminaExhausted` | Cached name for the '_lastStaminaExhausted' field. |
| `PropertyName._lastWeaponName` | Cached name for the '_lastWeaponName' field. |
| `PropertyName._lastZoneText` | Cached name for the '_lastZoneText' field. |
| `PropertyName._layer` | Cached name for the '_layer' field. |
| `PropertyName._moneyLabel` | Cached name for the '_moneyLabel' field. |
| `PropertyName._navChecked` | Cached name for the '_navChecked' field. |
| `PropertyName._roundLabel` | Cached name for the '_roundLabel' field. |
| `PropertyName._scoreCTLabel` | Cached name for the '_scoreCTLabel' field. |
| `PropertyName._scoreTLabel` | Cached name for the '_scoreTLabel' field. |
| `PropertyName._timeLabel` | Cached name for the '_timeLabel' field. |
| `PropertyName._topCol` | Cached name for the '_topCol' field. |
| `PropertyName._vitals` | Cached name for the '_vitals' field. |
| `PropertyName._weaponSlots` | Cached name for the '_weaponSlots' field. |
| `PropertyName._zoneLabel` | Cached name for the '_zoneLabel' field. |

## Methods

| Name | Summary |
|------|---------|
| `AnchorCorner(Control, float, float, float, float, Control.GrowDirection, Control.GrowDirection)` | Anchors a content-sized control to a screen corner. |
| `ApplyHudMargins()` | Applies the `Settings` HUD edge margins to corner-anchored elements; cached, no-op when unchanged. |
| `BearingToBombSpot(Vantix.Levels.BombSpot.BombSlot)` | Compass bearing (0..360°, north = -Z) from the player to the slot's BombSpot; NaN when the map has none. |
| `BuildBombBanner()` | Builds the center-bottom bomb banner panel; hidden by default and shown when the bomb is planted. |
| `BuildBottomRight()` | Builds the bottom-right loadout strip (weapon, ammo, equipment slots). |
| `BuildMoneyBlock()` | Builds the semi-transparent money text in the top-left corner. |
| `BuildTopBar()` | Builds the top-center column: compass strip, score row, and round label. |
| `BuildVitals()` | Builds the bottom-left vitals strip (health number plus health and stamina bars). |
| `MakeLabel(string, int, Color)` | Builds a label with a light outline plus shadow so it stays legible without a background box. |
| `MakeScoreColumn(string, Label)` | Builds a score column with a large number on top and a small caption below. |
| `MakeSoftPanel(int, int, int, int)` | Builds a soft, rounded, semi-transparent dark panel used only where a background helps legibility. |
| `NearlyEqualBearing(float, float)` | Compares two bearings (in degrees, NaN allowed) for near equality within 0.5 degrees. |
| `Punchy(Label)` | Applies a stronger outline so a label stays legible against bright backgrounds. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SetCornerOffset(Control, float, float)` | Sets the four offsets of a corner-anchored control while keeping its anchor and grow settings. |
| `UpdateAll()` | Pushes the cached HUD values into their widgets, only queueing redraws when something actually changed. |
| `UpdateLoadout()` | Pushes loadout state to the weapon slot widget when any of its inputs change. |
| `UpdateZoneLabel()` | Writes the name of the `Zone` under the player (via `ZoneAt`) into the zone label. |
| `_Process(double)` | Drives the per-frame compass refresh and the throttled refresh of the rest of the HUD. |
| `_Ready()` | Resolves the player reference, creates the canvas layer, and builds all HUD sub-elements. |
