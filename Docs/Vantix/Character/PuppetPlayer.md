# PuppetPlayer

`Vantix.Character.PuppetPlayer`

Remote-player driver. Interpolates GlobalPosition/Rotation from a ring buffer of snapshots at render time = serverTickEstimate - interpDelay (~6 ticks); brackets the surrounding pair and lerps, extrapolating briefly past the newest snapshot on packet drop. Animation state is written into the MovementController to drive the third-person body.

## Properties

| Name | Summary |
|------|---------|
| `SpectateMode` | Spectator mode for this puppet. The setter activates the matching camera (e.g. after death). |

## Fields

| Name | Summary |
|------|---------|
| `ExtrapolationMaxTicks` | Cap on extrapolation past the newest snapshot before the puppet freezes. 16 ticks ≈ 125ms at 128Hz. |
| `JitterToBufferMultiplier` | Multiplier on the MAD jitter signal (`JitterDownMs`) for the safety buffer; 2.5 × MAD ≈ 2σ (~95% coverage). |
| `MethodName.ApplyAimModifierLod` | Cached name for the 'ApplyAimModifierLod' method. |
| `MethodName.ApplySpectateMode` | Cached name for the 'ApplySpectateMode' method. |
| `MethodName.ApplyTeamGlow` | Cached name for the 'ApplyTeamGlow' method. |
| `MethodName.BuildGlowVisualsDeferred` | Cached name for the 'BuildGlowVisualsDeferred' method. |
| `MethodName.ComputeEffectiveInterpDelay` | Cached name for the 'ComputeEffectiveInterpDelay' method. |
| `MethodName.EnsureSpectateTpsCacheReady` | Cached name for the 'EnsureSpectateTpsCacheReady' method. |
| `MethodName.FindGlowSilhouette` | Cached name for the 'FindGlowSilhouette' method. |
| `MethodName.GetVisual` | Cached name for the 'GetVisual' method. |
| `MethodName.LodTierUpdateHz` | Cached name for the 'LodTierUpdateHz' method. |
| `MethodName.PlayDropMag` | Cached name for the 'PlayDropMag' method. |
| `MethodName.PlayFootstep` | Cached name for the 'PlayFootstep' method. |
| `MethodName.PlayJump` | Cached name for the 'PlayJump' method. |
| `MethodName.PlayLand` | Cached name for the 'PlayLand' method. |
| `MethodName.PlayShot` | Cached name for the 'PlayShot' method. |
| `MethodName.PlayerColor` | Cached name for the 'PlayerColor' method. |
| `MethodName.ResolveLodTierCached` | Cached name for the 'ResolveLodTierCached' method. |
| `MethodName.SilhouetteInFrustumManual` | Cached name for the 'SilhouetteInFrustumManual' method. |
| `MethodName.SpawnGrenade` | Cached name for the 'SpawnGrenade' method. |
| `MethodName.UpdateServerPosDebugCapsule` | Cached name for the 'UpdateServerPosDebugCapsule' method. |
| `MethodName.UpdateSpectateTpsCollision` | Cached name for the 'UpdateSpectateTpsCollision' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PlayerName` | Display name taken from the PlayerJoined event. Used only for logging. |
| `PlayerPalette` | 5 base colours (blue/green/red/purple/yellow), indexed by TeamSlot. Deterministic, no net sync. |
| `PropertyName.PlayerName` | Cached name for the 'PlayerName' field. |
| `PropertyName.SpectateMode` | Cached name for the 'SpectateMode' property. |
| `PropertyName._aimModifierLookupDone` | Cached name for the '_aimModifierLookupDone' field. |
| `PropertyName._bodyYawInitialized` | Cached name for the '_bodyYawInitialized' field. |
| `PropertyName._cachedAimModifier` | Cached name for the '_cachedAimModifier' field. |
| `PropertyName._cachedTeamColor` | Cached name for the '_cachedTeamColor' field. |
| `PropertyName._cachedTeamColorValid` | Cached name for the '_cachedTeamColorValid' field. |
| `PropertyName._debugCapsuleMesh` | Cached name for the '_debugCapsuleMesh' field. |
| `PropertyName._glowCurrentlyOn` | Cached name for the '_glowCurrentlyOn' field. |
| `PropertyName._glowNameLabel` | Cached name for the '_glowNameLabel' field. |
| `PropertyName._glowSilhouette` | Cached name for the '_glowSilhouette' field. |
| `PropertyName._hasInitialAppliedTeamColor` | Cached name for the '_hasInitialAppliedTeamColor' field. |
| `PropertyName._lastAimModifierActive` | Cached name for the '_lastAimModifierActive' field. |
| `PropertyName._lastAimModifierActiveValid` | Cached name for the '_lastAimModifierActiveValid' field. |
| `PropertyName._lastAppliedLocalTeam` | Cached name for the '_lastAppliedLocalTeam' field. |
| `PropertyName._lastAppliedTeam` | Cached name for the '_lastAppliedTeam' field. |
| `PropertyName._lastBracketedAnglesValid` | Cached name for the '_lastBracketedAnglesValid' field. |
| `PropertyName._lastBracketedPitch` | Cached name for the '_lastBracketedPitch' field. |
| `PropertyName._lastBracketedYaw` | Cached name for the '_lastBracketedYaw' field. |
| `PropertyName._lastPushedTeamColor` | Cached name for the '_lastPushedTeamColor' field. |
| `PropertyName._lastPushedTeamColorValid` | Cached name for the '_lastPushedTeamColorValid' field. |
| `PropertyName._lastShownHp` | Cached name for the '_lastShownHp' field. |
| `PropertyName._lastShownTeamSlot` | Cached name for the '_lastShownTeamSlot' field. |
| `PropertyName._lastSnapshotPushUsec` | Cached name for the '_lastSnapshotPushUsec' field. |
| `PropertyName._lodAnimAccum` | Cached name for the '_lodAnimAccum' field. |
| `PropertyName._lodTier` | Cached name for the '_lodTier' field. |
| `PropertyName._puppetBodyYaw` | Cached name for the '_puppetBodyYaw' field. |
| `PropertyName._renderClockInitialized` | Cached name for the '_renderClockInitialized' field. |
| `PropertyName._renderClockTickF` | Cached name for the '_renderClockTickF' field. |
| `PropertyName._serverPosDebugCapsule` | Cached name for the '_serverPosDebugCapsule' field. |
| `PropertyName._smoothedInterpDelay` | Cached name for the '_smoothedInterpDelay' field. |
| `PropertyName._spectateMode` | Cached name for the '_spectateMode' field. |
| `PropertyName._spectateRayQuery` | Cached name for the '_spectateRayQuery' field. |
| `PropertyName._spectateRayResult` | Cached name for the '_spectateRayResult' field. |
| `PropertyName._spectateTpsCam` | Cached name for the '_spectateTpsCam' field. |
| `PropertyName._spectateTpsRestCached` | Cached name for the '_spectateTpsRestCached' field. |
| `PropertyName._spectateTpsRestLocal` | Cached name for the '_spectateTpsRestLocal' field. |
| `PropertyName._visualHiddenSinceUsec` | Cached name for the '_visualHiddenSinceUsec' field. |
| `RenderClockNudgeRateTicksPerSec` | Ticks/sec the clock may nudge when bleeding off sub-resnap drift (~4 ms/s). |
| `RenderClockResnapTicks` | Hard re-anchor threshold for the render-clock; below it drift is bled in invisibly. 4 ticks ≈ 31ms. |
| `ResumeGapUsec` | Snapshot-gap threshold (µs) past which the puppet is treated as re-entering the PVS (300ms). |
| `SmoothingRate` | Rate (1/sec) for the exponential smoothing of `_smoothedInterpDelay` (~1s constant, frame-rate independent). |
| `_glowNameLabel` | World-space Label3D ("Name\nHP") parented to the puppet body (not the head bone, whose subtree is scaled 0.01). Rendered only on the glow-text layer; the composite shader stamps it back over the scene. |
| `_glowSilhouette` | Pre-baked silhouette mesh under the puppet's Skeleton3D (GlowSilhouetteMeshBaker tool). All puppets share one baked mesh + material chain; per-puppet team colour is pushed via SetInstanceShaderParameter("team_color", …). Glow on/off is a `Visible` toggle. |
| `_hasInitialAppliedTeamColor` | False until the first UpdateNameAndGlow runs; forces the TeamSlot block on the first call so an initial TeamSlot=255 sentinel isn't skipped by the delta check (else material stays white for seconds). |
| `_lastBracketedYaw` | Last yaw/pitch from a bracketed pair, held during extrapolation instead of snapping to A.Yaw (which would head-twitch on packet drop). |
| `_lastPushedTeamColor` | Last team colour pushed to the silhouette shader param; skips the SetInstanceShaderParameter call when unchanged (team_color flips only when TeamSlot does). |
| `_lastSnapshotPushUsec` | Wallclock when the most recent snapshot was pushed; drives the free-running renderTick. |
| `_renderClockTickF` | Free-running virtual server-tick this client renders at. Advances by delta×tickRate and is nudged toward the raw target rather than re-anchored each snapshot (avoids per-snapshot micro-snaps); hard-resets only past `RenderClockResnapTicks`. |
| `_smoothedInterpDelay` | Adaptive interp delay, exponentially smoothed across frames. Updated each `_Process` from `JitterDownMs` when `InterpLockTicks` is 0. |
| `_visualHiddenSinceUsec` | Wallclock when Visible was set false in _Ready. After `VisualRevealFailsafeUsec` the body is force-revealed, so bots that never send WorldInitComplete aren't permanently invisible. |

## Methods

| Name | Summary |
|------|---------|
| `ApplyAimModifierLod()` | Disables the spine-aim modifier in the Off tier (skipping its per-frame quaternion math). Lookup is cached; the Active setter is delta-gated. |
| `ApplySpectateMode()` | Activates the camera matching the current `SpectateMode`. |
| `ApplyTeamGlow(bool)` | Toggles the silhouette + Label3D Visible (the entire on/off mechanism, since the material chain is baked). Gated by Settings.TeamGlow; spectator visibility is decided in `UpdateNameAndGlow`. |
| `BuildGlowVisualsDeferred()` | Finds the pre-baked silhouette mesh, resets the delta trackers, and attaches the Label3D nameplate. Team colour and visibility are applied in `UpdateNameAndGlow`/`ApplyTeamGlow`. |
| `ComputeEffectiveInterpDelay(float, float)` | Resolves the render-delay for this frame. Locked (`cl_interp_lock`) returns the configured value; adaptive targets `4 + 2.5 × jitterTicks`, clamped to the ConVar range and exponentially smoothed (~1s constant). Mirrored into `InterpDelayMs`. |
| `EnsureSpectateTpsCacheReady()` | Caches the spectator third-person camera reference and its rest position on first activation. |
| `FindGlowSilhouette(Node)` | Depth-first search for the baked GlowSilhouetteMeshBaker (path not hard-coded since it may be nested anywhere). Returns the first match. |
| `GetVisual()` | Self-reference for call-sites that ask the puppet for its visual; the puppet is the NetworkPlayer. |
| `LodTierUpdateHz(Vantix.Character.PuppetPlayer.PuppetLodTier)` | Maps an LOD tier to its animation update rate (Hz); Off returns 0 to skip the advance entirely. |
| `PlayDropMag()` | Reliable-event handler for a remote reload: drops the magazine from the TPS weapon. |
| `PlayFootstep(Vector3, string, byte, bool, bool)` | Reliable-event handler for a remote footstep: plays the spatial audio sample. |
| `PlayJump()` | Reliable-event handler for a remote jump: triggers the viewmodel, one-shot animation and audio. |
| `PlayLand(float)` | Reliable-event handler for a remote landing: triggers the heavy or light land one-shot. |
| `PlayShot(byte, Vector3, Vector3, bool, bool, Vector3, Vector3, string)` | Reliable-event handler: spawns tracers and impact decals, plays the shoot audio, and triggers the third-person fire one-shot for a remote shot. |
| `PlayerColor(byte)` | Deterministic per-player colour from NetId (same on every client). Used for glow, label and scoreboard square. |
| `ProbeGround()` | Down-raycast under the puppet so jump and land sounds probe the same material and tunnel-reverb state as the local player. |
| `PushSnapshot(uint, Vantix.Net.SnapshotPlayer)` | Pushes a new snapshot into the interp buffer and records its arrival wallclock. Out-of-order packets are dropped. If the gap since the last snapshot exceeds `ResumeGapUsec`, the buffer is wiped and the position snapped (FoW resume), avoiding a "slide through walls" lerp on PVS re-entry. |
| `ResetOnVisibilityResume(Vantix.Net.SnapshotPlayer)` | Called on a snapshot after a long visibility gap: clears the buffer so the next tick brackets only fresh data, snaps the position, and reseeds the smoothed interp delay. |
| `ResolveLodTierCached(Camera3D, Vector3, Vector3, float)` | Picks the LOD tier from distance to the camera plus a forgiving frustum check. Takes the camera + basis from the caller to avoid a second GetViewport().GetCamera3D() per frame. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SilhouetteInFrustumManual(Camera3D, Vector3, Vector3, float)` | Manual frustum test against the cached camera basis: a single cone-angle test of the puppet's mid-point vs camForward with a radius/distance-scaled pad (replaces a 7-point IsPositionInFrustum sweep). |
| `SpawnGrenade(byte, uint, byte, Vector3, Vector3)` | Reliable-event handler for a remote grenade throw: spawns a puppet-mode SmokeGrenade (follows owner ProjectileState snapshots). The puppet body Rid is excluded from the grenade's raycast. |
| `UpdateNameAndGlow(Vantix.Net.SnapshotPlayer)` | Pushes HP/Team/TeamSlot into the nameplate and body-ID material. Glow + label only for teammates (or all when spectating) and not Deathmatch. Fields are delta-checked to avoid per-frame churn. |
| `UpdateServerPosDebugCapsule()` | Positions the red debug capsule at the latest server position (no interp/lerp), showing when the interp puppet lags the server body. Off by default (cl_debug_capsule 1). |
| `UpdateSpectateTpsCollision(float)` | Spring-arm step for the spectator third-person camera: raycasts pivot to rest, pulls the camera in on a hit, and smoothly lerps the result. |
| `_Process(double)` | Per-frame interpolation: computes the free-running renderTick, brackets the surrounding snapshots, blends them and pushes the result into the movement controller and animation tree. Past the newest snapshot, position extrapolates from its velocity (capped at `ExtrapolationMaxTicks`); view angles are held, not extrapolated. |
| `_Ready()` | Instantiates the visual child, configures animation throttling, and wires the puppet flags. |
