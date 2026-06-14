# PredictionBuffer

`Vantix.Client.PredictionBuffer`

Ring buffer of predicted states per tick, used to reconcile against server snapshots.

## Methods

| Name | Summary |
|------|---------|
| `Clear()` | Clears all entries from the buffer. |
| `FindFirstIndexAfter(uint)` | Returns the logical index of the first entry whose tick is > `afterTick`, or `Count` if none. Used by reconcile replay loops. |
| `GetAt(int)` | Random access by logical index — caller must check bounds via `Count`. |
| `LogicalToArray(int)` | Maps a logical index (0 = oldest, Count-1 = newest) to the underlying array index accounting for ring wraparound. |
| `Push(uint, Vantix.Character.MovementInput, Vantix.Character.MovementSnapshot, Vector3, Vector3)` | Appends a new tick entry. Discards non-monotonic ticks; overwrites the oldest entry once `Capacity` is reached. |
| `TryFindIndex(uint, int)` | Binary-search by tick (entries are monotonic). On hit returns logical index; on miss returns lower-bound logical index (= first index whose tick is > `tick`). |
| `TryGet(uint, Vantix.Client.PredictionBuffer.Entry)` | Looks up the entry for the exact tick; returns false if it has rolled out of the buffer. |
| `UpdateEntryState(uint, Vantix.Character.MovementSnapshot, Vector3, Vector3)` | Updates the cached state of an existing entry after a replay step so later reconciliations see the new value. |
