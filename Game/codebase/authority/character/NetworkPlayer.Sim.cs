using Godot;

/// <summary>Player camera view — determines which Camera3D is currently active.</summary>
public enum ViewMode { Fps, Tps, Disabled }

/// <summary>Shared player simulation: movement, hitscan, mantle, crouch, footsteps, grenades, plus
/// puppet/server visual hooks. <see cref="LocalPlayer"/> derives from this and adds the local-only
/// camera, mouse-look and aim-guide logic.</summary>
public partial class NetworkPlayer : CharacterBody3D
{
	public PlayerAudio Audio { get; private set; }

	public const int TickRate = 128;
	protected float _fixedDt;
	public uint CurrentTick { get; protected set; }

	[Export(PropertyHint.Range, "0.5,2.5,0.05")]
	public float StandHeight = 1.8f;
	[Export(PropertyHint.Range, "0.5,2.0,0.05")]
	public float CrouchHeight = 1.2f;
	[Export(PropertyHint.Range, "0.1,1.0,0.01")]
	public float CapsuleRadius = 0.4f;
	[Export(PropertyHint.Range, "0.0,1.0,0.05")]
	public float FloorSnapDist = 0.6f;
	[Export(PropertyHint.Range, "0,90,1")]
	public float FloorMaxAngleDeg = 50f;
	[Export(PropertyHint.Range, "0.0,1.0,0.01")]
	public float StepMaxHeight = 0.45f;

	[Export(PropertyHint.Range, "1.0,2.5,0.05")]
	public float StandEyeHeight = 1.7f;
	[Export(PropertyHint.Range, "0.5,2.0,0.05")]
	public float CrouchEyeHeight = 1.1f;

	[Export]
	public CollisionShape3D BodyCollision;
	protected CapsuleShape3D _capsule;
	protected Vector3 _headBasePos;

	public byte NetId;

	[Export]
	public Skeleton3D TpsSkeleton;

	protected HitboxRig _hitboxRig;
	/// <summary>Read-only access to the hitbox rig (for NetServer debug broadcasts that need positions).</summary>
	public HitboxRig GetHitboxRig() => _hitboxRig;

	/// <summary>Whether this driver builds a hitbox rig. ServerPlayer needs it for hit-reg, the puppet for
	/// the debug overlay + casing self-exclude; LocalPlayer overrides to false.</summary>
	protected virtual bool NeedsHitboxRig => true;
	/// <summary>Bone-pose history for lag-comp. Only initialised on the ServerAgent.</summary>
	public BonePoseRewindBuffer BoneHistory;

	public MovementController Movement { get; } = new();

	/// <summary>When set, the movement sim reads from this instead of the live input singleton.
	/// ServerPlayer: filled per tick by NetServer. ServerBotPlayer: set once at spawn.</summary>
	public InputPacket? NetInputSource;

	/// <summary>Frozen state (reconnect pool): _PhysicsProcess returns immediately and the pose stays.
	/// CollisionLayer/Mask are nulled so live players do not get stuck on the ghost body.</summary>
	public bool IsFrozen
	{
		get => _isFrozen;
		set
		{
			if (_isFrozen == value) return;
			_isFrozen = value;
			if (value)
			{
				_savedCollisionLayer = CollisionLayer;
				_savedCollisionMask = CollisionMask;
				CollisionLayer = 0u;
				CollisionMask = 0u;
			}
			else
			{
				if (_savedCollisionLayer != 0u) CollisionLayer = _savedCollisionLayer;
				if (_savedCollisionMask != 0u) CollisionMask = _savedCollisionMask;
			}
		}
	}
	private bool _isFrozen;
	private uint _savedCollisionLayer;
	private uint _savedCollisionMask;

	/// <summary>Death state: no movement, no collision, no shooting. Set by NetServer on the HP=0 trigger
	/// and cleared on respawn. Uses the same collision-zero logic as <see cref="IsFrozen"/>.</summary>
	public bool IsDead
	{
		get => _isDead;
		set
		{
			if (_isDead == value) return;
			_isDead = value;
			if (value)
			{
				if (_savedCollisionLayerDead == 0u && _savedCollisionMaskDead == 0u)
				{
					_savedCollisionLayerDead = CollisionLayer;
					_savedCollisionMaskDead = CollisionMask;
				}
				CollisionLayer = 0u;
				CollisionMask = 0u;
				Velocity = Vector3.Zero;
			}
			else
			{
				if (_savedCollisionLayerDead != 0u) CollisionLayer = _savedCollisionLayerDead;
				if (_savedCollisionMaskDead != 0u) CollisionMask = _savedCollisionMaskDead;
				_savedCollisionLayerDead = 0u;
				_savedCollisionMaskDead = 0u;
			}
		}
	}
	private bool _isDead;
	private uint _savedCollisionLayerDead;
	private uint _savedCollisionMaskDead;

	/// <summary>Tick index of the last consumed input. Sent back to the client as ackedTick for reconciliation.</summary>
	public uint LastAppliedInputTick;

	/// <summary>Builds a unique capsule resource per instance so crouch resize does not shrink every player.</summary>
	protected void SetupCapsule() =>
		_capsule = CharacterSetup.SetupCapsule(this, BodyCollision, StandHeight, CapsuleRadius, FloorMaxAngleDeg, FloorSnapDist);

	[ExportGroup("Firing")]
	[Export]
	public bool CanFire = true;

	/// <summary>True iff the magazine is empty and not already mid-reload. Drives the bot's ReloadPressed
	/// input via <see cref="NetServer.UpdateBotInputs"/>.</summary>
	public bool NeedsReload => Movement != null && Movement.CurrentMag == 0 && !Movement.IsReloading;
	[Export]
	public float HitscanRange = 200f;
	[Export]
	public uint HitscanMask = 1;

	private const float MantleMinHeight = 1.0f;
	private const float MantleMaxHeight = 1.75f;
	private const float MantleReach = 0.7f;
	private const float MantleDuration = 0.35f;

	/// <summary>True when this NetworkPlayer instance is a puppet visual — set externally by the
	/// <see cref="PuppetPlayer"/> wrapper before AddChild. Stays mutable because the wrapper owns the flag.</summary>
	public bool IsPuppet;
	/// <summary>True when this node broadcasts server-authoritative events (shot, footstep, jump, land, hit).</summary>
	public bool IsServerAuthority => IsServerAgent;

	public bool PuppetIsAirborne;
	public bool PuppetIsSprinting;
	public bool PuppetIsReloading;
	public bool PuppetIsInspecting;
	/// <summary>0 = weapon, 1 = grenade. Written by PuppetPlayer from Snapshot.ActiveSlot; without it the
	/// puppet's UpperBodyMix gate would stay at 0 (no FixedTick advances _activeSlot).</summary>
	public byte PuppetActiveSlot;
	/// <summary>Spine twist (view yaw minus body yaw, radians). UpdateTpsBodyAim applies it to the aim bone so
	/// the upper body follows the look direction; the body catches up past 90° delta.</summary>
	public float PuppetSpineTwist;

	private readonly FootstepController _footstepLogic = new();
	/// <summary>Cached fallback footstep material; avoids marshalling a fresh StringName per untagged step.</summary>
	private static readonly StringName _defaultMaterial = "default";

	private readonly GrenadeController _grenade = new();
	protected int _activeSlot;

	/// <summary>Mantle: three forward offsets for the down-raycast scan, pre-allocated to avoid per-tick allocations.</summary>
	private static readonly float[] _mantleForwardOffsets = { 0.08f, 0.18f, 0.35f };

	/// <summary>TpsAimModifier child under the skeleton; drives the spine twist/pitch for the TPS body
	/// aim pose (server + remote). Auto-created in <see cref="_Ready"/> when absent.</summary>
	public TpsAimModifier AimModifier { get; private set; }

	protected Vector3 _pendingThrowOrigin;
	protected Vector3 _pendingThrowVel;
	protected bool _pendingThrowValid;

	/// <summary>Public getter — netcode reads the footstep phase for the snapshot.</summary>
	public FootstepController FootstepLogic => _footstepLogic;
	/// <summary>Vertical velocity captured before MoveAndSlide. Used for land-impact scaling.</summary>
	public float PreMoveVelocityY => _preMoveVelocityY;
	/// <summary>Active weapon slot (0 = weapon, 1 = grenade) — used by the HUD.</summary>
	public int ActiveSlot => _activeSlot;
	/// <summary>Live grenade charge 0..1 while the throw key is held — used by the HUD.</summary>
	public float GrenadeCharge => _grenade.Charge;

	private bool _wasOnFloor;
	protected float _preMoveVelocityY;
	protected Vector3 _prevPhysicsPos;
	protected Vector3 _currentPhysicsPos;
	protected bool _isMantling;
	private Vector3 _mantleStart;
	private Vector3 _mantleTarget;
	private float _mantleTimer;
	/// <summary>Tick until which reconciliation is blocked (mantle plus grace window). Mantle state isn't
	/// checkpointed, so a replay during/after a mantle would snap the player back using stale snapshots.</summary>
	protected uint _mantleReconcileBlockUntilTick;
	protected MovementInput _lastMovementInput;

	/// <summary>Per-tick hook after the deterministic step. LocalPlayer pushes prediction + acks its own
	/// tick; ServerPlayer acks the consumed net-input tick. Base/puppet: nothing (puppets don't tick).</summary>
	protected virtual void OnTickApplied() { }
	/// <summary>Per-tick prediction buffer for reconciliation. Filled only for IsLocalPlayer.</summary>
	public readonly PredictionBuffer Prediction = new();
	/// <summary>Smooth-correction state: when ApplyServerCorrection detects a small drift it is faded out
	/// through this offset instead of snapping.</summary>
	protected Vector3 _correctionPending;
	/// <summary>Visual error after a replay (previously-visible minus new replay position).
	/// <see cref="_Process"/> adds this to GlobalPosition and fades it per tick to avoid a visible snap.</summary>
	protected Vector3 _visualErrorOffset;
	/// <summary>Bleed-out rate (1/sec) applied to <see cref="_visualErrorOffset"/> each tick. Set by
	/// <see cref="ApplyServerCorrection"/> by drift magnitude; reset in <see cref="ResetInterpToCurrentPos"/>.</summary>
	protected float _activeBleedRate = 6.5f;

	/// <summary>Wallclock at the start of the current FixedTick; subtick-fire offsets the fire-press wallclock
	/// from this to get a fractional in-tick position.</summary>
	protected ulong _tickStartUsec;
	/// <summary>Wallclock of the most recent fire-press edge, written by LocalPlayer's _Input with sub-tick
	/// precision and read by SendNetInput to compute FireSubTick. Stays 0 on server agents and puppets.</summary>
	protected ulong _lastFirePressUsec;

	protected readonly System.Collections.Generic.List<BufferedSubtickEvent> _subtickBuffer = new(Packets.MaxSubtickEventsWire);
	/// <summary>Held-input bitmask updated live on every input event. End-of-tick value seeds the
	/// MovementInput's legacy held flags as well as the next tick's <see cref="_intervalStartBits"/>.</summary>
	protected InputBits _liveBits;
	/// <summary>Held-input bitmask at the start of the current input-collection interval (= the previous
	/// tick's <see cref="_liveBits"/> snapshot). Used as <see cref="MovementInput.InitialBits"/>.</summary>
	protected InputBits _intervalStartBits;
	protected float _intervalStartViewYaw;
	protected float _intervalStartViewPitch;
	/// <summary>Wallclock at the start of the previous tick — the lower bound of the interval whose events
	/// are flushed into this tick's <see cref="MovementInput"/>. Set in <see cref="_PhysicsProcess"/>
	/// immediately before <see cref="_tickStartUsec"/> is updated.</summary>
	protected ulong _prevTickStartUsec;

	protected PhysicsRayQueryParameters3D _rayQuery;
	protected readonly PhysicsRayQueryResult3D _rayResult = new();
	protected Godot.Collections.Array<Rid> _selfExclude;
	/// <summary>Rate-limit timestamp (msec) for the "[stepup] BLOCKED — obstacle height" diagnostic log (max once/sec).</summary>
	private ulong _lastStepupBlockedLogMs;
	/// <summary>Rate-limit timestamp (msec) for the "[stepup] +X.XXm" success log (throttled to limit GC pressure).</summary>
	private ulong _lastStepupSuccessLogMs;
	/// <summary>True for the LocalPlayer from _Ready until the fade-out triggers once FootstepAudio preloads finish.</summary>
	protected bool _waitingForFadeOut;
	/// <summary>Position of the last blocked TryStepUp. Cooldown gate: skip the full TestMove sequence until the
	/// player moves &gt;10cm, since 3×TestMove + 1×IntersectRay per tick dominated load when walking into a wall.</summary>
	private Vector3 _stepupLastBlockedPos = new(float.MinValue, 0, 0);
	private uint _stepupLastBlockedTick;
	private const uint StepupBlockedCooldownTicks = 8;

	/// <summary>Initializes physics tuning, audio banks, hitbox rig, and the third-person aim setup. Server
	/// agents take an early-out and skip all visual-only setup.</summary>
	private void SetupSim()
	{
		Engine.PhysicsTicksPerSecond = TickRate;
		_fixedDt = 1f / TickRate;

		_selfExclude = new Godot.Collections.Array<Rid> { GetRid() };
		_rayQuery = new PhysicsRayQueryParameters3D { Exclude = _selfExclude };

		SetupCapsule();
		SetupHeadPitch();

		FloorMaxAngle = Mathf.DegToRad(FloorMaxAngleDeg);
		FloorSnapLength = FloorSnapDist;
		FloorBlockOnWall = true;
		FloorStopOnSlope = false;
		Movement.Stamina = ConVars.Sv.MaxStamina;
		Movement.ResetSpawnConsumables();
		WeaponStats spawnWeapon = ConVars.Weapons.AR15;
		Movement.InitializeAmmo(spawnWeapon);
		if (spawnWeapon != null) Movement.FireMode = spawnWeapon.FireMode;
		GrenadeTrajectory.Gravity = GrenadeTrajectory.BaseGravity / Mathf.Max(0.1f, ConVars.Sv.GrenadeRangeScale);
		Audio = new PlayerAudio(
			GetNodeOrNull<FootstepAudio>("FootstepAudio"),
			GetNodeOrNull<WeaponAudio>("WeaponAudio"));
		Audio.Configure(IsLocalPlayer, ConVars.Weapons.AR15);
		WarmUpAudio();
		_wasOnFloor = IsOnFloor();

		HitscanMask = 1u | HitboxRig.Layer;

		if (TpsSkeleton != null && NeedsHitboxRig)
		{
			bool baked = false;
			foreach (Node c in GetChildren())
				if (c is HitboxBaker baker) { baked = baker.Baked; break; }
			_hitboxRig = new HitboxRig { Skeleton = TpsSkeleton, Name = "HitboxRig" };
			AddChild(_hitboxRig);
			_hitboxRig.Build(skipAutoOrient: baked);
		}

		if (TpsSkeleton != null)
		{
			foreach (var child in TpsSkeleton.GetChildren())
			{
				if (child is TpsAimModifier mod)
				{
					AimModifier = mod;
					AimModifier.HeadPitch = HeadPitch;
					AimModifier.AimBoneName = TpsAimBoneName;
					AimModifier.PitchScale = TpsAimPitchScale;
					break;
				}
			}
			if (AimModifier == null && !string.IsNullOrEmpty(TpsAimBoneName))
			{
				int tpsAimBoneIdx = TpsSkeleton.FindBone(TpsAimBoneName);
				if (tpsAimBoneIdx < 0)
					GD.PushWarning($"[NetworkPlayer] TpsAimBoneName='{TpsAimBoneName}' not in skeleton — pitch/twist disabled");
				else
				{
					AimModifier = new TpsAimModifier
					{
						Name = "aim_modifier_auto",
						HeadPitch = HeadPitch,
						AimBoneName = TpsAimBoneName,
						PitchScale = TpsAimPitchScale,
					};
					TpsSkeleton.AddChild(AimModifier);
					Dbg.Print($"[NetworkPlayer] Auto-Setup TpsAimModifier on {TpsAimBoneName} (rig-independent)");
				}
			}
		}

		if (TpsAnimTree != null) TpsAnimTree.Active = true;
		PreWarmAnimationOneShots(TpsAnimTree);
		ApplyViewMode();
		_prevPhysicsPos = GlobalPosition;
		_currentPhysicsPos = GlobalPosition;

		OnSimReady();
	}

	/// <summary>Per-mode tail of <see cref="SetupSim"/>: sets the collision layer and any mode-specific spawn
	/// finalization. Base/puppet = client layer; ServerPlayer and LocalPlayer override.</summary>
	protected virtual void OnSimReady()
	{
		CollisionLayer = 1u << 1;
		CollisionMask = 1u | (1u << 1);
	}

	/// <summary>Fires + aborts every AnimationNodeOneShot in the tree (found via "/request" property names) at
	/// spawn, so Godot lazy-loads the referenced animations here instead of spiking the first gameplay event.</summary>
	private void PreWarmAnimationOneShots(AnimationTree tree)
	{
		if (tree == null) return;
		var props = tree.GetPropertyList();
		int count = 0;
		foreach (Godot.Collections.Dictionary prop in props)
		{
			if (prop["name"].VariantType != Variant.Type.String && prop["name"].VariantType != Variant.Type.StringName)
				continue;
			string name = prop["name"].AsString();
			if (!name.StartsWith("parameters/") || !name.EndsWith("/request"))
				continue;
			tree.Set(name, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			tree.Set(name, (int)AnimationNodeOneShot.OneShotRequest.Abort);
			count++;
		}
		if (count > 0)
			Dbg.Print($"[prewarm] pre-fired {count} one-shot(s) on '{tree.Name}' ({(IsPuppet ? "puppet" : IsServerAgent ? "server-agent" : "local")})");
	}


	/// <summary>Server-only: frees TPS mesh instances and hides the visual root (skeleton bones for hitbox
	/// posing remain). Overridden by <see cref="ServerPlayer"/>; base is a no-op.</summary>
	protected virtual void DisableExpensiveSubtreeProcessing() { }

	/// <summary>Local-only: plays silent footstep/jump/land samples at a hidden position at spawn to lazy-load
	/// the banks. Overridden by <see cref="LocalPlayer"/>; base is a no-op.</summary>
	protected virtual void WarmUpAudio() { }

	/// <summary>Authority position for snapshots/reconciliation — always the real physics state, never the
	/// visually lerped _Process value (which would drift during the inter-tick window).</summary>
	public Vector3 AuthorityPosition { get => _currentPhysicsPos; set { } }

	/// <summary>Ticks that have passed since spawn/respawn. Reconciliation is skipped while inside the
	/// settle window (30 ticks).</summary>
	protected int _ticksSinceSpawn;
	protected const int SpawnSettleTicks = 30;

	/// <summary>True while a server reconciliation is currently replaying the last ticks. Side effects
	/// (audio, tracers, decals, net-input send) are skipped during a replay — they already ran during the
	/// original tick.</summary>
	protected bool _isReplaying;

	/// <summary>Called by NetServer once per tick (after BoneAttachment3D updates) — snapshots all
	/// hitbox GlobalTransforms into the ring buffer. Server agent only.</summary>
	public void PushBoneHistory(uint tick)
	{
		if (BoneHistory == null || _hitboxRig == null) return;
		BoneHistory.Push(tick, _hitboxRig.CollisionShapes);
	}

	/// <summary>Resets render-interp state after a teleport so the first frame doesn't lerp from the old
	/// position, and clears any in-flight visual reconciliation offset.</summary>
	public void ResetInterpToCurrentPos()
	{
		_prevPhysicsPos = GlobalPosition;
		_currentPhysicsPos = GlobalPosition;
		_ticksSinceSpawn = 0;
		Prediction.Clear();
		_visualErrorOffset = Vector3.Zero;
		_activeBleedRate = Mathf.Max(0.01f, ConVars.Cl.ReconBleedNormal);
		_lastFirePressUsec = 0;
	}

	/// <summary>Per-physics-tick driver. Skips puppets (externally positioned) and frozen agents, runs the
	/// deterministic FixedTick for LocalPlayer and ServerAgent, and triggers visual updates on non-server
	/// instances.</summary>
	public override void _PhysicsProcess(double delta)
	{
		if (IsPuppet) return;
		if (IsFrozen) return;
		if (!IsLocalPlayer && !IsServerAgent) return;
		using var _prof = IsServerAgent ? MiniProfiler.SampleServer("NetworkPlayer._PhysicsProcess") : MiniProfiler.SampleClient("NetworkPlayer._PhysicsProcess (Local)");

		_prevTickStartUsec = _tickStartUsec;
		_tickStartUsec = Time.GetTicksUsec();

		if (IsLocalPlayer)
		{
			double nowSec = Time.GetTicksMsec() / 1000.0;
			if (nowSec - _reconcileWindowStartSec >= 1.0)
			{
				NetStats.ReconcilesPerSec = _reconcileCountWindow;
				_reconcileCountWindow = 0;
				_reconcileWindowStartSec = nowSec;
			}
		}

		GlobalPosition = _currentPhysicsPos;
		_prevPhysicsPos = _currentPhysicsPos;

		FixedTick(_fixedDt);

		_currentPhysicsPos = GlobalPosition;

		using (MiniProfiler.SampleClient("NetworkPlayer.UpdateTpsBodyAim")) UpdateTpsBodyAim();
	}

	/// <summary>Server-replayable tick step, called with constant <paramref name="dt"/> = 1/TickRate. Only code
	/// that must also run on the server belongs here (movement, fire, stamina).</summary>
	private void FixedTick(float dt)
	{
		CurrentTick++;
		_ticksSinceSpawn++;

		if (IsDead)
		{
			Velocity = Vector3.Zero;
			return;
		}

		if (_isMantling)
		{
			StepMantle(dt);
			ApplyCrouchHeight();
			return;
		}

		MovementInput moveIn;
		using (MiniProfiler.SampleClient("NetworkPlayer.BuildMovementInput")) moveIn = BuildMovementInput(dt);
		_lastMovementInput = moveIn;
		Movement.Velocity = Velocity;
		using (MiniProfiler.SampleClient("NetworkPlayer.Movement.Step")) Movement.Step(moveIn);
		Velocity = Movement.Velocity;

		FireInput fireIn;
		using (MiniProfiler.SampleClient("NetworkPlayer.BuildFireInput")) fireIn = BuildFireInput(dt);
		Movement.FireStep(fireIn);
		if (Movement.DidFireThisFrame)
		{
			using (MiniProfiler.SampleClient("NetworkPlayer.HandleHitscan")) HandleHitscan();
			Dbg.Print($"[fire] tick={CurrentTick} shot #{Movement.ShotIndex} ({ConVars.Weapons.AR15?.Name}) | next in {Movement.FireCooldown * 1000f:0}ms");
		}

		using (MiniProfiler.SampleClient("NetworkPlayer.HandleGrenades")) HandleGrenades(dt);

		_preMoveVelocityY = Velocity.Y;
		Movement.PreMoveHorizSpeed = new Vector3(Velocity.X, 0f, Velocity.Z).Length();

		using (MiniProfiler.SampleClient("NetworkPlayer.TryStepUp")) TryStepUp(dt);
		using (MiniProfiler.SampleClient("NetworkPlayer.MoveAndSlide")) MoveAndSlide();
		using (MiniProfiler.SampleClient("NetworkPlayer.TryMantle")) TryMantle();

		ApplyCrouchHeight();
		using (MiniProfiler.SampleClient("NetworkPlayer.HandleLandingDetection")) HandleLandingDetection();
		HandleJumpAnimation();
		if (Movement.DidReloadThisFrame && !_isReplaying)
			OnDropMagEvent();
		using (MiniProfiler.SampleClient("NetworkPlayer.HandleFootsteps")) HandleFootsteps();
		using (MiniProfiler.SampleClient("NetworkPlayer.HandleWeaponAudio")) HandleWeaponAudio();

		OnTickApplied();

		if (_correctionPending.LengthSquared() > 0.0001f)
		{
			float step = Mathf.Min(1f, _fixedDt * 8f);
			Vector3 delta = _correctionPending * step;
			GlobalPosition += delta;
			_currentPhysicsPos += delta;
			_prevPhysicsPos += delta;
			_correctionPending -= delta;
		}

		if (_visualErrorOffset.LengthSquared() > 0.0001f)
		{
			float bleedStep = Mathf.Min(1f, _fixedDt * _activeBleedRate);
			_visualErrorOffset *= 1f - bleedStep;
			if (_visualErrorOffset.LengthSquared() < 0.0001f) _visualErrorOffset = Vector3.Zero;
		}
	}

	/// <summary>Per-second reconcile-rate window feeding NetStats.ReconcilesPerSec. The count is bumped by
	/// LocalPlayer.ApplyServerCorrection (reconciliation lives on the local driver); the window is rolled in
	/// the shared stats pass.</summary>
	protected int _reconcileCountWindow;
	private double _reconcileWindowStartSec;


	/// <summary>Steps the footstep cadence and emits a step event per step. Cadence
	/// (<see cref="FootstepController"/>) is deterministic/replayable; material probing and audio are
	/// client-side side effects.</summary>
	private void HandleFootsteps()
	{
		using (MiniProfiler.SampleClient("NetworkPlayer.HandleFootsteps.Cadence"))
		{
			_footstepLogic.Step(new FootstepInput
			{
				Dt = _fixedDt,
				HorizontalSpeed = Movement.HorizontalSpeed,
				OnFloor = IsOnFloor(),
				ShiftHeld = _lastMovementInput.ShiftHeld,
				CrouchHeld = Movement.CrouchBlend > 0.5f,
				IsSprinting = Movement.ActuallySprinting,
				IsSliding = Movement.IsSliding,
			});
		}

		if (!_footstepLogic.DidStepThisFrame) return;
		if (_isReplaying) return;

		HitInfo ground;
		using (MiniProfiler.SampleClient("NetworkPlayer.HandleFootsteps.CastGround"))
			ground = CastGround();
		StringName material = ground.Hit ? ground.Material : _defaultMaterial;

		OnFootstepEvent(ground, material);
	}

	/// <summary>Per-tick weapon audio (shoot/dry-fire/reload). Cosmetic, Local-only — overridden by
	/// <see cref="LocalPlayer"/>; base is a no-op.</summary>
	protected virtual void HandleWeaponAudio() { }

	/// <summary>Down-raycast under the feet with the same material detection as <see cref="HandleHitscan"/>.</summary>
	protected HitInfo CastGround()
	{
		var space = GetWorld3D().DirectSpaceState;
		Vector3 from = GlobalPosition + Vector3.Up * 0.4f;
		return Hitscan.CastMulti(space, from, Vector3.Down, 1.0f, _selfExclude, mask: 1u);
	}

	/// <summary>True when the ground collider is in the "tunnel" group, used to swap to tunnel reverb.</summary>
	protected static bool IsTunnelGround(HitInfo ground)
		=> ground.Hit && ground.Collider != null && ground.Collider.IsInGroup("tunnel");

	/// <summary>Performs the hitscan after DoFire using the movement controller's LastShotOrigin/Direction.
	/// On server authority, also does lag-compensated damage and broadcasts ShotFired.</summary>
	private void HandleHitscan()
	{
		if (_isReplaying) return;
		ResolveShot(GetWorld3D().DirectSpaceState);
	}

	/// <summary>Per-shot resolution. Base = the local client's cosmetic pass (decal, smoke, tracer).
	/// <see cref="ServerPlayer"/> overrides with the authoritative lag-comp cast + broadcast.</summary>
	protected virtual void ResolveShot(PhysicsDirectSpaceState3D space)
	{
		HitInfo hit;
		using (MiniProfiler.SampleClient("NetworkPlayer.Hitscan.Cast")) hit = Hitscan.Cast(space, Movement.LastShotOrigin, Movement.LastShotDirection,
			HitscanRange, exclude: GetRid(), mask: HitscanMask);

		float shotLength = hit.Hit ? hit.Distance : HitscanRange;
		using (MiniProfiler.SampleClient("NetworkPlayer.SmokeVoxelField.DisturbAll")) SmokeVoxelField.DisturbAll(Movement.LastShotOrigin, Movement.LastShotDirection, shotLength);

		if (_fpsWeapon != null && _currentWeapon != null)
		{
			Vector3 start = _currentWeapon.GetMuzzleWorldPosition();
			Vector3 end = hit.Hit ? hit.Position : start + Movement.LastShotDirection * HitscanRange;
			if (_currentWeapon.ShouldSpawnTracer())
				BulletTracer.Spawn(GetTree(), start, end, _currentWeapon.TracerColor, _currentWeapon.TracerWidth, _currentWeapon.TracerSpeed, _currentWeapon.TracerStreakLength);
			_currentWeapon.MuzzleSmoke();
			_currentWeapon.MuzzleFlash();
		}

		if (hit.Hit)
		{
			using (MiniProfiler.SampleClient("NetworkPlayer.BulletImpactManager.Spawn")) BulletImpactManager.Instance?.Spawn(hit.Position, hit.Normal, hit.Material);
			if (Dbg.Enabled)
			{
				string typeName = hit.Collider?.GetType().Name ?? "?";
				string parent = hit.Collider?.GetParent()?.Name ?? "?";
				string groupsStr = "<none>";
				if (hit.Collider != null)
				{
					var groups = hit.Collider.GetGroups();
					if (groups.Count > 0)
					{
						var parts = new string[groups.Count];
						for (int i = 0; i < groups.Count; i++) parts[i] = groups[i].ToString();
						groupsStr = string.Join(",", parts);
					}
				}
				Dbg.Print($"[hit] tick={CurrentTick} | {hit.Collider?.Name ?? "?"} ({typeName}) parent={parent} | mat={hit.Material} groups=[{groupsStr}] | pos={hit.Position:F2} dist={hit.Distance:F2}m");
			}
		}
		else
		{
			Dbg.Print($"[hit] tick={CurrentTick} | NO HIT (origin={Movement.LastShotOrigin:F2} dir={Movement.LastShotDirection:F2})");
		}
	}

	/// <summary>Samples this tick's weapon buttons from the driver's input source. Base = neutral (puppets
	/// never tick); <see cref="LocalPlayer"/> reads Godot input, <see cref="ServerPlayer"/> the packet.</summary>
	protected virtual WeaponButtons SampleWeaponButtons() => default;

	/// <summary>Resolves <see cref="_activeSlot"/> from the driver's input source (LocalPlayer: slot-key
	/// edges; ServerPlayer: the packet's SlotIsGrenade bit). Base = no-op (puppets/server-no-packet).</summary>
	protected virtual void ResolveActiveSlot() { }

	/// <summary>Builds the per-tick fire input, gating weapon actions when the grenade slot is active. The
	/// ServerAgent pulls triggers from the replicated <see cref="NetworkPlayer.NetInputSource"/> (no Godot
	/// input on the headless server); LocalPlayer reads live input.</summary>
	private FireInput BuildFireInput(float dt)
	{
		bool weaponSlot = _activeSlot == 0;
		WeaponButtons btn = SampleWeaponButtons();
		bool firePressed = weaponSlot && btn.Fire;
		bool reloadPressed = weaponSlot && btn.Reload;
		bool inspectPressed = weaponSlot && btn.Inspect;
		bool adsHeld = weaponSlot && btn.Ads;

		WeaponStats weapon = ConVars.Weapons.AR15;

		Vector3 shootOrigin = HeadPitch != null ? HeadPitch.GlobalPosition : GlobalPosition;

		uint fireTick = (IsServerAgent && NetInputSource.HasValue) ? NetInputSource.Value.TickIndex : CurrentTick;

		float fireYaw = Movement.SubtickFireViewValid ? Movement.SubtickFireViewYaw : Rotation.Y;
		float firePitch = Movement.SubtickFireViewValid
			? Movement.SubtickFireViewPitch
			: (HeadPitch != null ? HeadPitch.Rotation.X : 0f);

		return new FireInput
		{
			TickIndex = fireTick,
			FirePressed = firePressed,
			ReloadPressed = reloadPressed,
			InspectPressed = inspectPressed,
			AdsHeld = adsHeld,
			CanFire = CanFire && weapon != null,
			Weapon = weapon,
			Speed = Movement.HorizontalSpeed,
			ShooterPosition = shootOrigin,
			ViewYaw = fireYaw,
			ViewPitch = firePitch,
			Dt = dt,
		};
	}

	/// <summary>Handles slot switching (weapon vs grenade) and steps the grenade charge controller; throws on
	/// fire-release in the grenade slot. Deterministic/replayable. The ServerAgent pulls the slot from the
	/// replicated InputPacket.</summary>
	private void HandleGrenades(float dt)
	{
		ResolveActiveSlot();
		bool grenadeSlot = _activeSlot == 1;
		bool firePressed = SampleWeaponButtons().Fire;
		_grenade.Step(new GrenadeInput
		{
			SlotActive = grenadeSlot,
			ThrowHeld = grenadeSlot && firePressed,
			Dt = dt,
		});

		if (_grenade.DidThrowThisFrame)
		{
			ThrowGrenade();
		}
		else
		{
			_pendingThrowValid = grenadeSlot
				&& ComputeThrow(_grenade.Charge, out _pendingThrowOrigin, out _pendingThrowVel);
		}
	}

	/// <summary>Spawns a <see cref="SmokeGrenade"/> in front of the camera; throw speed lerps with held charge.</summary>
	private void ThrowGrenade()
	{
		if (!_pendingThrowValid) return;
		var (projectileId, ownerNetId) = RegisterGrenadeThrow(_pendingThrowOrigin, _pendingThrowVel);
		SmokeGrenade.Spawn(GetParent(), _pendingThrowOrigin, _pendingThrowVel, GetRid(),
			ownerNetId: ownerNetId, projectileId: projectileId, isPuppet: false);
		Dbg.Print($"[grenade] thrown tick={CurrentTick} vel={_pendingThrowVel:F1} pid={projectileId}");
	}

	/// <summary>Driver hook: registers a thrown grenade with the netcode + returns its (projectileId, owner)
	/// for the spawn. <see cref="LocalPlayer"/> allocates a predicted id and sends it to the server; base = (0,0).</summary>
	protected virtual (uint projectileId, byte ownerNetId) RegisterGrenadeThrow(Vector3 origin, Vector3 vel) => (0u, 0);

	/// <summary>Computes the throw origin and velocity for a given charge. Shared by <see cref="ThrowGrenade"/>
	/// and the aim guide so the preview matches the actual flight path.</summary>
	private bool ComputeThrow(float charge, out Vector3 origin, out Vector3 vel)
	{
		origin = vel = Vector3.Zero;
		if (HeadPitch == null) return false;
		Vector3 fwd = -HeadPitch.GlobalTransform.Basis.Z;
		Vector3 dir = (fwd + Vector3.Up * ConVars.Sv.GrenadeThrowUpBias).Normalized();
		float speed = Mathf.Lerp(ConVars.Sv.GrenadeMinThrowSpeed, ConVars.Sv.GrenadeMaxThrowSpeed, charge);
		Vector3 inherit = new Vector3(Velocity.X, 0f, Velocity.Z) * ConVars.Sv.GrenadeInheritVelocity;
		vel = dir * speed + inherit;
		origin = HeadPitch.GlobalPosition + fwd * 0.4f;
		return true;
	}

	private float _serverSmoothedBodyYaw;
	private bool _serverBodyYawInitialized;
	private const float ServerBodyYawMaxTwistRad = 1.5708f;
	private const float ServerBodyYawRateMoving = 12f;
	private const float ServerBodyYawRateStanding = 6f;

	/// <summary>Syncs spine twist/pitch onto the <see cref="AimModifier"/>. The server-agent path mirrors the
	/// puppet's lagged body-yaw smoothing and rotates the skeleton root so bone poses match the puppet; lag-comp
	/// then works because BoneHistory stores the aim+twist-correct GlobalTransforms.</summary>
	public void UpdateTpsBodyAim()
	{
		if (IsServerAgent && AimModifier != null && NetInputSource.HasValue)
		{
			float svViewYaw = NetInputSource.Value.ViewYaw;
			if (!_serverBodyYawInitialized)
			{
				_serverSmoothedBodyYaw = svViewYaw;
				_serverBodyYawInitialized = true;
			}
			Vector3 hVel = new Vector3(Velocity.X, 0f, Velocity.Z);
			bool moving = hVel.LengthSquared() > 1.0f;
			float rate = moving ? ServerBodyYawRateMoving : ServerBodyYawRateStanding;
			float lerpT = Mathf.Min(1f, rate * _fixedDt);
			_serverSmoothedBodyYaw = Mathf.LerpAngle(_serverSmoothedBodyYaw, svViewYaw, lerpT);
			float postTwist = Mathf.Wrap(svViewYaw - _serverSmoothedBodyYaw, -Mathf.Pi, Mathf.Pi);
			if (Mathf.Abs(postTwist) > ServerBodyYawMaxTwistRad)
				_serverSmoothedBodyYaw = svViewYaw - Mathf.Sign(postTwist) * ServerBodyYawMaxTwistRad;
			var rot = Rotation; rot.Y = _serverSmoothedBodyYaw; Rotation = rot;
			AimModifier.SpineTwist = Mathf.Wrap(svViewYaw - _serverSmoothedBodyYaw, -Mathf.Pi, Mathf.Pi);
			return;
		}

		if (ViewMode != ViewMode.Tps)
		{
			if (AimModifier != null) AimModifier.SpineTwist = 0f;
			return;
		}

		float twist = IsPuppet ? PuppetSpineTwist : 0f;
		if (AimModifier != null)
			AimModifier.SpineTwist = twist;
		if (TpsAnimTree != null)
			TpsAnimTree.Set(_pTpsAimAdd, Movement?.AdsBlend ?? 0f);
	}

	private static readonly StringName _pTpsAimAdd = "parameters/AimAdd/add_amount";

	/// <summary>Configures the head-pitch pivot position to the stand-eye height.</summary>
	private void SetupHeadPitch()
	{
		if (HeadPitch != null)
		{
			HeadPitch.Position = new Vector3(HeadPitch.Position.X, StandEyeHeight, HeadPitch.Position.Z);
			_headBasePos = HeadPitch.Position;
			Dbg.Print($"[character] Eye height set to {StandEyeHeight:0.00}m");
		}
		else GD.PushWarning("[character] HeadPitch reference missing — camera will not drop on crouch");
	}

	/// <summary>Base = a neutral idle input, used by <see cref="ServerPlayer"/>'s fallback before the first net
	/// packet. Overridden by <see cref="LocalPlayer"/> (live input) and <see cref="ServerPlayer"/> (net packet).</summary>
	protected virtual MovementInput BuildMovementInput(float dt) => new()
	{
		TickIndex = CurrentTick,
		Dt = dt,
		Weapon = ConVars.Weapons.AR15,
		OnFloor = IsOnFloor(),
		TouchingWall = IsOnWall(),
		WallNormal = IsOnWall() ? GetWallNormal() : Vector3.Zero,
		ViewYaw = Rotation.Y,
		ViewPitch = HeadPitch != null ? HeadPitch.Rotation.X : 0f,
	};

	/// <summary>Resizes the capsule and lerps the head-pitch eye height between stand and crouch.</summary>
	protected void ApplyCrouchHeight()
	{
		CharacterSetup.ApplyCrouchHeight(_capsule, BodyCollision, StandHeight, CrouchHeight, Movement.CrouchBlend);
		if (HeadPitch != null)
		{
			float blend = Movement.CrouchBlend;
			float y = Mathf.Lerp(StandEyeHeight, CrouchEyeHeight, blend);
			HeadPitch.Position = new Vector3(_headBasePos.X, y, _headBasePos.Z);
		}
	}

	/// <summary>Pre-MoveAndSlide step-up: tests for a small lip ahead and lifts the body by the actual step
	/// height; MoveAndSlide + FloorSnap then settle onto the new floor. Runs every tick (gating caused
	/// stair-stutter at sprint speed). Skips when airborne.</summary>
	protected void TryStepUp(float dt)
	{
		if (StepMaxHeight <= 0f || !IsOnFloor()) return;

		Vector3 horizVel = new Vector3(Velocity.X, 0f, Velocity.Z);
		Vector3 wishLocal = _lastMovementInput.WishDir;
		bool hasInput = wishLocal.LengthSquared() > 0.01f;
		bool hasVel = horizVel.LengthSquared() >= 0.25f;
		if (!hasInput && !hasVel) return;

		if (CurrentTick - _stepupLastBlockedTick < StepupBlockedCooldownTicks
			&& GlobalPosition.DistanceSquaredTo(_stepupLastBlockedPos) < 0.01f)
			return;

		Vector3 inputDir = hasInput
			? (Transform.Basis * wishLocal.Normalized())
			: horizVel.Normalized();
		inputDir.Y = 0f;
		if (inputDir.LengthSquared() < 0.0001f) return;
		inputDir = inputDir.Normalized();
		float testSpeed = Mathf.Max(horizVel.Length(), ConVars.Sv.WalkSpeed);
		Vector3 horizMove = inputDir * testSpeed * dt * 2f;
		Transform3D startTrans = GlobalTransform;

		if (!TestMove(startTrans, horizMove)) return;

		Vector3 upMove = new Vector3(0f, StepMaxHeight, 0f);
		if (TestMove(startTrans, upMove))
		{
			Dbg.Print($"[stepup] BLOCKED — no headroom (ceiling above, StepMaxHeight={StepMaxHeight:F2}m)");
			_stepupLastBlockedPos = GlobalPosition;
			_stepupLastBlockedTick = CurrentTick;
			return;
		}

		Transform3D elevated = startTrans.Translated(upMove);
		if (TestMove(elevated, horizMove))
		{
			if (Dbg.Enabled && Time.GetTicksMsec() - _lastStepupBlockedLogMs > 1000)
			{
				_lastStepupBlockedLogMs = Time.GetTicksMsec();
				Vector3 dbgProbeFrom = startTrans.Origin + inputDir * (CapsuleRadius + 0.1f) + Vector3.Up * 5f;
				_rayQuery.From = dbgProbeFrom;
				_rayQuery.To = dbgProbeFrom + Vector3.Down * 10f;
				if (GetWorld3D().DirectSpaceState.IntersectRayInto(_rayQuery, _rayResult))
				{
					float h = _rayResult.GetPosition().Y - startTrans.Origin.Y;
					if (h > StepMaxHeight && h < 2.0f)
						Dbg.Print($"[stepup] BLOCKED — obstacle height ≈ {h:F2}m → crouch-jump (up to ~{MantleMinHeight:F1}m) or mantle (up to {MantleMaxHeight:F2}m) required.");
				}
			}
			_stepupLastBlockedPos = GlobalPosition;
			_stepupLastBlockedTick = CurrentTick;
			return;
		}

		Vector3 fwd = inputDir;
		Vector3 probeFrom = startTrans.Origin + fwd * (CapsuleRadius + 0.1f)
			+ new Vector3(0f, StepMaxHeight + 0.15f, 0f);
		var space = GetWorld3D().DirectSpaceState;
		_rayQuery.From = probeFrom;
		_rayQuery.To = probeFrom + new Vector3(0f, -(StepMaxHeight + 0.35f), 0f);
		if (!space.IntersectRayInto(_rayQuery, _rayResult))
		{
			if (Dbg.Enabled && Time.GetTicksMsec() - _lastStepupBlockedLogMs > 1000)
			{
				_lastStepupBlockedLogMs = Time.GetTicksMsec();
				Dbg.Print("[stepup] no step surface detected → no lift");
			}
			_stepupLastBlockedPos = GlobalPosition;
			_stepupLastBlockedTick = CurrentTick;
			return;
		}
		float actualStep = _rayResult.GetPosition().Y - startTrans.Origin.Y;
		if (Mathf.Abs(actualStep) < 0.05f) return;
		if (actualStep <= 0.02f || actualStep > StepMaxHeight)
		{
			if (Dbg.Enabled && Time.GetTicksMsec() - _lastStepupBlockedLogMs > 1000)
			{
				_lastStepupBlockedLogMs = Time.GetTicksMsec();
				Dbg.Print($"[stepup] obstacle {actualStep:F2}m outside [0.02..{StepMaxHeight:F2}m] → no lift (jump/mantle required)");
			}
			_stepupLastBlockedPos = GlobalPosition;
			_stepupLastBlockedTick = CurrentTick;
			return;
		}
		GlobalPosition += new Vector3(0f, actualStep, 0f);
		var v = Velocity;
		if (v.Y < 0f) v.Y = 0f;
		float stepPen = ConVars.Sv.StepUpSpeedPenalty * Mathf.Clamp(actualStep / Mathf.Max(0.01f, StepMaxHeight), 0f, 1f);
		if (stepPen > 0f) { v.X *= 1f - stepPen; v.Z *= 1f - stepPen; }
		Velocity = v;
		if (Dbg.Enabled && Time.GetTicksMsec() - _lastStepupSuccessLogMs > 200)
		{
			_lastStepupSuccessLogMs = Time.GetTicksMsec();
			Dbg.Print($"[stepup] +{actualStep:F3}m (ground threshold) | speed={horizVel.Length():F1}");
		}
	}

	/// <summary>Auto-mantle: when airborne with forward wish input and crouch held and an obstacle with a
	/// reachable flat top is ahead, lerps the player onto the top over <see cref="MantleDuration"/>. The crouch
	/// gate prevents unintended mantles on run-jumps.</summary>
	private void TryMantle()
	{
		if (_isMantling) return;
		if (!Movement.IsAirborne) return;
		if (Velocity.Y < -0.5f) return;
		if (Movement.LastWishDir.LengthSquared() < 0.01f) return;
		if (!_lastMovementInput.CrouchHeld) return;

		Vector3 forward = -Transform.Basis.Z;

		Vector3 chest = GlobalPosition + new Vector3(0f, StandHeight * 0.5f, 0f);
		var space = GetWorld3D().DirectSpaceState;

		_rayQuery.CollisionMask = 1u;
		_rayQuery.From = chest;
		_rayQuery.To = chest + forward * MantleReach;
		if (!space.IntersectRayInto(_rayQuery, _rayResult)) return;

		Vector3 fwdNormal = _rayResult.GetNormal();
		if (Mathf.Abs(fwdNormal.Y) > 0.4f) return;
		Vector3 fwdHit = _rayResult.GetPosition();

		Vector3 topPos = default;
		float heightDiff = 0f;
		bool foundTop = false;
		float lastRejectedDiff = float.NaN;
		foreach (float fwdOff in _mantleForwardOffsets)
		{
			Vector3 above = fwdHit + forward * fwdOff + new Vector3(0f, MantleMaxHeight, 0f);
			_rayQuery.From = above;
			_rayQuery.To = above + new Vector3(0f, -MantleMaxHeight * 1.5f, 0f);
			if (!space.IntersectRayInto(_rayQuery, _rayResult)) continue;
			if (_rayResult.GetNormal().Y < 0.7f) continue;
			Vector3 p = _rayResult.GetPosition();
			float hd = p.Y - GlobalPosition.Y;
			if (hd < MantleMinHeight || hd > MantleMaxHeight)
			{
				lastRejectedDiff = hd;
				continue;
			}
			if (!foundTop || hd < heightDiff)
			{
				topPos = p; heightDiff = hd;
				foundTop = true;
			}
		}
		if (!foundTop)
		{
			if (Dbg.Enabled && !float.IsNaN(lastRejectedDiff))
			{
				string reason = lastRejectedDiff < MantleMinHeight
					? $"below mantle-min ({MantleMinHeight:F2}m) → crouch-jump physics suffices"
					: $"above mantle-max ({MantleMaxHeight:F2}m) → obstacle too tall to climb";
				Dbg.Print($"[mantle] REJECTED heightDiff={lastRejectedDiff:F2}m → {reason} | playerY={GlobalPosition.Y:F2}");
			}
			return;
		}

		Transform3D testTrans = new(GlobalTransform.Basis, topPos + new Vector3(0f, 0.05f, 0f));
		if (TestMove(testTrans, new Vector3(0f, StandHeight, 0f)))
		{
			Dbg.Print($"[mantle] REJECTED capsule blocked at top | heightDiff={heightDiff:F2}m topY={topPos.Y:F2}");
			return;
		}

		Vector3 landingPos = topPos;
		Vector3 inwardProbe = topPos + forward * (CapsuleRadius * 0.5f) + new Vector3(0f, 0.3f, 0f);
		_rayQuery.From = inwardProbe;
		_rayQuery.To = inwardProbe + new Vector3(0f, -0.6f, 0f);
		if (space.IntersectRayInto(_rayQuery, _rayResult)
			&& _rayResult.GetNormal().Y >= 0.7f
			&& Mathf.Abs(_rayResult.GetPosition().Y - topPos.Y) < 0.15f)
		{
			landingPos = _rayResult.GetPosition();
		}

		Dbg.Print($"[mantle] FIRED (climb) heightDiff={heightDiff:F2}m | topY={topPos.Y:F2} playerY={GlobalPosition.Y:F2} → target=({landingPos.X:F2},{landingPos.Y:F2},{landingPos.Z:F2})");

		_isMantling = true;
		_mantleStart = GlobalPosition;
		_mantleTarget = landingPos + new Vector3(0f, 0.05f, 0f);
		_mantleTimer = MantleDuration;
		Velocity = Vector3.Zero;
		_mantleReconcileBlockUntilTick = CurrentTick + (uint)Mathf.CeilToInt(MantleDuration * TickRate) + 30;
	}

	/// <summary>Per-tick smoothstep lerp during an active mantle.</summary>
	private void StepMantle(float dt)
	{
		_mantleTimer -= dt;
		if (_mantleTimer <= 0f)
		{
			GlobalPosition = _mantleTarget;
			Velocity = Vector3.Zero;
			_isMantling = false;
			if (IsLocalPlayer) Prediction.Clear();
			return;
		}
		float progress = 1f - (_mantleTimer / MantleDuration);
		float eased = progress * progress * (3f - 2f * progress);
		GlobalPosition = _mantleStart.Lerp(_mantleTarget, eased);
		Velocity = Vector3.Zero;
	}

	/// <summary>Detects floor transitions (touchdown and lift-off), triggers landing animation and audio,
	/// and broadcasts the land event from server authority.</summary>
	private void HandleLandingDetection()
	{
		bool onFloorNow = IsOnFloor();
		if (onFloorNow && !_wasOnFloor)
		{
			if (!_isReplaying)
				OnLandEvent(Mathf.Max(0f, -_preMoveVelocityY));
		}
		else if (!onFloorNow && _wasOnFloor)
		{
			Dbg.Print($"[floor] left — airborne | vY={Velocity.Y:F1}");
		}
		_wasOnFloor = onFloorNow;
	}

	/// <summary>Handles the jump-edge animation trigger, audio, and the server jump event broadcast.</summary>
	private void HandleJumpAnimation()
	{
		if (Movement.DidJumpThisFrame && !_isReplaying)
			OnJumpEvent();
		if (Movement.DidWallJumpThisFrame && Dbg.Enabled)
			Dbg.Print($"[walljump] vY={Velocity.Y:F2} | horizSpeed={Movement.HorizontalSpeed:F1}");
	}

	/// <summary>Step-edge event from the deterministic footstep cadence. ServerPlayer broadcasts the step;
	/// LocalPlayer plays the audio. Base/puppet no-op. The probed ground/material are passed in.</summary>
	protected virtual void OnFootstepEvent(HitInfo ground, StringName material) { }

	/// <summary>Land-edge event. ServerPlayer overrides to broadcast the land impact; LocalPlayer overrides
	/// to play the landing audio. Base/puppet no-op. Replay-gated by the caller.</summary>
	protected virtual void OnLandEvent(float impact) { }

	/// <summary>Jump-edge event. ServerPlayer overrides to broadcast the jump; LocalPlayer overrides to play
	/// the jump audio. Base/puppet no-op. Replay-gated by the caller.</summary>
	protected virtual void OnJumpEvent() { }

	/// <summary>Reload-start edge event. ServerPlayer broadcasts the mag-drop. Base/local/puppet no-op (the
	/// local player drops its own mag via the FPS montage). Replay-gated by the caller.</summary>
	protected virtual void OnDropMagEvent() { }

}
