# EncodedInput

`Vantix.Net.EncodedInput`

Wire-quantised form of one tick's input (packed view angles, wishdir and subtick events).

## Fields

| Name | Summary |
|------|---------|
| `EventCount` | Valid entries in `Events`. 0 = server takes the legacy single-segment path. Capped at `MaxSubtickEventsWire`. |
| `Events` | Subtick events, length == EventCount. Null when EventCount = 0. |
| `FireSubTick` | Sub-tick fire-press offset (0..255 → 0..0.996 of a tick), for fractional-tick lag-comp rewind. Only meaningful when `Flags1` bit 7 (firePressed) is set; otherwise 0. |
| `InitialBits` | InputBits at the start of the tick (t=0); seeds the server's subtick replay. 0 on legacy paths. |
