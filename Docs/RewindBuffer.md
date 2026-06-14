# RewindBuffer

Per-agent history ring of the server-authoritative position per tick, filled every tick by `NetServer`. Server-side `Hitscan` rewinds other agents to `shooterTick - RTT/2 - interpDelay` for fair lag compensation. Capacity 128 ticks (1 s @ 128 Hz); overwrites oldest in O(1); lookups binary-search (ticks monotonic).

## Methods

| Name | Summary |
|------|---------|
| `Clear()` | Clears all recorded entries. |
| `Push(uint, Vector3)` | Appends a tick/position pair, discarding non-monotonic ticks and overwriting the oldest when at capacity. |
| `Query(uint)` | Returns the interpolated position for the given tick, clamping to the nearest endpoint when out of range. Binary-searches for the bracket — O(log n) per query. |
