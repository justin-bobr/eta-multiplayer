# ETA

> Server-authoritative tactical FPS on Godot 4.6 + C#.

[![Status: In Development](https://img.shields.io/badge/status-in%20development-orange)]()
[![Godot 4.6](https://img.shields.io/badge/Godot-4.6-blue)](https://godotengine.org/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)

<p align="center">
  <img src="Screenshots/screen1.png" width="49%"/>
  <img src="Screenshots/screen2.png" width="49%"/>
</p>

Round-based 5v5 competitive shooter. Pre-alpha (`0.0.1`) — networking, hit detection, weapons, movement, smoke voxels, settings are end-to-end functional. Round economy / buy menu / additional content still in progress.

**Code is open source; assets are not.** See [License](#license).

---

## Tech Stack

| | |
|---|---|
| Engine | Godot 4.6 (Forward+, D3D12) |
| Language | C# / .NET 8 |
| Physics | Jolt @ 128 Hz |
| Network | LiteNetLib 2.1.4 (UDP) |
| Format | CSharpier 0.30 |

---

## Features

### Networking

- **128 Hz physics, 64 Hz snapshots, CS2-style sub-tick input** — every input edge is timestamped (~30 µs granularity) and the movement step decomposes each tick at event positions. Counter-strafe / jump / fire timing land between tick boundaries.
- **Client prediction + reconciliation** — 256-tick rolling buffer, velocity-scaled drift threshold, visual error bleed for soft corrections, hard snap > 5 m.
- **Server lag compensation** — 128-tick rewind buffer, fractional rewind tick (`currentTick − RTT/2 − InterpDelay + FireSubTick/256`) interpolates between stored snapshots.
- **Wire format**: yaw/pitch as `ushort`, pos/vel as cm-scale `int16`, 1-byte material IDs.
- **Anti-cheat**: WishDir magnitude clamp, pitch clamp, monotonic sub-tick event check, server-derived `OnFloor`/`TouchingWall` (never trusted from client).
- **Fog of War (voxel PVS)** — server-side line-of-sight culling: enemies behind walls are not in the snapshot. Bake once via the `VoxelPvsInstance` node (4 m cells, raycast pairwise), or fall back to runtime incremental build.
- **Reconnect grace pool** — disconnected peers frozen for `--reconnect-grace` (default 600 s); same identity token resumes same NetId / pos / score.

### Movement

- **Source-engine accel model** (friction first, then add-in-wishdir). Walk 4.0 / Sprint 5.0 / Shift 1.9 / Crouch 1.9 m/s. Air accel 100, air-max-wishspeed 0.6.
- **Counter-strafe** — opposite-direction input zeros velocity in ~50–80 ms.
- **Jump tech**: coyote 100 ms, jump buffer 80 ms, crouch-jump buffer 150 ms with forward bonus, crouch-cancel-jump (+1.85 m/s vertical), apex-hang gravity modifier.
- **Wall tech**: wall jump (5.5 m/s threshold, 65% momentum retain), wall cling (1.25 s, one charge per airtime).
- **Slide** — crouch during sprint at ≥5.5 m/s → 9 m/s boost, friction 6, max 1 s, 200 ms accuracy window after stop.
- **Stamina** (max 100, 18.5/s drain, 20/s regen). **Breath hold** (3 s hold / 1 s recover / 0.5 s cooldown).
- **Auto-mantle** onto 1.0–1.75 m ledges. **Step-up** for short bumps.

### Combat

- Server-authoritative hitscan with rewound hitboxes. Multi-RID exclude (shooter + own hitboxes).
- **Per-bodypart damage** (M4A1): head 140 / chest 33 / waist 36 / leg 27 / foot 20.
- **Armor**: 50 cap, body hits split 50/50 armor/HP, headshots bypass.
- **HP regen**: +1 per 80 ms, 8 s delay after damage.
- **Wall block**: shots blocked by world geometry; opt-in penetration via `wallhit` group on the StaticBody3D.
- **Spread / recoil**: deterministic per-weapon pattern (M4A1 = 30 entries), exponential bloom, movement / airborne spread tiers, AimPunch with per-weapon climb clamp. Per-tick deterministic RNG seeded by `tickIndex * 2654435761 ^ shotIndex * 40503` — all clients see identical spread.
- **ADS**: per-weapon FOV, sensitivity / movement / spread / kick multipliers, optional DoF + FOV-zoom.
- Tracers (true travelling cylinders), per-face surface detection (face → material → impact_tag), Decal3D + GPUParticles3D impact sets per material.

### Weapons

Per-weapon `.tscn` + `Weapon.cs`. `WeaponStats` carries 40+ tunables (fire rate, recoil array, bloom curve, ADS offsets, kick spring, audio clip arrays, ADS FOV).

- Muzzle marker, 3 GPUParticles (flash / smoke / sparks), eject marker for shell pool, AnimationTree.
- **Layered weapon audio**: Body / Mech / Tail / Distant. Distance crossover (28 m for M4A1) swaps to distant variant. 3D positional for remotes; occlusion via raycast → low-pass + duck. Environment reverb buses (outdoor / indoor / tunnel via ceiling raycast + `tunnel` floor group).

### Grenades & Smoke Voxel System

Voxel smoke field: 0.6 m cells, ~23×9×23 grid. Per-tick advection (wind + buoyancy), diffusion 0.8, dissipation 0.018, baked to 3D R8 texture at 30 Hz. Custom `FogVolume` shader with FBM noise. Burn 24 s.

- **Flood-fill + raycast** wall connectivity at smoke spawn → smoke can't seep through geometry.
- **Shot disturbance**: bullets clear a 1 m channel for 0.3 s along the shot path.
- **LOS integration**: segment density `1 − exp(−od·DensityMul) > 0.65` blocks vision.
- **Cloud-shadow integration**: shader fed up to 40 active smoke AABBs / frame to mask shadows inside smoke.
- Deterministic projectile (shared `GrenadeTrajectory.Advance` for live + aim preview). Throw model with min/max speed, up-bias, velocity inheritance, charge curve.

### Operator Abilities *(design locked, implementation in progress)*

Smokes and flashes are standard kit for everyone. Each match each operator picks **one ability** from a shared 11-entry pool — **unique per match** (no duplicates across both teams) so abilities are a draft decision, not a load-out. One charge per round.

| # | Ability | Effect |
|---|---|---|
| 1 | Sprint Surge | +25% sprint speed for 3 s |
| 2 | Recon Wire | Invisible tripwire, tags crosser for the team 4 s |
| 3 | Pulse Scan | 6 m radar ping, reveals enemies 1.5 s |
| 4 | Stim Patch | +30 HP regen over 3 s, 0.6 s no-fire lockout |
| 5 | Spotter Drone | Hovering drone, spots first enemy in 12 m cone for 6 s |
| 6 | Ammo Cache | Drop a refill kit, +1 magazine for any teammate who steps on it |
| 7 | Sound Decoy | Looping footstep audio for 4 s, audible 12 m |
| 8 | Heartbeat Sensor | 5 s directional scan of enemies within 5 m |
| 9 | Brace | −25% incoming bullet damage for 1.5 s (can't fire) |
| 10 | Steady Aim | Next shot: 0 spread, 0 recoil |
| 11 | Long Breath | Next breath-hold lasts 2× as long |

No damage abilities. No ult-tier game-winners. Effects competing with bullets share the hitscan rewind pipeline.

### Audio

- **Per-material footsteps** — 33 surfaces. Material via Godot group on the floor collider. Scanned at runtime from `res://audio/footsteps/<material>/`. Per-stance loudness (shift 0.12 / walk 0.62 / sprint 1.0), distance-based cadence, 3D positional for remotes (11–46 m hear range), occlusion (720 Hz lowpass), tunnel reverb.
- **Weapon audio** layered Body / Mech / Tail / Distant with phase-coherent pitch jitter and environment-adaptive reverb.

### Graphics

- **Post-process compositor** (single compute pass, `post_process.glsl`): heat haze, CA, sharpen, vignette, film grain, motion blur — all individually toggleable.
- **AA**: FXAA / SMAA / TAA. FSR 1 / 2 upscale when RenderScale < 1.
- **Shadows**: Low (1024) / Medium (2048) / High (4096) atlas + soft filter tiers.
- **Atmosphere**: volumetric fog (always on, smoke voxels depend on it), cloud shadows (smoke-masked), god rays, lens flare, dust motes.
- **Viewmodel**: 3-source light sampler (sun + sky + albedo raycasts) re-lights FPS arms to match world. Layer 1 = world, Layer 2 = viewmodel.
- TPS: world-space spine twist + per-foot ground-snap IK.

### HUD

Compass with 360° strip + bombsite A/B markers. Vitals (HP / armor / stamina), money, loadout, scoreboard (Tab, 4 Hz), HitFeed, killfeed, low-HP FX, dynamic crosshair (kick-on-fire, ADS hide). Runtime-tunable margins.

### Bot AI

`--bots N` flag. Bots wander between waypoints with reactive raycast avoidance (multi-probe + LOS-filtered targets), drop-in replacement when a real player joins. Combat AI: LOS detection (triple-ray: head/chest/waist), smooth aim turn, reaction delay scaling with `sv_bot_difficulty` 0–3 (500ms/feet → 80ms/head), tactical reload.

### Developer Tools

- **In-game console** (`F10` / `^`) — tab-completion, history, ConVar registry. `sv_*` server-authoritative, `cl_*` local.
- **ConVars**: ~80 server-side + ~95 client-side runtime-tunable fields, per-weapon scoped overrides (recoil patterns etc.).
- Debug commands: `sv_debug_hitboxes`, `sv_debug_capsule`, `sv_debug_aimray`.
- **NetGraph**: ping / loss / pps / tick / choke / reconcile-drift, jitter line graphs.
- **DebugOverlay**: smoothed proc/phys ms, FPS with rolling min, RAM/VRAM/draws, player state.

---

## Project Layout

```
codebase/             C# source
  authority/character/  Local / Puppet / Server / Bot player split
  fx/                   Post-process, smoke voxels, tracers, decals
  hud/                  Crosshair, hit-feed, scoreboard, vitals
  net/                  Client / server, prediction, rewind, packets
  settings/             Settings + in-game menu
character/            Player mesh, anims, materials
fx/                   FX assets
maps/                 dust2, warehouse
props/                Ammo, barrels, vehicles
weapons/              Weapon scenes
loading.tscn          Boot scene
world.tscn            In-game world container
```

---

## Getting Started

### Requirements
- Godot 4.6 with .NET / Mono support
- .NET 8 SDK
- Windows (D3D12 driver; Linux/macOS untested)

### Build
```bash
git clone <your-repo-url> eta
cd eta
dotnet tool restore
```
Open in Godot, let `.godot/` generate, then **Build** (hammer icon).

### Run

| Scenario | Command |
|---|---|
| Main menu (default) | `godot` |
| Dedicated server, default addr | `godot -- --server` |
| Dedicated, custom addr | `godot -- --server --host 0.0.0.0 --port 28000` |
| Listen server | `godot -- --listen` |
| Client auto-connect | `godot -- --connect 10.0.0.5:27015` |
| Dedicated + 5 bots, 64 tick | `godot -- --server --bots 5 --tickrate 64` |

### CLI Flags

| Flag | Default | |
|---|---|---|
| `--server` | — | Dedicated headless. Combine with `--host`/`--port`. |
| `--listen` | — | Listen server (server + local client). |
| `--connect HOST:PORT` | — | Client + auto-connect, skips menu. |
| `--host` | `127.0.0.1` | Bind address (`--server`/`--listen`). |
| `--port` | `27015` | Bind port. |
| `--max-players N` | `16` | Clamp `1..64`. |
| `--bots N` | `0` | AI bots, replaced as real players join. |
| `--name "NAME"` | `Player` | Display name. |
| `--tickrate N` | `128` | Physics tick, clamp `30..256`. |
| `--gamemode dm \| competitive` | `competitive` | |
| `--reconnect-grace SECONDS` | `600` | Disconnect → identity token resume window. |
| `--identity TOKEN` | — | Override identity token (testing). |

Engine vs game args separated by `--`: `godot -- --connect ...`.

### Controls

| | |
|---|---|
| Move | `WASD` |
| Sprint hold / toggle | `Shift` / `X` |
| Jump / Crouch | `Space` / `Ctrl` |
| Fire / ADS | `LMB` / `RMB` |
| Reload / Inspect | `R` / `F` |
| Hold breath | `Q` |
| Slot 1/2 | `1` / `2` |
| Use | `E` |
| Scoreboard | `Tab` |
| Menu | `Esc` |
| Console | `F10` or `^` |

---

## Code Style

CSharpier-enforced. VSCode is configured to format on save.

```bash
dotnet csharpier codebase           # format
dotnet csharpier --check codebase   # CI / pre-commit
```

**Comment policy**: every method has an English `/// <summary>` XML doc. No inline `//` comments in method bodies — documentation lives in XML doc comments above signatures.

---

## Contributing

1. Fork → branch → PR against `main`.
2. `dotnet csharpier --check codebase` before submitting.
3. Open an issue for anything large before starting.

Asset contributions require explicit licensing — see [License](#license).

---

## Roadmap

- Round-based defuse mode (economy, buy menu, win conditions)
- Operator abilities (pre-round draft, 11-ability pool)
- Additional weapons (AR, AWP, pistols, knife)
- Flash + HE grenades
- Full dust2 parity pass
- Linux / macOS builds
- Dedicated-server matchmaking
- Replay system (built on the rewind buffer)
- Main menu IP/port connect widget
- In-game skin system + creator workshop, player-to-player trading, **100% of net revenue to creator**
- In-game map editor + server-browser publish, same creator-revenue rule

---

## Credits

- **Godot Engine** — Juan Linietsky, Ariel Manzur & contributors
- **Jolt Physics** — Jorrit Rouwé
- **LiteNetLib** — Ruslan Pyrch (RevenantX)
- **CSharpier** — Brian Surowiec
- **Programming** — Stefan Kalysta
- Art / models / animations / audio / FX / shaders — see per-asset attribution; placeholders pending in some cases.

If you find your work included without proper attribution, open an issue.

---

## License

**Two distinct kinds of content, two licenses.**

**Code** (`.cs`, `.glsl`, `.gdshader`, configs) is **open source**. Specific license (MIT / Apache 2.0 / etc.) **TBD** before the first tagged release. Until then, code is shared for reading and learning; open an issue if you want to reuse a meaningful chunk.

**Assets** (meshes, rigs, animations, weapon models, maps, textures, lightmaps, audio, decals, LUTs, sky / muzzle / smoke / particles, icons, branding) are **All Rights Reserved**. ETA does not own most of these — they ship under the rights set by their respective owners or are placeholders pending replacement.

You **may not** copy, redistribute, modify, sublicense, datamine, repackage, or use ETA assets in any other project — or to train ML models. Permission must come from the original rights holder, not from this project.

If your work is included without permission, open an issue and we'll remove it.
