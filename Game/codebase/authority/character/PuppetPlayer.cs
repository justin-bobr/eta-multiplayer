using Godot;
using System.Collections.Generic;

/// <summary>
/// Remote-player driver. Interpolates GlobalPosition/Rotation from a ring buffer of snapshots at render
/// time = serverTickEstimate - interpDelay (~6 ticks); brackets the surrounding pair and lerps, extrapolating
/// briefly past the newest snapshot on packet drop. Animation state is written into the MovementController
/// to drive the third-person body.
/// </summary>
[GlobalClass]
public partial class PuppetPlayer : NetworkPlayer
{
	/// <summary>Display name taken from the PlayerJoined event. Used only for logging.</summary>
	public string PlayerName = "";

	/// <summary>Spectator mode for this puppet. The setter activates the matching camera (e.g. after death).</summary>
	public SpectateMode SpectateMode
	{
		get => _spectateMode;
		set { _spectateMode = value; ApplySpectateMode(); }
	}
	private SpectateMode _spectateMode = SpectateMode.None;

	private const int Capacity = 32;

	/// <summary>Cap on extrapolation past the newest snapshot before the puppet freezes. 16 ticks ≈ 125ms at 128Hz.</summary>
	private const int ExtrapolationMaxTicks = 16;

	private struct Entry { public uint Tick; public SnapshotPlayer Snap; }
	private readonly List<Entry> _buf = new();
	/// <summary>Wallclock when the most recent snapshot was pushed; drives the free-running renderTick.</summary>
	private ulong _lastSnapshotPushUsec;
	/// <summary>Adaptive interp delay, exponentially smoothed across frames. Updated each <see cref="_Process"/>
	/// from <see cref="NetStats.JitterDownMs"/> when <see cref="ClConVars.InterpLockTicks"/> is 0.</summary>
	private float _smoothedInterpDelay = 6f;
	/// <summary>Free-running virtual server-tick this client renders at. Advances by delta×tickRate and is
	/// nudged toward the raw target rather than re-anchored each snapshot (avoids per-snapshot micro-snaps);
	/// hard-resets only past <see cref="RenderClockResnapTicks"/>.</summary>
	private float _renderClockTickF;
	private bool _renderClockInitialized;
	/// <summary>Hard re-anchor threshold for the render-clock; below it drift is bled in invisibly. 4 ticks ≈ 31ms.</summary>
	private const float RenderClockResnapTicks = 4f;
	/// <summary>Ticks/sec the clock may nudge when bleeding off sub-resnap drift (~4 ms/s).</summary>
	private const float RenderClockNudgeRateTicksPerSec = 0.5f;
	/// <summary>Last yaw/pitch from a bracketed pair, held during extrapolation instead of snapping to A.Yaw
	/// (which would head-twitch on packet drop).</summary>
	private float _lastBracketedYaw;
	private float _lastBracketedPitch;
	private bool _lastBracketedAnglesValid;
	/// <summary>Self-reference for call-sites that ask the puppet for its visual; the puppet is the NetworkPlayer.</summary>
	public NetworkPlayer GetVisual() => this;

	private float _puppetBodyYaw;
	private bool _bodyYawInitialized;
	private const float MaxTwistRad = 1.5708f;
	private const float BodyYawRateMoving = 12f;
	private const float BodyYawRateStanding = 6f;


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
	/// <summary>Last team colour pushed to the silhouette shader param; skips the SetInstanceShaderParameter
	/// call when unchanged (team_color flips only when TeamSlot does).</summary>
	private Color _lastPushedTeamColor;
	private bool _lastPushedTeamColorValid;
	/// <summary>False until the first UpdateNameAndGlow runs; forces the TeamSlot block on the first call so an
	/// initial TeamSlot=255 sentinel isn't skipped by the delta check (else material stays white for seconds).</summary>
	private bool _hasInitialAppliedTeamColor = false;
	/// <summary>Wallclock when Visible was set false in _Ready. After <see cref="VisualRevealFailsafeUsec"/> the
	/// body is force-revealed, so bots that never send WorldInitComplete aren't permanently invisible.</summary>
	private ulong _visualHiddenSinceUsec;
	private const ulong VisualRevealFailsafeUsec = 5_000_000;

	/// <summary>Pre-baked silhouette mesh under the puppet's Skeleton3D (GlowSilhouetteMeshBaker tool). All
	/// puppets share one baked mesh + material chain; per-puppet team colour is pushed via
	/// SetInstanceShaderParameter("team_color", …). Glow on/off is a <see cref="GeometryInstance3D.Visible"/> toggle.</summary>
	private GlowSilhouetteMeshBaker _glowSilhouette;
	/// <summary>World-space Label3D ("Name\nHP") parented to the puppet body (not the head bone, whose subtree
	/// is scaled 0.01). Rendered only on the glow-text layer; the composite shader stamps it back over the scene.</summary>
	private Label3D _glowNameLabel;

	private const uint GlowTextVisualLayer = 1u << 19;

	/// <summary>Instantiates the visual child, configures animation throttling, and wires the puppet flags.</summary>
	public override void _Ready()
	{
		base._Ready();   // SetupSim, anim tree, hitbox rig, OnSimReady (client collision layer)
		if (Engine.IsEditorHint()) return;
		ViewMode = ViewMode.Tps;
		Visible = false;
		_visualHiddenSinceUsec = Time.GetTicksUsec();
		ApplySpectateMode();

		if (TpsAnimTree != null)
			TpsAnimTree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;

		_debugCapsuleMesh = new CapsuleMesh
		{
			Radius = CapsuleRadius,
			Height = StandHeight,
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

		CallDeferred(MethodName.BuildGlowVisualsDeferred);
	}

	/// <summary>Finds the pre-baked silhouette mesh, resets the delta trackers, and attaches the Label3D
	/// nameplate. Team colour and visibility are applied in <see cref="UpdateNameAndGlow"/>/<see cref="ApplyTeamGlow"/>.</summary>
	private void BuildGlowVisualsDeferred()
	{
		_glowSilhouette = FindGlowSilhouette(this);
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
			Position = new Vector3(0f, StandHeight + 0.25f, 0f),
			Layers = GlowTextVisualLayer,
			Visible = false,
		};
		AddChild(_glowNameLabel);

		Dbg.Print($"[PuppetPlayer netId={NetId}] glow visuals built: silhouette={(_glowSilhouette != null)} + Label3D");

		if (_buf.Count > 0) UpdateNameAndGlow(_buf[_buf.Count - 1].Snap);
	}

	/// <summary>Depth-first search for the baked GlowSilhouetteMeshBaker (path not hard-coded since it may be
	/// nested anywhere). Returns the first match.</summary>
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

	/// <summary>5 base colours (blue/green/red/purple/yellow), indexed by TeamSlot. Deterministic, no net sync.</summary>
	private static readonly Color[] PlayerPalette = new[]
	{
		new Color(0.30f, 0.60f, 1.00f), // blue
		new Color(0.40f, 0.95f, 0.35f), // green
		new Color(1.00f, 0.30f, 0.30f), // red
		new Color(0.70f, 0.40f, 1.00f), // purple
		new Color(1.00f, 0.95f, 0.30f), // yellow
	};

	/// <summary>Deterministic per-player colour from NetId (same on every client). Used for glow, label and
	/// scoreboard square.</summary>
	public static Color PlayerColor(byte netId) => PlayerPalette[netId % PlayerPalette.Length];

	/// <summary>Pushes HP/Team/TeamSlot into the nameplate and body-ID material. Glow + label only for teammates
	/// (or all when spectating) and not Deathmatch. Fields are delta-checked to avoid per-frame churn.</summary>
	private void UpdateNameAndGlow(SnapshotPlayer snap)
	{
		var localSnap = NetMain.Instance?.Client?.LastSelfSnap;
		bool localTeamKnown = localSnap.HasValue;
		byte localTeam = localTeamKnown ? localSnap.Value.Team : (byte)255;
		bool isDeathmatch = snap.Team == (byte)Team.Deathmatch;
		bool isTeammate = localTeamKnown && !isDeathmatch && snap.Team == localTeam;
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

	/// <summary>Toggles the silhouette + Label3D Visible (the entire on/off mechanism, since the material chain
	/// is baked). Gated by Settings.TeamGlow; spectator visibility is decided in <see cref="UpdateNameAndGlow"/>.</summary>
	private void ApplyTeamGlow(bool enabled)
	{
		_glowCurrentlyOn = enabled;
		if (_glowSilhouette != null && GodotObject.IsInstanceValid(_glowSilhouette))
			_glowSilhouette.Visible = enabled;
		if (_glowNameLabel != null) _glowNameLabel.Visible = enabled;
		Dbg.Print($"[PuppetPlayer netId={NetId}] team glow {(enabled ? "ON" : "OFF")} — silhouette toggled (puppetTeam={_lastAppliedTeam} localTeam={_lastAppliedLocalTeam})");
	}

	/// <summary>Positions the red debug capsule at the latest server position (no interp/lerp), showing when the
	/// interp puppet lags the server body. Off by default (cl_debug_capsule 1).</summary>
	private void UpdateServerPosDebugCapsule()
	{
		if (_serverPosDebugCapsule == null) return;
		bool wantVisible = ConVars.Sv.DebugCapsule && _buf.Count > 0;
		_serverPosDebugCapsule.Visible = wantVisible;
		if (!wantVisible) return;
		var latestSnap = _buf[_buf.Count - 1].Snap;
		float crouchBlend = latestSnap.CrouchBlend / 255f;
		float h = Mathf.Lerp(StandHeight, CrouchHeight, crouchBlend);
		if (!Mathf.IsEqualApprox(_debugCapsuleMesh.Height, h)) _debugCapsuleMesh.Height = h;
		_serverPosDebugCapsule.GlobalPosition = latestSnap.Pos + new Vector3(0f, h * 0.5f, 0f);
	}

	/// <summary>Pushes a new snapshot into the interp buffer and records its arrival wallclock. Out-of-order
	/// packets are dropped. If the gap since the last snapshot exceeds <see cref="ResumeGapUsec"/>, the buffer
	/// is wiped and the position snapped (FoW resume), avoiding a "slide through walls" lerp on PVS re-entry.</summary>
	public void PushSnapshot(uint serverTick, SnapshotPlayer snap)
	{
		if (_buf.Count > 0 && serverTick <= _buf[_buf.Count - 1].Tick) return;
		ulong now = Time.GetTicksUsec();
		if (_lastSnapshotPushUsec > 0 && (now - _lastSnapshotPushUsec) > ResumeGapUsec)
			ResetOnVisibilityResume(snap);
		_buf.Add(new Entry { Tick = serverTick, Snap = snap });
		while (_buf.Count > Capacity) _buf.RemoveAt(0);
		_lastSnapshotPushUsec = now;

		if (!Visible
			&& (snap.Flags & (byte)SnapshotFlags.WorldReady) != 0)
		{
			Visible = true;
			Dbg.Print($"[PuppetPlayer netId={NetId}] world-ready → TPS body revealed");
		}
	}

	/// <summary>Snapshot-gap threshold (µs) past which the puppet is treated as re-entering the PVS (300ms).</summary>
	private const ulong ResumeGapUsec = 300_000;

	/// <summary>Called on a snapshot after a long visibility gap: clears the buffer so the next tick brackets
	/// only fresh data, snaps the position, and reseeds the smoothed interp delay.</summary>
	private void ResetOnVisibilityResume(SnapshotPlayer snap)
	{
		_buf.Clear();
		_smoothedInterpDelay = 6f;
			GlobalPosition = snap.Pos;
	}

	/// <summary>Resolves the render-delay for this frame. Locked (<c>cl_interp_lock</c>) returns the configured
	/// value; adaptive targets <c>4 + 2.5 × jitterTicks</c>, clamped to the ConVar range and exponentially
	/// smoothed (~1s constant). Mirrored into <see cref="NetStats.InterpDelayMs"/>.</summary>
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

	/// <summary>Multiplier on the MAD jitter signal (<see cref="NetStats.JitterDownMs"/>) for the safety buffer;
	/// 2.5 × MAD ≈ 2σ (~95% coverage).</summary>
	private const float JitterToBufferMultiplier = 2.5f;
	/// <summary>Rate (1/sec) for the exponential smoothing of <see cref="_smoothedInterpDelay"/> (~1s constant,
	/// frame-rate independent).</summary>
	private const float SmoothingRate = 1.0f;

	/// <summary>Per-frame interpolation: computes the free-running renderTick, brackets the surrounding
	/// snapshots, blends them and pushes the result into the movement controller and animation tree. Past the
	/// newest snapshot, position extrapolates from its velocity (capped at <see cref="ExtrapolationMaxTicks"/>);
	/// view angles are held, not extrapolated.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("PuppetPlayer._Process");
		if (_buf.Count == 0) return;

		if (!Visible
			&& Time.GetTicksUsec() - _visualHiddenSinceUsec > VisualRevealFailsafeUsec)
		{
			Visible = true;
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

		float targetRenderTickF = (float)client.LastSnapshotServerTick - effectiveDelay + ticksSinceLast;
		if (targetRenderTickF < 0f) targetRenderTickF = 0f;
		float renderTickF;
		if (!_renderClockInitialized)
		{
			_renderClockTickF = targetRenderTickF;
			_renderClockInitialized = true;
			renderTickF = _renderClockTickF;
		}
		else
		{
			float drift = targetRenderTickF - _renderClockTickF;
			if (Mathf.Abs(drift) > RenderClockResnapTicks)
			{
				_renderClockTickF = targetRenderTickF;
			}
			else
			{
				_renderClockTickF += (float)delta * tickRate;
				float maxNudge = (float)delta * RenderClockNudgeRateTicksPerSec;
				_renderClockTickF += Mathf.Clamp(drift, -maxNudge, maxNudge);
			}
			renderTickF = _renderClockTickF;
		}

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

		float viewYaw, viewPitch;
		if (bracketed)
		{
			viewYaw = Mathf.LerpAngle(A.Snap.Yaw, B.Snap.Yaw, t);
			viewPitch = Mathf.LerpAngle(A.Snap.Pitch, B.Snap.Pitch, t);
			_lastBracketedYaw = viewYaw;
			_lastBracketedPitch = viewPitch;
			_lastBracketedAnglesValid = true;
		}
		else if (_lastBracketedAnglesValid)
		{
			viewYaw = _lastBracketedYaw;
			viewPitch = _lastBracketedPitch;
		}
		else
		{
			viewYaw = B.Snap.Yaw;
			viewPitch = B.Snap.Pitch;
		}

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

		float bodyYawCos = Mathf.Cos(_puppetBodyYaw);
		float bodyYawSin = Mathf.Sin(_puppetBodyYaw);
		var bodyBasis = new Basis(
			new Vector3(bodyYawCos, 0f, -bodyYawSin),
			new Vector3(0f, 1f, 0f),
			new Vector3(bodyYawSin, 0f, bodyYawCos));
		GlobalTransform = new Transform3D(bodyBasis, pos);

		if (HeadPitch != null)
		{
			float pitchCos = Mathf.Cos(viewPitch);
			float pitchSin = Mathf.Sin(viewPitch);
			var pitchBasis = new Basis(
				new Vector3(1f, 0f, 0f),
				new Vector3(0f, pitchCos, pitchSin),
				new Vector3(0f, -pitchSin, pitchCos));
			var headXform = HeadPitch.Transform;
			headXform.Basis = pitchBasis;
			HeadPitch.Transform = headXform;
		}

		float spineTwist = Mathf.Wrap(viewYaw - _puppetBodyYaw, -Mathf.Pi, Mathf.Pi);
		PuppetSpineTwist = Mathf.Clamp(spineTwist, -MaxTwistRad, MaxTwistRad);

		var mc = Movement;
		mc.Velocity = (A.Snap.Vel.Lerp(B.Snap.Vel, t));
		Velocity = mc.Velocity;
		mc.AdsBlend = Mathf.Lerp(A.Snap.AdsBlend, B.Snap.AdsBlend, t) / 255f;
		mc.CrouchBlend = Mathf.Lerp(A.Snap.CrouchBlend, B.Snap.CrouchBlend, t) / 255f;
		mc.WeaponRaiseBlend = Mathf.Lerp(A.Snap.RaiseBlend, B.Snap.RaiseBlend, t) / 255f;
		mc.ShotIndex = B.Snap.ShotIndex;
		float apX = Mathf.Lerp(A.Snap.AimPunchX, B.Snap.AimPunchX, t) / 16f;
		float apY = Mathf.Lerp(A.Snap.AimPunchY, B.Snap.AimPunchY, t) / 16f;
		mc.AimPunch = new Vector3(apX, apY, 0f);
		mc.IsSliding = (B.Snap.Flags & (byte)SnapshotFlags.Sliding) != 0;
		mc.IsWallClinging = (B.Snap.Flags & (byte)SnapshotFlags.WallClinging) != 0;
		PuppetIsAirborne = (B.Snap.Flags & (byte)SnapshotFlags.Airborne) != 0;
		PuppetIsSprinting = (B.Snap.Flags & (byte)SnapshotFlags.Sprinting) != 0;
		PuppetIsReloading = (B.Snap.Flags & (byte)SnapshotFlags.Reloading) != 0;
		PuppetIsInspecting = (B.Snap.Flags & (byte)SnapshotFlags.Inspecting) != 0;
		PuppetActiveSlot = B.Snap.ActiveSlot;

		UpdateTpsBodyAim();
		UpdateTpsMontages();

		Camera3D cam = GetViewport()?.GetCamera3D();
		Vector3 camPos = Vector3.Zero;
		Vector3 camForward = -Vector3.Forward;
		float cosFovHalf = -1f;
		if (cam != null)
		{
			camPos = cam.GlobalPosition;
			camForward = -cam.GlobalBasis.Z;
			float fovHalfRad = cam.Fov * Mathf.Pi / 360.0f;
			cosFovHalf = Mathf.Cos(fovHalfRad);
		}

		_lodTier = ResolveLodTierCached(cam, camPos, camForward, cosFovHalf);
		float lodHz = LodTierUpdateHz(_lodTier);
		_lodAnimAccum += (float)delta;
		if (lodHz > 0f && _lodAnimAccum >= 1f / lodHz)
		{
			using (MiniProfiler.SampleClient("PuppetPlayer.TpsTree.Advance")) TpsAnimTree?.Advance(_lodAnimAccum);
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

		if (_glowCurrentlyOn && _glowSilhouette != null && GodotObject.IsInstanceValid(_glowSilhouette))
		{
			bool wantVisible = _lodTier != PuppetLodTier.Off
				&& SilhouetteInFrustumManual(cam, camPos, camForward, cosFovHalf);
			if (_glowSilhouette.Visible != wantVisible) _glowSilhouette.Visible = wantVisible;
		}

		if (_lodTier != PuppetLodTier.Off)
			UpdateNameAndGlow(_buf[_buf.Count - 1].Snap);
	}

	/// <summary>Manual frustum test against the cached camera basis: a single cone-angle test of the puppet's
	/// mid-point vs camForward with a radius/distance-scaled pad (replaces a 7-point IsPositionInFrustum sweep).</summary>
	private bool SilhouetteInFrustumManual(Camera3D cam, Vector3 camPos, Vector3 camForward, float cosFovHalf)
	{
		if (cam == null) return true;
		Vector3 center = GlobalPosition + new Vector3(0f, StandHeight * 0.5f, 0f);
		Vector3 toPuppet = center - camPos;
		float dist = toPuppet.Length();
		if (dist < 0.0001f) return true;
		float forwardDist = camForward.Dot(toPuppet);
		if (forwardDist < -StandHeight) return false;
		float angularPadCos;
		if (forwardDist <= 0.1f)
		{
			angularPadCos = 1f;
		}
		else
		{
			float capR = Mathf.Max(CapsuleRadius, StandHeight * 0.5f);
			float angularExtentRad = Mathf.Atan2(capR, forwardDist);
			angularPadCos = Mathf.Cos(Mathf.Acos(Mathf.Clamp(cosFovHalf, -1f, 1f)) + angularExtentRad);
		}
		float dirDot = forwardDist / dist;
		return dirDot >= angularPadCos;
	}

	/// <summary>Activates the camera matching the current <see cref="SpectateMode"/>.</summary>
	private void ApplySpectateMode()
	{
		var fpsCam = GetNodeOrNull<Camera3D>("head_pitch/fps_camera");
		var tpsCam = GetNodeOrNull<Camera3D>("head_pitch/tps_camera");
		bool wantTps = _spectateMode == SpectateMode.Tps;
		bool wantFps = _spectateMode == SpectateMode.Fps;
		if (fpsCam != null) fpsCam.Current = wantFps;
		if (tpsCam != null) tpsCam.Current = wantTps;
		if (wantTps) EnsureSpectateTpsCacheReady();
	}

	/// <summary>Maps an LOD tier to its animation update rate (Hz); Off returns 0 to skip the advance entirely.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static float LodTierUpdateHz(PuppetLodTier tier) => tier switch
	{
		PuppetLodTier.Near => 60f,
		PuppetLodTier.Mid => 30f,
		PuppetLodTier.Far => 12f,
		_ => 0f,
	};

	/// <summary>Picks the LOD tier from distance to the camera plus a forgiving frustum check. Takes the camera
	/// + basis from the caller to avoid a second GetViewport().GetCamera3D() per frame.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private PuppetLodTier ResolveLodTierCached(Camera3D cam, Vector3 camPos, Vector3 camForward, float cosFovHalf)
	{
		if (cam == null) return PuppetLodTier.Near;
		Vector3 toPuppet = GlobalPosition - camPos;
		float dist = toPuppet.Length();
		bool inFrustum = true;
		if (dist > LodNearMaxDist)
		{
			float dirDot = camForward.Dot(toPuppet / Mathf.Max(dist, 0.0001f));
			inFrustum = dirDot >= (cosFovHalf - LodFrustumPadCos);
		}
		if (!inFrustum) return dist <= LodMidMaxDist ? PuppetLodTier.Far : PuppetLodTier.Off;
		if (dist <= LodNearMaxDist) return PuppetLodTier.Near;
		if (dist <= LodMidMaxDist) return PuppetLodTier.Mid;
		if (dist <= LodFarMaxDist) return PuppetLodTier.Far;
		return PuppetLodTier.Off;
	}

	private bool _lastAimModifierActive;
	private bool _lastAimModifierActiveValid;

	/// <summary>Disables the spine-aim modifier in the Off tier (skipping its per-frame quaternion math). Lookup
	/// is cached; the Active setter is delta-gated.</summary>
	private void ApplyAimModifierLod()
	{
		if (!_aimModifierLookupDone)
		{
			_aimModifierLookupDone = true;
				_cachedAimModifier = AimModifier;
		}
		if (_cachedAimModifier == null) return;
		bool wantActive = _lodTier != PuppetLodTier.Off;
		if (!_lastAimModifierActiveValid || _lastAimModifierActive != wantActive)
		{
			_cachedAimModifier.Active = wantActive;
			_lastAimModifierActive = wantActive;
			_lastAimModifierActiveValid = true;
		}
	}

	private Camera3D _spectateTpsCam;
	private Vector3 _spectateTpsRestLocal;
	private bool _spectateTpsRestCached;
	private PhysicsRayQueryParameters3D _spectateRayQuery;
	private readonly PhysicsRayQueryResult3D _spectateRayResult = new();
	private const float SpectateWallMargin = 0.15f;
	private const float SpectateSmoothRate = 12f;
	private const uint SpectateCollisionMask = 1u;

	/// <summary>Caches the spectator third-person camera reference and its rest position on first activation.</summary>
	private void EnsureSpectateTpsCacheReady()
	{
		if (_spectateTpsRestCached) return;
		_spectateTpsCam = GetNodeOrNull<Camera3D>("head_pitch/tps_camera");
		if (_spectateTpsCam != null)
		{
			_spectateTpsRestLocal = _spectateTpsCam.Position;
			_spectateTpsRestCached = true;
			_spectateRayQuery = new PhysicsRayQueryParameters3D { CollisionMask = SpectateCollisionMask, Exclude = new Godot.Collections.Array<Rid> { GetRid() } };
		}
	}

	/// <summary>Spring-arm step for the spectator third-person camera: raycasts pivot to rest, pulls the
	/// camera in on a hit, and smoothly lerps the result.</summary>
	private void UpdateSpectateTpsCollision(float dt)
	{
		if (!_spectateTpsRestCached) EnsureSpectateTpsCacheReady();
		if (!_spectateTpsRestCached) return;
		var head = HeadPitch;
		if (head == null || _spectateTpsCam == null) return;

		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return;

		Vector3 worldDesired = head.GlobalTransform * _spectateTpsRestLocal;
		Vector3 pivot = head.GlobalPosition;
		_spectateRayQuery.From = pivot;
		_spectateRayQuery.To = worldDesired;

		Vector3 targetLocal;
		if (space.IntersectRayInto(_spectateRayQuery, _spectateRayResult))
		{
			Vector3 hitPos = _spectateRayResult.GetPosition();
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

		if (tracer && (_tpsWeapon == null || _tpsWeapon.ShouldSpawnTracer()))
		{
			Vector3 tracerStart = _tpsWeapon != null ? _tpsWeapon.GetMuzzleWorldPosition() : GlobalPosition + Vector3.Up * 1.4f;
			Vector3 endpoint = hit ? hitPos : tracerStart + dir * 80f;
			BulletTracer.Spawn(GetTree(), tracerStart, endpoint,
				_tpsWeapon?.TracerColor ?? new Color(2.5f, 1.6f, 0.5f, 1f),
				_tpsWeapon?.TracerWidth ?? 0.006f,
				_tpsWeapon?.TracerSpeed ?? 80f,
				_tpsWeapon?.TracerStreakLength ?? 2f);
		}

		if (hit)
		{
			BulletImpactManager.Instance?.Spawn(hitPos, hitNormal, (StringName)(material ?? "default"));
		}

		float shotLength = hit ? (hitPos - origin).Length() : HitscanRange;
		SmokeVoxelField.DisturbAll(origin, dir, shotLength);

		WeaponStats weaponStats = ConVars.Weapons.AR15;
		Audio?.PlayShoot(weaponStats, origin, ReverbEnv.Outdoor);

		_tpsWeapon?.MuzzleSmoke();
		_tpsWeapon?.MuzzleFlash();
		if (_lodTier != PuppetLodTier.Off)
			_tpsWeapon?.EjectCasing();
	}

	/// <summary>Reliable-event handler for a remote footstep: plays the spatial audio sample.</summary>
	public void PlayFootstep(Vector3 pos, string material, byte loudness, bool leftFoot, bool sprinting)
	{
		if (Audio == null) return;
		float loud01 = loudness / 255f;
		Audio.PlayStep(pos, (StringName)(material ?? "default"), loud01, inTunnel: false, sprinting);
	}

	/// <summary>Reliable-event handler for a remote reload: drops the magazine from the TPS weapon.</summary>
	public void PlayDropMag()
	{
		if (_lodTier != PuppetLodTier.Off) _tpsWeapon?.DropMagazine();
	}

	/// <summary>Reliable-event handler for a remote jump: triggers the viewmodel, one-shot animation and audio.</summary>
	public void PlayJump()
	{
		if (Audio != null)
		{
			var (mat, inTunnel) = ProbeGround();
			Audio.PlayJump(GlobalPosition, mat, 0.75f, inTunnel);
		}
	}

	/// <summary>Reliable-event handler for a remote landing: triggers the heavy or light land one-shot.</summary>
	public void PlayLand(float impactSpeed)
	{
		if (Audio != null && impactSpeed >= ConVars.Cl.JumpMinFallSpeed)
		{
			float impact01 = Mathf.Clamp((impactSpeed - 1.5f) / 7f, 0f, 1f);
			var (mat, inTunnel) = ProbeGround();
			Audio.PlayLand(GlobalPosition, mat, impact01, inTunnel);
		}
	}

	/// <summary>Reliable-event handler for a remote grenade throw: spawns a puppet-mode SmokeGrenade (follows
	/// owner ProjectileState snapshots). The puppet body Rid is excluded from the grenade's raycast.</summary>
	public void SpawnGrenade(byte ownerNetId, uint projectileId, byte grenadeType, Vector3 origin, Vector3 velocity)
	{
		SmokeGrenade.Spawn(GetParent(), origin, velocity, GetRid(),
			ownerNetId: ownerNetId, projectileId: projectileId, isPuppet: true);
	}

	/// <summary>Down-raycast under the puppet so jump and land sounds probe the same material and tunnel-reverb
	/// state as the local player.</summary>
	private (StringName material, bool inTunnel) ProbeGround()
	{
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return ((StringName)"default", false);
		Vector3 from = GlobalPosition + Vector3.Up * 0.4f;
		HitInfo hit = Hitscan.Cast(space, from, Vector3.Down, 1.0f, exclude: GetRid(), mask: HitscanMask);
		var mat = hit.Hit ? hit.Material : (StringName)"default";
		bool inTunnel = hit.Hit && hit.Collider != null && hit.Collider.IsInGroup("tunnel");
		return (mat, inTunnel);
	}
}
