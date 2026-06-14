# RewindBuffer

`Vantix.Server.RewindBuffer`

Per-agent ring of server-authoritative positions per tick. `Hitscan` rewinds agents here for lag compensation. Holds 128 ticks (1s @ 128Hz).

## Methods

| Name | Summary |
|------|---------|
| `Clear()` | Clears all recorded entries. |
| `Push(uint, Vector3)` | Appends a tick/position pair, discarding non-monotonic ticks and overwriting the oldest when at capacity. |
| `Query(uint)` | Returns the interpolated position for the given tick, clamping to the nearest endpoint when out of range. Binary-searches for the bracket — O(log n) per query. |
