# SnapshotPlayer

`Vantix.Net.SnapshotPlayer`

One player's state within a server snapshot (position, view, blends, hp).

## Fields

| Name | Summary |
|------|---------|
| `Armor` | Kevlar 0..50. Consumed without regen; headshots bypass it. |
| `Team` | Cast of `Team`; drives puppet team-glow + scoreboard colour. None=0/CT=1/T=2/Deathmatch=3. |
| `TeamSlot` | Persistent per-team index (0..15), assigned at register time. Drives the per-player colour (palette[teamSlot]). |
