# InputPacket

`Vantix.Net.InputPacket`

Decoded per-tick input (view, wishdir, buttons and subtick events) the simulation steps on.

## Fields

| Name | Summary |
|------|---------|
| `Events` | Subtick events ordered by TFraction ascending. Null/empty for tick-quantised inputs. |
| `FireSubTick` | Sub-tick offset of the fire-press edge (0..255 → 0..0.996 of a tick). Only meaningful when `FirePressed` is true. Server adds `FireSubTick / 256f` to the lag-comp rewind tick. |
