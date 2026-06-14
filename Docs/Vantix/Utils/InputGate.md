# InputGate

`Vantix.Utils.InputGate`

Central input gate. `Blocked` is true when the player should receive no game input: settings menu open, window unfocused (Alt-Tab), mouse capture off, or LocalPlayer dead. Input-read sites should consult this first so keystrokes don't leak through.

## Fields

| Name | Summary |
|------|---------|
| `LocalPlayerFrozen` | True from LocalPlayer._Ready until `WorldInitComplete` is sent; freezes input reads and SendNetInput so the player can't move or send pre-spawn ticks while preloads run. |
