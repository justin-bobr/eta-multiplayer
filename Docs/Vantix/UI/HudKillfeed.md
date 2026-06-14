# HudKillfeed

`Vantix.UI.HudKillfeed`

Top-right killfeed, one row per death: "Attacker (Weapon) -> Victim [HS]". Suicide/world damage (attacker 0 or == victim) shows "✕ Victim" without attacker; own kills highlighted yellow.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.NameOf` | Cached name for the 'NameOf' method. |
| `MethodName.OnDeath` | Cached name for the 'OnDeath' method. |
| `MethodName.WeaponName` | Cached name for the 'WeaponName' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName._list` | Cached name for the '_list' field. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `WeaponName(byte)` | WeaponId lookup; v1 has AR15 only. |
