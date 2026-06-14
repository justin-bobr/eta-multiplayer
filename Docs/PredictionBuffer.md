# PredictionBuffer

Ring buffer of local prediction states, one per `FixedTick` (post-step `MovementSnapshot` + position). On a server snapshot with `ackedInputTick = N`, the stored position at tick N is compared against the server's to compute mispredict drift. Capacity 512 ticks (≈4 s @ 128 Hz) covers high-ping spikes without the reconcile entry rolling out. Overwrites oldest in O(1); lookups binary-search since ticks are monotonic.

## Methods

| Name | Summary |
|------|---------|
| `Clear()` | Clears all entries from the buffer. |
| `FindFirstIndexAfter(uint)` | Returns the logical index of the first entry whose tick is > `afterTick`, or `Count` if none. Used by reconcile replay loops. |
| `GetAt(int)` | Random access by logical index — caller must check bounds via `Count`. |
| `LogicalToArray(int)` | Maps a logical index (0 = oldest, Count-1 = newest) to the underlying array index accounting for ring wraparound. |
| `Push(uint, MovementInput, MovementSnapshot, Vector3, Vector3)` | Appends a new tick entry. Discards non-monotonic ticks; overwrites the oldest entry once `Capacity` is reached. |
| `TryFindIndex(uint, int)` | Binary-search by tick (entries are monotonic). On hit returns logical index; on miss returns lower-bound logical index (= first index whose tick is > `tick`). |
| `TryGet(uint, PredictionBuffer.Entry)` | Looks up the entry for the exact tick; returns false if it has rolled out of the buffer. |
| `UpdateEntryState(uint, MovementSnapshot, Vector3, Vector3)` | Updates the cached state of an existing entry after a replay step so later reconciliations see the new value. |
