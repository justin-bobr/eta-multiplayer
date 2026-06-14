# NetMode

Mode the game instance runs in — set from the command line (see `Parse`).

## Fields

| Name | Summary |
|------|---------|
| `Client` | Client only. Boots into the main menu unless `AutoConnect` (via `--connect HOST:PORT`) connects directly to `Host`:`Port`. |
| `Listen` | Server plus local client in the same process — dev shortcut for editor play. |
| `Server` | Dedicated headless server. |
