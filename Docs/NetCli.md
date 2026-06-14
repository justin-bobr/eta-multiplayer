# NetCli

Parses command line arguments (Godot separates user args after "--"). Examples: godot Client mode → boots into main menu godot -- --server Dedicated on 127.0.0.1:27015 godot -- --server --host 0.0.0.0 --port 28000 Dedicated on a custom address godot -- --listen Listen server + local client (skip menu) godot -- --connect 192.168.1.10 Client, auto-connect (skip menu), default port godot -- --connect 10.0.0.5:28000 Client, auto-connect on a custom port Additional flags: --max-players N (default 16), --bots N (default 0 = no bots), --name "...", --tickrate 128, --reconnect-grace 600, --gamemode dm|competitive, --identity TOKEN.

## Fields

| Name | Summary |
|------|---------|
| `AutoConnect` | True when `--connect HOST:PORT` was given. The client auto-connects and the main menu is skipped. |
| `IdentityOverride` | Override for Settings.NetIdentityToken — used only for multi-client testing on one PC. Empty = use persisted token from user://settings.cfg. |
| `MaxBots` | Bots the server auto-spawns (capped to free spawn markers), via `--bots N`. Default 0. Replaced by real players (lowest-NetId bot despawns). |
| `Mode` | Defaults to `Client` so a flagless launch (incl. editor F5) lands in the main menu. |

## Methods

| Name | Summary |
|------|---------|
| `Parse()` | Parses command line arguments into a populated `NetCli` instance. |
| `ParseHostPort(string, NetCli)` | Parses a "host" or "host:port" string into the target `NetCli`. |
| `ToString()` | Diagnostic string representation listing all parsed CLI fields. |
