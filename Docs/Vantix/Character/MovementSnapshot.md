# MovementSnapshot

`Vantix.Character.MovementSnapshot`

Complete `MovementController` state for client-side prediction reconciliation. Snapshotted per tick into a ring buffer via `Snapshot` and restored via `Restore` before replay. Value type (no GC). Excludes Sv (immutable) and _fireRng (re-seeded deterministically from TickIndex+ShotIndex). Node-side state (transform, mantle) is captured separately in the netcode snapshot.
