# NetStats

`Vantix.Net.NetStats`

Global netcode stats. Written by `NetClient`/`NetServer`, read by `DebugOverlay`; static to avoid per-node wiring. Server mode populates only server fields, Client only client fields, Listen both.

## Fields

| Name | Summary |
|------|---------|
| `JitterDownMs` | Snapshot inter-arrival variance in ms. |
| `JitterUpMs` | Input send-interval variance in ms (client → server). |
| `LastReconcileDriftHorizM` | Horizontal (XZ) component of the last reconcile drift, metres. Aim-relevant — should stay tight. |
| `LastReconcileDriftM` | Last reconcile drift in metres (server pos vs. client prediction at the ack'd tick). 0 = no correction since spawn. Drives severity colour coding in the debug overlays. |
| `LastReconcileDriftVertM` | Vertical (Y) component of the last reconcile drift, metres. Mostly cosmetic stair-step mismatch; ~20cm tolerable. |
| `LastReconcileTimeSec` | Engine time (sec) of the last reconcile — for the "recent" highlight. |
| `ReconcilesPerSec` | Rolling reconcile count over the last ~1 s. 0 = stable. |

## Methods

| Name | Summary |
|------|---------|
| `Reset(Vantix.Net.NetMode)` | Called once on mode change — clears stale values. |
