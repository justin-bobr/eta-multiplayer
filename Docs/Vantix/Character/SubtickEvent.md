# SubtickEvent

`Vantix.Character.SubtickEvent`

An input change at a fractional position within a tick, for subtick movement replay.

## Fields

| Name | Summary |
|------|---------|
| `StateAfter` | Full held-state bitmask AFTER this event applies (the bits the player is holding from this instant onward until the next event). |
| `TFraction` | Position inside the tick, 0..1 = tick-start..tick-end. Events must be sorted ascending. |
| `ViewPitch` | View pitch at this event. |
| `ViewYaw` | View yaw at this event, used for the substep starting here. |
