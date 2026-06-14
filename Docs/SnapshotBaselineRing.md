# SnapshotBaselineRing

Ring buffer of snapshot baselines for delta compression. Server: one ring per peer of the last `Capacity` snapshots sent to that peer (post-PVS); each send delta's against the entry matching `LastAckedSnapshotTick`. Client: ring of the last received + reconstructed snapshots; a packet with `baselineTick != 0` applies its delta onto the looked-up baseline. Capacity 64 (~1 s @ 64 Hz) tolerates ~500ms RTT before the baseline ages out and the next snapshot goes full (self-healing).

## Methods

| Name | Summary |
|------|---------|
| `Clear()` | Invalidates all entries. Call on session reset (reconnect, new map). |
| `Find(uint)` | Returns the entry for the given tick, or null if not in history (aged out or never sent/received). A linear scan over 64 slots is trivial. |
| `Push(uint, IReadOnlyList<SnapshotPlayer>)` | Stores the snapshot in the current ring slot. The internal SnapshotPlayer[] is only grown when the player count increases; otherwise it is overwritten in place (zero-alloc steady state). |
| `Push(uint, SnapshotPlayer[], int)` | Variant for a SnapshotPlayer[] buffer (client side already holds snapshots as an array). |
