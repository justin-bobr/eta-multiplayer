# BotCombatContext

`Vantix.Character.BotCombatContext`

Per-tick world snapshot a bot reads to choose its movement and fire decisions.

## Fields

| Name | Summary |
|------|---------|
| `NeedsReload` | True when the bot's magazine is empty and it isn't already reloading. Drives the per-tick ReloadPressed flag; the edge detector fires the reload once. |
