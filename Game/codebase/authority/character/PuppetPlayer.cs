using Godot;
using System.Collections.Generic;

/// <summary>
/// Driver node for a remote player. Holds its own <see cref="PlayerCore"/> instance
/// (puppet_player.tscn with IsPuppet=true) as the visual and interpolates its GlobalPosition / Rotation
/// from a ring buffer of received snapshots.
///
/// Render time = serverTickEstimate - interpDelay (default ~6 ticks, ~47 ms at 128 Hz). Every frame the
/// surrounding snapshot pair is located and lerped linearly. When the render time advances past the
/// newest snapshot (packet drop or lag spike) the buffer briefly extrapolates and then freezes.
///
/// Animation state (Velocity, AdsBlend, CrouchBlend, AimPunch, ShotIndex, stateFlags) is written into
/// the MovementController of the visual child. LocalAnimation reads it back and drives the third-person
/// body animation accordingly.
/// </summary>
public enum SpectateMode
{
	/// <summary>Default — no camera active; the puppet is simply rendered in the world.</summary>
	None,
	/// <summary>Follow camera — third-person camera enabled.</summary>
	Tps,
	/// <summary>First-person — sees through the puppet's eyes without a viewmodel.</summary>
	Fps,
}

/// <summary>Snapshot-driven visual for a remote player including spectator camera support.</summary>
public partial class PuppetPlayer : Node3D
{
	/// <summary>Net id of the remote player. Set before AddChild.</summary>
	public byte NetId;
	/// <summary>Display name taken from the PlayerJoined event. Used only for logging.</summary>
	public string PlayerName = "";

	/// <summary>Spectator mode for this puppet. Default <see cref="SpectateMode.None"/>. Set to Tps/Fps when
	/// the local player should spectate through this puppet (e.g. after death). The setter activates the
	/// corresponding camera and deactivates the other.</summary>
	public SpectateMode SpectateMode
	{
		get => _spectateMode;
		set { _spectateMode = value; ApplySpectateMode(); }
	}
	private SpectateMode _spectateMode = SpectateMode.None;

	private const int Capacity = 32;

	/// <summary>Hard cap on how far past the newest known snapshot we keep extrapolating before the
	/// puppet freezes at the last extrapolated position. 16 ticks ≈ 125ms at 128Hz — conservative
	/// (Source defaults to ~250ms). Past this point further drift would hurt more than help.</summary>
	private const int ExtrapolationMaxTicks = 16;

	private struct Entry { public uint Tick; public SnapshotPlayer Snap; }
	private readonly List<Entry> _buf = new();
	/// <summary>Wallclock (Time.GetTicksUsec) when the most recent snapshot was pushed. Drives the
	/// free-running renderTick so interpolation can advance and extrapolate between snapshots when
	/// the server's snapshot stream is delayed or partially dropped.</summary>
	private ulong _lastSnapshotPushUsec;
	/// <summary>Adaptive interp delay, exponentially smoothed across frames to avoid the puppet visibly
	/// snapping between delay values when jitter fluctuates. Updated each <see cref="_Process"/> tick
	/// from <see cref="NetStats.JitterDownMs"/> when <see cref="ClConVars.InterpLockTicks"/> is 0.</summary>
	private float _smoothedInterpDelay = 6f;
	private PlayerCore _visual;
	/// <summary>Read-only Zugriff aufs Visual-PlayerCore (= das instanziierte puppet_player.tscn).
	/// Wird vom HudServerHitboxesDebug-Renderer gebraucht um die Hitbox-Shape-Specs des Puppets zu lesen.</summary>
	public PlayerCore GetVisualPlayerCore() => _visual;

	private float _puppetBodyYaw;
	private bool _bodyYawInitialized;
	private const float MaxTwistRad = 1.5708f;
	private const float BodyYawRateMoving = 12f;
	private const float BodyYawRateStanding = 6f;

	private static PackedScene _characterScene;

	// === LOD-System ===
	// Animation update frequency falls off with distance and is forced to ~zero when
	// the puppet is outside the local camera frustum. Tier-thresholds tuned for a
	// dust-style 64×64m map: most enemies are <40m, A/B-sites cross-map ~80m. Each
	// tier's UpdateHz drives both TpsAnimTree.Advance() and UpdateTpsAnimations().
	// AimModifier (SkeletonModifier3D) is disabled in tier Off because no one is
	// looking at the puppet's spine twist when they can't see it.
	private enum PuppetLodTier { Near, Mid, Far, Off }
	private const float LodNearMaxDist = 15f;
	private const float LodMidMaxDist = 40f;
	private const float LodFarMaxDist = 80f;
	private const float LodFrustumPadCos = 0.15f;
	private float _lodAnimAccum;
	private PuppetLodTier _lodTier = PuppetLodTier.Near;
	private TpsAimModifier _cachedAimModifier;
	private bool _aimModifierLookupDone;

	private MeshInstance3D _serverPosDebugCapsule;
	private CapsuleMesh _debugCapsuleMesh;

	private byte _lastShownHp = 255;
	private byte _lastShownTeamSlot = 255;
	private byte _lastAppliedTeam = 255;
	private byte _lastAppliedLocalTeam = 255;
	private Color _cachedTeamColor = new(1f, 1f, 1f, 1f);
	private bool _cachedTeamColorValid = false;
	/// <summary>Last <see cref="_cachedTeamColor"/> value actually pushed to the silhouette's
	/// per-instance shader parameter. Used to skip the SetInstanceShaderParameter call when the
	/// colour has not changed — team_color only flips when TeamSlot does (≈ once per match per
	/// puppet), so pushing every frame is pure waste.</summary>
	private Color _lastPushedTeamColor;
	private bool _lastPushedTeamColorValid;
	/// <summary>False until the FIRST UpdateNameAndGlow has been observed for this puppet. Forces the
	/// TeamSlot block to run on the first call regardless of whether snap.TeamSlot equals the reset
	/// _lastShownTeamSlot (= 255). Without this guard, if the initial snapshot has TeamSlot = 255
	/// (server-side "no team assigned yet" sentinel), the delta check (255 != 255 = false) silently
	/// skipped — material stayed at the default white for 2-3 s until the server finally sent a
	/// real team-slot value. Symptom user observed: "wird grün erst nach ner Zeit wenn man davor steht".</summary>
	private bool _hasInitialAppliedTeamColor = false;
	/// <summary>Time.GetTicksUsec() when <c>_visual.Visible</c> was set to false in _Ready (waiting for WorldReady snapshot bit). Drives the failsafe in _Process — after <see cref="VisualRevealFailsafeUsec"/> microseconds the body is force-revealed regardless of flag state, so server-agent bots (which never send WorldInitComplete) or stale players from before this feature was deployed aren't permanently invisible.</summary>
	private ulong _visualHiddenSinceUsec;
	private const ulong VisualRevealFailsafeUsec = 5_000_000;

	/// <summary>The pre-baked single-mesh silhouette node living under the puppet's Skeleton3D
	/// (created in puppet_player.tscn via the GlowSilhouetteMeshBaker editor tool). All puppets share
	/// the same baked mesh + material chain (outline_hull → outline_xray → inner_fade); per-puppet
	/// team colour is pushed via SetInstanceShaderParameter("team_color", …) on this MeshInstance3D,
	/// so there's no per-puppet material instancing — the GPU's instance-shader-param slot does the
	/// per-team split for free. Glow visibility is just a <see cref="GeometryInstance3D.Visible"/>
	/// toggle.</summary>
	private GlowSilhouetteMeshBaker _glowSilhouette;
	/// <summary>World-space Label3D rendering "Name\nHP" parented directly to <see cref="_visual"/>
	/// (NOT to the head bone via BoneAttachment3D — the skeleton subtree is scaled 0.01, which would
	/// shrink the Position offset to millimetres and force counter-scale gymnastics). Layered onto
	/// the glow text viewport ONLY — the main camera does not see it; the composite shader picks it
	/// up from text_tex and stamps it back over the final scene with the team-colour modulate.</summary>
	private Label3D _glowNameLabel;

	private const uint GlowTextVisualLayer = 1u << 19; // visual layer 20 = glow_text_camera cull_mask

	/// <summary>Instantiates the visual child, configures animation throttling, and wires the puppet flags.</summary>
	public override void _Ready()
	{
		ProcessPriority = 100;

		_characterScene ??= GD.Load<PackedScene>("res://character/puppet_player.tscn");
		if (_characterScene == null)
		{
			GD.PushError("[PuppetPlayer] res://character/puppet_player.tscn could not be loaded");
			return;
		}
		_visual = _characterScene.Instantiate<PlayerCore>();
		_visual.IsPuppet = true;
		_visual.NetId = NetId;
		_visual.Name = "visual";
		_visual.ViewMode = ViewMode.Tps;
		// Hide TPS body until the remote client signals WorldInitComplete (= the WorldReady
		// snapshot flag arrives). Otherwise other players would see freshly-connecting puppets
		// pop in with a default pose before their world is even loaded. PushSnapshot flips
		// this back true the first time a snapshot with WorldReady=1 arrives, and the failsafe
		// in _Process forces it visible after 5s if the flag never arrives (e.g. server-agent
		// bots that never send WorldInitComplete, or pre-existing players from before this
		// feature was deployed).
		_visual.Visible = false;
		_visualHiddenSinceUsec = Time.GetTicksUsec();
		AddChild(_visual);
		ApplySpectateMode();

		if (_visual.TpsAnimTree != null)
			_visual.TpsAnimTree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;

		// Debug: roter transparenter Body-Capsule auf der LAST SERVER POSITION (= AuthorityPosition aus
		// letztem Snapshot, NICHT die interpolierte Puppet-Pose). Macht visuell sichtbar wo der Server
		// den Spieler hat vs wo der Client ihn rendert. Mismatch zeigt Lag-Comp/Interp-Issues.
		// Wichtig: hängt am ROOT der PuppetPlayer (parent ist World/Players), NICHT am _visual,
		// damit Bewegungen des _visual den Capsule NICHT mitziehen. Per _Process explizit auf snap.Pos gesetzt.
		// Always created (default invisible) damit Runtime-Toggle von sv_debug_capsule funktioniert auch wenn
		// Puppet vor dem Toggle gespawnt wurde. Visibility wird in UpdateServerPosDebugCapsule per ConVar gegated.
		_debugCapsuleMesh = new CapsuleMesh
		{
			Radius = _visual.CapsuleRadius,
			Height = _visual.StandHeight,
			RadialSegments = 8,
			Rings = 4,
		};
		_serverPosDebugCapsule = new MeshInstance3D
		{
			Name = "sv_pos_debug_capsule",
			Mesh = _debugCapsuleMesh,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.15f, 0.15f, 0.30f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			TopLevel = true,
			Visible = false,
		};
		AddChild(_serverPosDebugCapsule);

		// Deferred — _visual's own _Ready muss erst laufen damit eventuelle dynamische Meshes
		// (z.B. via WeaponHolder spawned weapon visuals) im Tree sind. _Ready läuft post-deferred.
		// BuildGlowVisualsDeferred also spawns the Label3D nameplate on the head bone (see method).
		CallDeferred(MethodName.BuildGlowVisualsDeferred);
	}

	/// <summary>Walks the visual subtree once, finds the pre-baked silhouette MeshInstance3D
	/// (GlowSilhouetteMeshBaker instance, parented to the puppet's Skeleton3D in puppet_player.tscn),
	/// resets the delta trackers, and attaches the Label3D nameplate. Per-puppet team colour is
	/// pushed via SetInstanceShaderParameter on the silhouette in <see cref="UpdateNameAndGlow"/>;
	/// visibility flipping happens in <see cref="ApplyTeamGlow"/>.</summary>
	private void BuildGlowVisualsDeferred()
	{
		_glowSilhouette = FindGlowSilhouette(_visual);
		if (_glowSilhouette != null) _glowSilhouette.Visible = false;

		_lastShownTeamSlot = 255;
		_lastShownHp = 255;
		_lastAppliedTeam = 255;
		_lastAppliedLocalTeam = 255;
		_hasInitialAppliedTeamColor = false;
		_cachedTeamColorValid = false;

		string baseName = string.IsNullOrEmpty(PlayerName) ? $"Player_{NetId}" : PlayerName;
		_glowNameLabel = new Label3D
		{
			Name = "glow_name_label",
			Text = baseName,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			FixedSize = false,
			FontSize = 64,
			OutlineSize = 12,
			Modulate = Colors.White,
			OutlineModulate = new Color(0f, 0f, 0f, 1f),
			PixelSize = 0.0025f,
			Position = new Vector3(0f, _visual.StandHeight + 0.25f, 0f),
			Layers = GlowTextVisualLayer,
			Visible = false,
		};
		_visual.AddChild(_glowNameLabel);

		Dbg.Print($"[PuppetPlayer netId={NetId}] glow visuals built: silhouette={(_glowSilhouette != null)} + Label3D");

		// If a snapshot is already buffered (network arrived before this deferred call ran), fire
		// UpdateNameAndGlow immediately so team_color + glow visibility are correct on the first
		// frame the silhouette renders. Without this, the glow appeared default-white for several
		// seconds until the next _Process tick crossed the delta-tracker reset (user observation:
		// "weiß für 5 sekunden bevor grün greift").
		if (_buf.Count > 0) UpdateNameAndGlow(_buf[_buf.Count - 1].Snap);
	}

	/// <summary>Depth-first search for the GlowSilhouetteMeshBaker MeshInstance3D the editor tool
	/// baked into puppet_player.tscn. We don't hard-code a path because the user may have nested
	/// it under a different node than the Skeleton3D root (e.g. a "GlowGroup" container, a costume
	/// variant subtree). Returns the first match — only one silhouette per puppet is expected.</summary>
	private static GlowSilhouetteMeshBaker FindGlowSilhouette(Node root)
	{
		if (root == null) return null;
		if (root is GlowSilhouetteMeshBaker baker) return baker;
		for (int i = 0; i < root.GetChildCount(); i++)
		{
			var found = FindGlowSilhouette(root.GetChild(i));
			if (found != null) return found;
		}
		return null;
	}

	/// <summary>5 klare Grundfarben — blau/grün/rot/lila/gelb. Reicht für ein 5-vs-5 Match (= ein
	/// Slot pro Team-Member). Index = TeamSlot, deterministisch — Server + Client computen die
	/// gleiche Color ohne Network-Sync.</summary>
	private static readonly Color[] PlayerPalette = new[]
	{
		new Color(0.30f, 0.60f, 1.00f), // blue
		new Color(0.40f, 0.95f, 0.35f), // green
		new Color(1.00f, 0.30f, 0.30f), // red
		new Color(0.70f, 0.40f, 1.00f), // purple
		new Color(1.00f, 0.95f, 0.30f), // yellow
	};

	/// <summary>Deterministic per-player color from NetId. Same NetId → same Color on every client
	/// (no network sync needed). Used for the puppet glow, label modulate and scoreboard color-square
	/// so each player has one persistent visual identity. Skips greyscale on purpose.</summary>
	public static Color PlayerColor(byte netId) => PlayerPalette[netId % PlayerPalette.Length];

	/// <summary>Pushes the latest snapshot HP/Team/TeamSlot into the Label3D nameplate and the shared
	/// body-ID material. Glow + label nur wenn puppet.Team == localSelf.Team UND nicht Deathmatch.
	/// TeamSlot/Hp/Team are delta-checked so we don't re-allocate strings or push uniform changes
	/// every frame. The Label3D billboards itself in 3D so no per-frame projection math is needed.</summary>
	private void UpdateNameAndGlow(SnapshotPlayer snap)
	{
		var localSnap = NetMain.Instance?.Client?.LastSelfSnap;
		bool localTeamKnown = localSnap.HasValue;
		byte localTeam = localTeamKnown ? localSnap.Value.Team : (byte)255;
		bool isDeathmatch = snap.Team == (byte)Team.Deathmatch;
		bool isTeammate = localTeamKnown && !isDeathmatch && snap.Team == localTeam;
		// Spectator mode: local player has no team yet (still in lobby / joined as spectator) OR
		// is dead (Hp == 0 → in death-cam / spectating remaining teammates and enemies). In both
		// cases reveal the glow on EVERY puppet, not just teammates — matches CS2 spectator UX.
		bool localIsSpectating = !localTeamKnown || localSnap.Value.Hp == 0;

		if (!_hasInitialAppliedTeamColor || _lastShownTeamSlot != snap.TeamSlot)
		{
			_hasInitialAppliedTeamColor = true;
			_lastShownTeamSlot = snap.TeamSlot;
			var color = PlayerColor(snap.TeamSlot);
			_cachedTeamColor = new Color(color.R, color.G, color.B, 1f);
			_cachedTeamColorValid = true;
			if (_glowNameLabel != null) _glowNameLabel.Modulate = new Color(color.R, color.G, color.B, 0.5f);
		}
		if (_lastShownHp != snap.Hp && _glowNameLabel != null)
		{
			_lastShownHp = snap.Hp;
			string baseName = string.IsNullOrEmpty(PlayerName) ? $"Player_{NetId}" : PlayerName;
			_glowNameLabel.Text = $"{baseName}\n{snap.Hp} HP";
		}

		bool wantGlow = (isTeammate || localIsSpectating) && Settings.TeamGlow;
		if (_lastAppliedTeam != snap.Team || _lastAppliedLocalTeam != localTeam || wantGlow != _glowCurrentlyOn)
		{
			_lastAppliedTeam = snap.Team;
			_lastAppliedLocalTeam = localTeam;
			ApplyTeamGlow(wantGlow);
		}
	}

	private bool _glowCurrentlyOn;

	/// <summary>Toggles the pre-baked silhouette MeshInstance3D + Label3D nameplate. The silhouette's
	/// material chain (outline_hull → outline_distance → N fade-tail shells) is already attached at
	/// scene-bake time; flipping Visible is the entire on/off mechanism. Settings.TeamGlow gates
	/// the whole thing so the user can A/B compare from the Settings menu without recompile.
	/// Spectator-mode visibility (alive teammates only vs. all-puppets-when-dead) is decided in
	/// <see cref="UpdateNameAndGlow"/> via the localIsSpectating clause.</summary>
	private void ApplyTeamGlow(bool enabled)
	{
		_glowCurrentlyOn = enabled;
		if (_glowSilhouette != null && GodotObject.IsInstanceValid(_glowSilhouette))
			_glowSilhouette.Visible = enabled;
		if (_glowNameLabel != null) _glowNameLabel.Visible = enabled;
		Dbg.Print($"[PuppetPlayer netId={NetId}] team glow {(enabled ? "ON" : "OFF")} — silhouette toggled (puppetTeam={_lastAppliedTeam} localTeam={_lastAppliedLocalTeam})");
	}

	/// <summary>Update jeden Frame: rote Server-Position-Debug-Capsule auf die NEUESTE Server-Pos
	/// setzen (= ohne InterpDelay, ohne Lerp). Macht visuell sichtbar wenn das interp-puppet (= grünes
	/// _visual) hinter dem roten Server-Body herhinkt (Lag-Comp/Extrap-Issues).
	/// Default off — User aktiviert via console: cl_debug_capsule 1</summary>
	private void UpdateServerPosDebugCapsule()
	{
		if (_serverPosDebugCapsule == null) return;
		// Default off — User aktiviert via console: sv_debug_capsule 1 (server-weit, alle Clients).
		bool wantVisible = ConVars.Sv.DebugCapsule && _buf.Count > 0;
		_serverPosDebugCapsule.Visible = wantVisible;
		if (!wantVisible) return;
		var latestSnap = _buf[_buf.Count - 1].Snap;
		// CrouchBlend kommt als byte 0..255 — gleiche Lerp-Formel wie BaseCharacter.ApplyCrouchHeight.
		float crouchBlend = latestSnap.CrouchBlend / 255f;
		float h = Mathf.Lerp(_visual.StandHeight, _visual.CrouchHeight, crouchBlend);
		if (!Mathf.IsEqualApprox(_debugCapsuleMesh.Height, h)) _debugCapsuleMesh.Height = h;
		_serverPosDebugCapsule.GlobalPosition = latestSnap.Pos + new Vector3(0f, h * 0.5f, 0f);
	}

	/// <summary>Pushes a new server snapshot into the interp buffer and records the wallclock arrival
	/// timestamp used by the free-running renderTick. Out-of-order packets are dropped.
	///
	/// Fog-of-War visibility resume: if the time since the last snapshot exceeds
	/// <see cref="ResumeGapUsec"/> (= past the extrapolation cap), the buffer is wiped and the visual
	/// position snapped to the new snapshot before this push. Otherwise interpolation would lerp the
	/// puppet from the old extrapolated position to the new one over many render frames, producing a
	/// slow "slide through walls" artefact when the player re-enters the receiver's PVS.</summary>
	public void PushSnapshot(uint serverTick, SnapshotPlayer snap)
	{
		if (_buf.Count > 0 && serverTick <= _buf[_buf.Count - 1].Tick) return;
		ulong now = Time.GetTicksUsec();
		if (_lastSnapshotPushUsec > 0 && (now - _lastSnapshotPushUsec) > ResumeGapUsec)
			ResetOnVisibilityResume(snap);
		_buf.Add(new Entry { Tick = serverTick, Snap = snap });
		while (_buf.Count > Capacity) _buf.RemoveAt(0);
		_lastSnapshotPushUsec = now;

		// Reveal the TPS body the first tick the server says the remote client finished its
		// world preloads. Hidden by default in _Ready until this flag flips true.
		if (_visual != null && !_visual.Visible
			&& (snap.Flags & (byte)SnapshotFlags.WorldReady) != 0)
		{
			_visual.Visible = true;
			Dbg.Print($"[PuppetPlayer netId={NetId}] world-ready → TPS body revealed");
		}
	}

	/// <summary>Snapshot-gap threshold (microseconds) past which the puppet is treated as having re-
	/// entered the receiver's PVS. 300ms covers the extrapolation cap (16 ticks ≈ 125ms at 128Hz)
	/// plus the typical worst-case packet-loss window before falling into resume territory.</summary>
	private const ulong ResumeGapUsec = 300_000;

	/// <summary>Called when a new snapshot arrives after a long visibility gap (FoW strip + return,
	/// or sustained packet loss). Clears the interp buffer so the next <see cref="_Process"/> tick
	/// brackets only the fresh data, snaps the visual position to the resumed snapshot, and resets
	/// the smoothed interp delay to its default seed. Footstep audio is intentionally not muted —
	/// the FoW gate on <see cref="NetServer.BroadcastFootstep"/> stops generating new step events while
	/// the puppet is out of sight, and any in-flight one-shot plays out naturally.</summary>
	private void ResetOnVisibilityResume(SnapshotPlayer snap)
	{
		_buf.Clear();
		_smoothedInterpDelay = 6f;
		if (_visual != null)
			_visual.GlobalPosition = snap.Pos;
	}

	/// <summary>Resolves the effective render-delay for this frame. Locked variant (<c>cl_interp_lock</c>)
	/// returns the configured value directly. Adaptive variant targets <c>4 + 2.5 × jitterTicks</c>:
	/// 4-tick margin (≈ 31ms at 128Hz) plus a 2σ-equivalent buffer (multiplier 2.5 because the jitter
	/// signal is MAD, not stddev — see <see cref="JitterToBufferMultiplier"/>). Clamped to the [min, max]
	/// ConVar range and exponentially smoothed with a frame-rate-independent ~1-second time constant
	/// (<see cref="SmoothingRate"/>) so the puppet does not visibly snap each frame when jitter
	/// fluctuates. The live effective delay is mirrored into <see cref="NetStats.InterpDelayMs"/> for
	/// the debug overlay.</summary>
	private int ComputeEffectiveInterpDelay(float tickDt, float frameDelta)
	{
		int lockTicks = ConVars.Cl.InterpLockTicks;
		if (lockTicks > 0)
		{
			lockTicks = Mathf.Clamp(lockTicks, 1, 64);
			_smoothedInterpDelay = lockTicks;
			NetStats.InterpDelayMs = (int)(lockTicks * tickDt * 1000f);
			return lockTicks;
		}
		float jitterTicks = NetStats.JitterDownMs / (tickDt * 1000f);
		float target = 4f + JitterToBufferMultiplier * jitterTicks;
		int minTicks = Mathf.Max(1, ConVars.Cl.InterpMinTicks);
		int maxTicks = Mathf.Max(minTicks, ConVars.Cl.InterpMaxTicks);
		target = Mathf.Clamp(target, minTicks, maxTicks);
		float smoothing = 1f - Mathf.Exp(-frameDelta * SmoothingRate);
		_smoothedInterpDelay = Mathf.Lerp(_smoothedInterpDelay, target, smoothing);
		int effective = Mathf.Clamp(Mathf.RoundToInt(_smoothedInterpDelay), minTicks, maxTicks);
		NetStats.InterpDelayMs = (int)(effective * tickDt * 1000f);
		return effective;
	}

	/// <summary>Multiplier applied to the EMA-of-deviation jitter signal to derive the safety buffer.
	/// <see cref="NetStats.JitterDownMs"/> is a Mean Absolute Deviation, not a stddev — for gaussian-ish
	/// jitter MAD ≈ 0.8σ, so 2.5 × MAD ≈ 2σ which covers ~95% of inter-arrival variance. Earlier
	/// versions used 2.0 (≈ 1.6σ ≈ 89% coverage) — under-buffered on moderate jitter.</summary>
	private const float JitterToBufferMultiplier = 2.5f;
	/// <summary>Time-constant rate (1/sec) for the exponential smoothing of <see cref="_smoothedInterpDelay"/>.
	/// 1.0 ≈ 1-second time constant — slow enough that single-frame jitter spikes don't visibly snap the
	/// puppet, fast enough that a sustained network condition change is absorbed within a couple of
	/// seconds. Frame-rate independent: <c>1 - exp(-dt × rate)</c> gives the same effective convergence
	/// regardless of FPS, unlike a naive <c>dt × const</c> formula.</summary>
	private const float SmoothingRate = 1.0f;

	/// <summary>Per-frame interpolation step: computes a free-running renderTick (last-snapshot tick
	/// minus the effective interp delay plus the wallclock time since the snapshot arrived, expressed
	/// in ticks), locates the bracketing snapshots, blends them and pushes the resulting state into
	/// the visual's movement controller and animation tree.
	///
	/// The interp delay is either locked (<see cref="ClConVars.InterpLockTicks"/> &gt; 0 — competitive
	/// setting that keeps client render-time in sync with the server's hardcoded 6-tick lag-comp rewind)
	/// or adaptive: <c>4 + 2.5 × JitterDownMs / tickPeriodMs</c> (≈ 2σ buffer since the jitter signal
	/// is a Mean Absolute Deviation), clamped to [<see cref="ClConVars.InterpMinTicks"/>,
	/// <see cref="ClConVars.InterpMaxTicks"/>], smoothed across frames with a 1-second time constant
	/// so the puppet does not visibly snap when jitter fluctuates.
	///
	/// When the renderTick advances past the newest known snapshot (packet drop, server hitch), we
	/// extrapolate position forward using the newest snapshot's velocity, capped at
	/// <see cref="ExtrapolationMaxTicks"/>. View angles are intentionally NOT extrapolated — guessing
	/// where a player is looking feels worse than freezing the last known orientation.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("PuppetPlayer._Process");
		if (_visual == null || _buf.Count == 0) return;

		// Failsafe: if 5s passed since spawn AND _visual is still hidden, force-reveal.
		// Catches edge cases where the WorldReady snapshot bit never arrives (server-agent
		// bots have it set at spawn, but stale players from before the feature deployment
		// or net-glitched clients might not). Better to show a body 5s late than to leave
		// the player permanently invisible to peers.
		if (!_visual.Visible
			&& Time.GetTicksUsec() - _visualHiddenSinceUsec > VisualRevealFailsafeUsec)
		{
			_visual.Visible = true;
			Dbg.Print($"[PuppetPlayer netId={NetId}] WorldReady-failsafe → TPS revealed after 5s grace");
		}

		var client = NetMain.Instance?.Client;
		if (client == null) return;

		ushort tickRate = client.ServerTickRate > 0 ? client.ServerTickRate : (ushort)128;
		float tickDt = 1f / tickRate;
		float ticksSinceLast = _lastSnapshotPushUsec > 0
			? (float)((Time.GetTicksUsec() - _lastSnapshotPushUsec) / 1_000_000.0) * tickRate
			: 0f;

		int effectiveDelay = ComputeEffectiveInterpDelay(tickDt, (float)delta);

		float renderTickF = (float)client.LastSnapshotServerTick - effectiveDelay + ticksSinceLast;
		if (renderTickF < 0f) renderTickF = 0f;

		Entry A = _buf[0], B = _buf[_buf.Count - 1];
		bool bracketed = false;
		for (int i = 0; i < _buf.Count - 1; i++)
		{
			if ((float)_buf[i].Tick <= renderTickF && (float)_buf[i + 1].Tick >= renderTickF)
			{
				A = _buf[i]; B = _buf[i + 1]; bracketed = true; break;
			}
		}

		float extrapolateAheadTicks = 0f;
		if (!bracketed)
		{
			A = B = _buf[_buf.Count - 1];
			if (renderTickF > (float)B.Tick)
			{
				extrapolateAheadTicks = renderTickF - (float)B.Tick;
				if (extrapolateAheadTicks > ExtrapolationMaxTicks) extrapolateAheadTicks = ExtrapolationMaxTicks;
			}
		}

		float t = (A.Tick == B.Tick) ? 0f : (renderTickF - A.Tick) / (B.Tick - A.Tick);
		t = Mathf.Clamp(t, 0f, 1f);

		Vector3 pos = A.Snap.Pos.Lerp(B.Snap.Pos, t);
		if (extrapolateAheadTicks > 0f)
			pos += B.Snap.Vel * (extrapolateAheadTicks * tickDt);
		float viewYaw = Mathf.LerpAngle(A.Snap.Yaw, B.Snap.Yaw, t);
		float viewPitch = Mathf.LerpAngle(A.Snap.Pitch, B.Snap.Pitch, t);

		_visual.GlobalPosition = pos;

		if (!_bodyYawInitialized)
		{
			_puppetBodyYaw = viewYaw;
			_bodyYawInitialized = true;
		}

		Vector3 hVel = new Vector3(B.Snap.Vel.X, 0f, B.Snap.Vel.Z);
		bool moving = hVel.LengthSquared() > 1.0f;
		float rate = moving ? BodyYawRateMoving : BodyYawRateStanding;
		float lerpT = Mathf.Min(1f, rate * (float)delta);
		_puppetBodyYaw = Mathf.LerpAngle(_puppetBodyYaw, viewYaw, lerpT);

		float postTwist = Mathf.Wrap(viewYaw - _puppetBodyYaw, -Mathf.Pi, Mathf.Pi);
		if (Mathf.Abs(postTwist) > MaxTwistRad)
			_puppetBodyYaw = viewYaw - Mathf.Sign(postTwist) * MaxTwistRad;

		var r = _visual.Rotation; r.Y = _puppetBodyYaw; _visual.Rotation = r;

		if (_visual.HeadPitch != null)
		{
			var hr = _visual.HeadPitch.Rotation; hr.X = viewPitch; _visual.HeadPitch.Rotation = hr;
		}

		float spineTwist = Mathf.Wrap(viewYaw - _puppetBodyYaw, -Mathf.Pi, Mathf.Pi);
		_visual.PuppetSpineTwist = Mathf.Clamp(spineTwist, -MaxTwistRad, MaxTwistRad);

		var mc = _visual.Movement;
		mc.Velocity = B.Snap.Vel;
		_visual.Velocity = B.Snap.Vel;
		mc.AdsBlend = B.Snap.AdsBlend / 255f;
		mc.CrouchBlend = B.Snap.CrouchBlend / 255f;
		mc.WeaponRaiseBlend = B.Snap.RaiseBlend / 255f;
		mc.ShotIndex = B.Snap.ShotIndex;
		mc.AimPunch = new Vector3(B.Snap.AimPunchX / 16f, B.Snap.AimPunchY / 16f, 0f);
		mc.IsSliding = (B.Snap.Flags & (byte)SnapshotFlags.Sliding) != 0;
		mc.IsWallClinging = (B.Snap.Flags & (byte)SnapshotFlags.WallClinging) != 0;
		_visual.PuppetIsAirborne = (B.Snap.Flags & (byte)SnapshotFlags.Airborne) != 0;
		_visual.PuppetIsSprinting = (B.Snap.Flags & (byte)SnapshotFlags.Sprinting) != 0;
		_visual.PuppetIsReloading = (B.Snap.Flags & (byte)SnapshotFlags.Reloading) != 0;
		_visual.PuppetIsInspecting = (B.Snap.Flags & (byte)SnapshotFlags.Inspecting) != 0;
		_visual.PuppetActiveSlot = B.Snap.ActiveSlot;

		_visual.UpdateTpsBodyAim();

		// Distance + frustum check feeds the per-tier animation update rate. Computed
		// once per frame against the active 3D camera (local player's FPS cam, or the
		// TPS spectate cam when alive-spectating). Falls through to Near tier if no
		// camera is available (= editor / very early in the spawn flow).
		_lodTier = ResolveLodTier();
		float lodHz = LodTierUpdateHz(_lodTier);
		_lodAnimAccum += (float)delta;
		if (lodHz > 0f && _lodAnimAccum >= 1f / lodHz)
		{
			_visual.UpdateTpsAnimations();
			_visual.TpsAnimTree?.Advance(_lodAnimAccum);
			_lodAnimAccum = 0f;
		}
		ApplyAimModifierLod();

		if (_spectateMode == SpectateMode.Tps)
			UpdateSpectateTpsCollision((float)delta);

		UpdateServerPosDebugCapsule();
		if (_cachedTeamColorValid && _glowSilhouette != null && GodotObject.IsInstanceValid(_glowSilhouette)
			&& (!_lastPushedTeamColorValid || _lastPushedTeamColor != _cachedTeamColor))
		{
			_glowSilhouette.SetInstanceShaderParameter("team_color", _cachedTeamColor);
			_lastPushedTeamColor = _cachedTeamColor;
			_lastPushedTeamColorValid = true;
		}

		// Per-frame frustum gate on the silhouette. We could rely on Godot's automatic frustum
		// culling at render time (it would still skip the mesh-submit when fully off-camera), but
		// we ALSO want the upstream Visible flag to flip so the per-instance shader-parameter
		// push above becomes a no-op for off-screen puppets — saves a tiny per-frame CPU cost
		// over a 10-puppet match. Tests six representative AABB points (feet, head, four sides
		// at mid height): if ANY one is inside the active 3D camera's frustum, the puppet is
		// considered visible (matches the user request: "auch wenn nur ein stück in meiner
		// kamera ist"). Through-wall visibility is preserved via IgnoreOcclusionCulling = true
		// on the baked silhouette node — the frustum check only gates true off-camera puppets.
		if (_glowCurrentlyOn && _glowSilhouette != null && GodotObject.IsInstanceValid(_glowSilhouette))
		{
			bool wantVisible = _lodTier != PuppetLodTier.Off && SilhouetteInActiveCameraFrustum();
			if (_glowSilhouette.Visible != wantVisible) _glowSilhouette.Visible = wantVisible;
		}

		if (_lodTier != PuppetLodTier.Off)
			UpdateNameAndGlow(_buf[_buf.Count - 1].Snap);
	}

	private bool SilhouetteInActiveCameraFrustum()
	{
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null || _visual == null) return true;
		Vector3 pos = _visual.GlobalPosition;
		float h = _visual.StandHeight;
		float r = _visual.CapsuleRadius;
		float hHalf = h * 0.5f;
		if (cam.IsPositionInFrustum(pos + new Vector3(0f, hHalf, 0f))) return true;
		if (cam.IsPositionInFrustum(pos)) return true;
		if (cam.IsPositionInFrustum(pos + new Vector3(0f, h, 0f))) return true;
		if (cam.IsPositionInFrustum(pos + new Vector3(r, hHalf, 0f))) return true;
		if (cam.IsPositionInFrustum(pos + new Vector3(-r, hHalf, 0f))) return true;
		if (cam.IsPositionInFrustum(pos + new Vector3(0f, hHalf, r))) return true;
		if (cam.IsPositionInFrustum(pos + new Vector3(0f, hHalf, -r))) return true;
		return false;
	}


	/// <summary>Activates the camera matching the current <see cref="SpectateMode"/>.</summary>
	private void ApplySpectateMode()
	{
		if (_visual == null) return;
		var fpsCam = _visual.GetNodeOrNull<Camera3D>("head_pitch/fps_camera");
		var tpsCam = _visual.GetNodeOrNull<Camera3D>("head_pitch/tps_camera");
		bool wantTps = _spectateMode == SpectateMode.Tps;
		bool wantFps = _spectateMode == SpectateMode.Fps;
		if (fpsCam != null) fpsCam.Current = wantFps;
		if (tpsCam != null) tpsCam.Current = wantTps;
		if (wantTps) EnsureSpectateTpsCacheReady();
	}

	/// <summary>Maps an LOD tier to its animation update rate (Hz). Off returns 0 — the caller skips the AnimationTree advance entirely. Near is 60 Hz which matches the pre-LOD constant baseline; Mid/Far step down to amortise puppet animation cost as enemy count grows.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static float LodTierUpdateHz(PuppetLodTier tier) => tier switch
	{
		PuppetLodTier.Near => 60f,
		PuppetLodTier.Mid => 30f,
		PuppetLodTier.Far => 12f,
		_ => 0f,
	};

	/// <summary>Picks the LOD tier from distance to the active 3D camera AND a forgiving frustum check. Off-frustum AND beyond Near range collapses straight to Off; close puppets even when off-camera stay one tier above so a quick turn doesn't catch them in T-pose.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private PuppetLodTier ResolveLodTier()
	{
		Camera3D cam = GetViewport()?.GetCamera3D();
		if (cam == null || _visual == null) return PuppetLodTier.Near;
		Vector3 toPuppet = _visual.GlobalPosition - cam.GlobalPosition;
		float dist = toPuppet.Length();
		bool inFrustum = true;
		if (dist > LodNearMaxDist)
		{
			Vector3 camForward = -cam.GlobalBasis.Z;
			float dirDot = camForward.Dot(toPuppet / Mathf.Max(dist, 0.0001f));
			float fovHalfRad = cam.Fov * Mathf.Pi / 360.0f;
			float cosFovHalf = Mathf.Cos(fovHalfRad) - LodFrustumPadCos;
			inFrustum = dirDot >= cosFovHalf;
		}
		if (!inFrustum) return dist <= LodMidMaxDist ? PuppetLodTier.Far : PuppetLodTier.Off;
		if (dist <= LodNearMaxDist) return PuppetLodTier.Near;
		if (dist <= LodMidMaxDist) return PuppetLodTier.Mid;
		if (dist <= LodFarMaxDist) return PuppetLodTier.Far;
		return PuppetLodTier.Off;
	}

	/// <summary>Toggles the spine-aim SkeletonModifier3D off when the puppet is in the Off tier. No one sees the spine pose then, so the Modifier's per-frame quaternion math + SetBonePoseRotation can be skipped entirely. Result lookup is cached after first walk.</summary>
	private void ApplyAimModifierLod()
	{
		if (!_aimModifierLookupDone)
		{
			_aimModifierLookupDone = true;
			if (_visual != null)
				foreach (Node n in _visual.FindChildren("*", "TpsAimModifier", true, false))
					if (n is TpsAimModifier mod) { _cachedAimModifier = mod; break; }
		}
		if (_cachedAimModifier != null)
			_cachedAimModifier.Active = _lodTier != PuppetLodTier.Off;
	}

	private Camera3D _spectateTpsCam;
	private Vector3 _spectateTpsRestLocal;
	private bool _spectateTpsRestCached;
	private PhysicsRayQueryParameters3D _spectateRayQuery;
	private const float SpectateWallMargin = 0.15f;
	private const float SpectateSmoothRate = 12f;
	private const uint SpectateCollisionMask = 1u;

	/// <summary>Caches the spectator third-person camera reference and its rest position on first activation.</summary>
	private void EnsureSpectateTpsCacheReady()
	{
		if (_spectateTpsRestCached || _visual == null) return;
		_spectateTpsCam = _visual.GetNodeOrNull<Camera3D>("head_pitch/tps_camera");
		if (_spectateTpsCam != null)
		{
			_spectateTpsRestLocal = _spectateTpsCam.Position;
			_spectateTpsRestCached = true;
			_spectateRayQuery = new PhysicsRayQueryParameters3D { CollisionMask = SpectateCollisionMask, Exclude = new Godot.Collections.Array<Rid> { _visual.GetRid() } };
		}
	}

	/// <summary>Spring-arm step for the spectator third-person camera: raycasts pivot to rest, pulls the
	/// camera in on a hit, and smoothly lerps the result.</summary>
	private void UpdateSpectateTpsCollision(float dt)
	{
		if (!_spectateTpsRestCached) EnsureSpectateTpsCacheReady();
		if (!_spectateTpsRestCached || _visual == null) return;
		var head = _visual.HeadPitch;
		if (head == null || _spectateTpsCam == null) return;

		var space = _visual.GetWorld3D()?.DirectSpaceState;
		if (space == null) return;

		Vector3 worldDesired = head.GlobalTransform * _spectateTpsRestLocal;
		Vector3 pivot = head.GlobalPosition;
		_spectateRayQuery.From = pivot;
		_spectateRayQuery.To = worldDesired;
		var hit = space.IntersectRay(_spectateRayQuery);

		Vector3 targetLocal;
		if (hit.Count > 0)
		{
			Vector3 hitPos = (Vector3)hit["position"];
			Vector3 dir = worldDesired - pivot;
			float desiredDist = dir.Length();
			if (desiredDist > 0.001f)
			{
				float hitDist = (hitPos - pivot).Length();
				float safeDist = Mathf.Max(0.1f, hitDist - SpectateWallMargin);
				Vector3 safeWorld = pivot + dir / desiredDist * safeDist;
				targetLocal = head.GlobalTransform.AffineInverse() * safeWorld;
			}
			else targetLocal = _spectateTpsRestLocal;
		}
		else targetLocal = _spectateTpsRestLocal;

		float lerpT = 1f - Mathf.Exp(-SpectateSmoothRate * dt);
		_spectateTpsCam.Position = _spectateTpsCam.Position.Lerp(targetLocal, lerpT);
	}

	/// <summary>Reliable-event handler: spawns tracers and impact decals, plays the shoot audio, and triggers
	/// the third-person fire one-shot for a remote shot.</summary>
	public void PlayShot(byte weaponId, Vector3 origin, Vector3 dir, bool tracer,
		bool hit, Vector3 hitPos, Vector3 hitNormal, string material)
	{
		if (_visual == null) return;

		if (tracer)
		{
			Vector3 endpoint = hit ? hitPos : origin + dir * 80f;
			var anim = _visual.WeaponHolder;
			Color color = anim?.TracerColor ?? new Color(2.5f, 1.6f, 0.5f, 1f);
			float width = anim?.TracerWidth ?? 0.02f;
			float speed = anim?.TracerSpeed ?? 80f;
			float streak = anim?.TracerStreakLength ?? 2f;
			BulletTracer.Spawn(GetTree(), origin, endpoint, color, width, speed, streak);
		}

		if (hit)
		{
			BulletImpactManager.Instance?.Spawn(hitPos, hitNormal, (StringName)(material ?? "default"));
		}

		float shotLength = hit ? (hitPos - origin).Length() : _visual.HitscanRange;
		SmokeVoxelField.DisturbAll(origin, dir, shotLength);

		WeaponStats weaponStats = _visual.WeaponHolder?.ActiveWeapon ?? ConVars.Weapons.M4A1;
		_visual.Audio?.PlayShoot(weaponStats, origin, ReverbEnv.Outdoor);

		_visual.TpsAnimTree?.Set(PlayerCore._aFire, (int)AnimationNodeOneShot.OneShotRequest.Fire);
	}

	/// <summary>Reliable-event handler for a remote footstep: plays the spatial audio sample.</summary>
	public void PlayFootstep(Vector3 pos, string material, byte loudness, bool leftFoot, bool sprinting)
	{
		if (_visual?.Audio == null) return;
		float loud01 = loudness / 255f;
		_visual.Audio.PlayStep(pos, (StringName)(material ?? "default"), loud01, inTunnel: false, sprinting);
	}

	/// <summary>Reliable-event handler for a remote jump: triggers the viewmodel, one-shot animation and audio.</summary>
	public void PlayJump()
	{
		if (_visual == null) return;
		_visual.WeaponHolder?.TriggerJump(autoLand: false);
		_visual.TpsAnimTree?.Set(PlayerCore._aJumpStart, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		if (_visual.Audio != null)
		{
			var (mat, inTunnel) = ProbeGround();
			_visual.Audio.PlayJump(_visual.GlobalPosition, mat, 0.75f, inTunnel);
		}
	}

	/// <summary>Reliable-event handler for a remote landing: triggers the heavy or light land one-shot.</summary>
	public void PlayLand(float impactSpeed)
	{
		if (_visual == null) return;
		_visual.WeaponHolder?.TriggerLand(impactSpeed);
		bool heavy = impactSpeed > _visual.TpsHeavyLandThreshold;
		StringName param = heavy ? PlayerCore._aJumpLandHeavy : PlayerCore._aJumpLand;
		_visual.TpsAnimTree?.Set(param, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		if (_visual.Audio != null && impactSpeed > 1.5f)
		{
			float impact01 = Mathf.Clamp((impactSpeed - 1.5f) / 7f, 0f, 1f);
			var (mat, inTunnel) = ProbeGround();
			_visual.Audio.PlayLand(_visual.GlobalPosition, mat, impact01, inTunnel);
		}
	}

	/// <summary>Reliable-event handler for a remote grenade throw: spawns a puppet-mode SmokeGrenade
	/// (no local physics — follows ProjectileState snapshots from the owner). The puppet's body Rid is
	/// excluded from the grenade's raycast so the can doesn't immediately collide with the puppet capsule.</summary>
	public void SpawnGrenade(byte ownerNetId, uint projectileId, byte grenadeType, Vector3 origin, Vector3 velocity)
	{
		if (_visual == null) return;
		SmokeGrenade.Spawn(_visual.GetParent(), origin, velocity, _visual.GetRid(),
			ownerNetId: ownerNetId, projectileId: projectileId, isPuppet: true);
	}

	/// <summary>Down-raycast under the puppet so jump and land sounds probe the same material and tunnel-reverb
	/// state as the local player.</summary>
	private (StringName material, bool inTunnel) ProbeGround()
	{
		var space = _visual.GetWorld3D()?.DirectSpaceState;
		if (space == null) return ((StringName)"default", false);
		Vector3 from = _visual.GlobalPosition + Vector3.Up * 0.4f;
		HitInfo hit = Hitscan.Cast(space, from, Vector3.Down, 1.0f, exclude: _visual.GetRid(), mask: _visual.HitscanMask);
		var mat = hit.Hit ? hit.Material : (StringName)"default";
		bool inTunnel = hit.Hit && hit.Collider != null && hit.Collider.IsInGroup("tunnel");
		return (mat, inTunnel);
	}
}
