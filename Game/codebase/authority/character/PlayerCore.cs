using Godot;

/// <summary>Player camera view — determines which Camera3D is currently active.</summary>
public enum ViewMode { Fps, Tps, Disabled }

/// <summary>Shared player simulation: movement, hitscan, mantle, crouch, footsteps, grenades, plus
/// puppet/server visual hooks. <see cref="LocalPlayer"/> derives from this and adds the local-only
/// camera, mouse-look and aim-guide logic.</summary>
public partial class PlayerCore : ServerBaseCharacter
{
	[Export] public Node3D HeadPitch;
	[Export] public LocalAnimation WeaponHolder;
	[Export] public new CollisionShape3D BodyCollision;
	public PlayerAudio Audio { get; private set; }

	/// <summary>Server agents go on layer 5, all other player visuals on layer 2 (Characters).</summary>
	protected override void ConfigureCollisionLayers()
	{
		if (IsServerAgent)
		{
			CollisionLayer = 1u << 4;
			CollisionMask = 1u | (1u << 4);
		}
		else
		{
			CollisionLayer = 1u << 1;
			CollisionMask = 1u | (1u << 1);
		}
	}

	[ExportGroup("View")]
	[Export] public ViewMode ViewMode = ViewMode.Fps;
	[Export] public Node3D TpsVisual;

	[ExportGroup("Firing")]
	[Export] public bool CanFire = true;
	[Export] public float HitscanRange = 200f;
	[Export] public uint HitscanMask = 1;

	private const float MantleMinHeight = 1.0f;
	private const float MantleMaxHeight = 1.75f;
	private const float MantleReach = 0.7f;
	private const float MantleDuration = 0.35f;

	/// <summary>True only on <see cref="LocalPlayer"/> instances.</summary>
	public virtual bool IsLocalPlayer => false;
	/// <summary>True on <see cref="ServerPlayer"/> and <see cref="ServerBotPlayer"/> instances.</summary>
	public virtual bool IsServerAgent => false;
	/// <summary>True when this PlayerCore instance is a puppet visual — set externally by the
	/// <see cref="PuppetPlayer"/> wrapper before AddChild. Stays mutable because the wrapper owns the flag.</summary>
	public bool IsPuppet;
	/// <summary>True when this node broadcasts server-authoritative events (shot, footstep, jump, land, hit).</summary>
	public bool IsServerAuthority => IsServerAgent;

	public bool PuppetIsAirborne;
	public bool PuppetIsSprinting;
	public bool PuppetIsReloading;
	public bool PuppetIsInspecting;
	/// <summary>0 = weapon, 1 = grenade. Written by PuppetPlayer from Snapshot.ActiveSlot — without it the
	/// UpperBodyMix gate would stay at 0 because _activeSlot never advances on a puppet (no FixedTick) and
	/// WeaponHolder?.ActiveWeapon is null. The puppet would then show no weapon-hold pose or ADS blend.</summary>
	public byte PuppetActiveSlot;
	/// <summary>Spine twist: view yaw minus body yaw (radians). UpdateTpsBodyAim applies this to the aim
	/// bone so the upper body follows the look direction while the body only catches up past 90 degrees delta.</summary>
	public float PuppetSpineTwist;

	private readonly FootstepController _footstepLogic = new();

	private readonly GrenadeController _grenade = new();
	private int _activeSlot;

	[ExportGroup("TPS Animation Tree")]
	[Export] public AnimationTree TpsAnimTree;
	[Export] public float TpsBlendReferenceSpeed = 5.0f;
	[Export] public float TpsWalkRunThreshold = 3.0f;
	[Export] public float TpsRunSprintThreshold = 6.0f;
	[Export] public float TpsWalkAnimNaturalSpeed = 2.0f;
	[Export] public float TpsRunAnimNaturalSpeed = 5.0f;
	[Export] public float TpsSprintAnimNaturalSpeed = 6.0f;
	[Export] public float TpsCrouchAnimNaturalSpeed = 1.5f;
	[Export] public float TpsAnimTimeScaleMin = 0.5f;
	[Export] public float TpsAnimTimeScaleMax = 2.5f;
	[Export] public float TpsBlendSmoothRate = 6f;
	[Export] public float TpsBlendStopRate = 20f;
	/// <summary>Grace period before the air state triggers. Prevents brief floor losses (such as a crouch
	/// capsule resize snap) from throwing the character into the fall animation.</summary>
	[Export] public float TpsAirGraceTime = 0.08f;
	[Export] public float TpsHeavyLandThreshold = 8.0f;
	[Export(PropertyHint.Range, "0.1,1.0,0.05")] public float TpsLandLeadFactor = 0.55f;
	[Export] public float TpsLandRayMaxDist = 5.0f;
	[Export] public float TpsLandFallbackDuration = 0.5f;

	private Vector2 _tpsBlendSmoothed;

	/// <summary>Mantle: three forward offsets for the down-raycast scan, pre-allocated to avoid per-tick allocations.</summary>
	private static readonly float[] _mantleForwardOffsets = { 0.08f, 0.18f, 0.35f };
	/// <summary>Lag compensation: cached exclude array (shooter's body RID), clear+refill per shot.</summary>
	private Godot.Collections.Array<Rid> _lagCompExcludes;
	/// <summary>Lag compensation: target list für den manuellen Bone-Cast (Hitbox-Node + rewound World-
	/// Transform + Shape3D). Per-Shot rebuilt aus BoneHistory der anderen Peers.</summary>
	private System.Collections.Generic.List<(Node3D hitbox, Transform3D worldXform, Shape3D shape)> _boneCastTargets;

	private static readonly StringName _aIsCrouching = "parameters/Locomotion/conditions/is_crouching";
	private static readonly StringName _aIsNotCrouching = "parameters/Locomotion/conditions/is_not_crouching";
	private static readonly StringName _aIsRunning = "parameters/Locomotion/conditions/is_running";
	private static readonly StringName _aIsNotRunning = "parameters/Locomotion/conditions/is_not_running";
	private static readonly StringName _aIsSprinting = "parameters/Locomotion/conditions/is_sprinting";
	private static readonly StringName _aIsNotSprinting = "parameters/Locomotion/conditions/is_not_sprinting";
	private static readonly StringName _aIsInAir = "parameters/Locomotion/conditions/is_in_air";
	private static readonly StringName _aIsOnFloor = "parameters/Locomotion/conditions/is_on_floor";
	internal static readonly StringName _aJumpStart = "parameters/JumpStart/request";
	internal static readonly StringName _aJumpLand = "parameters/JumpLand/request";
	internal static readonly StringName _aJumpLandHeavy = "parameters/JumpLandHeavy/request";
	private static readonly StringName _aWalkBlend = "parameters/Locomotion/Walk/blend_position";
	private static readonly StringName _aRunBlend = "parameters/Locomotion/Run/blend_position";
	private static readonly StringName _aCrouchBlend = "parameters/Locomotion/Crouch/blend_position";
	private static readonly StringName _aSprintBlend = "parameters/Locomotion/Sprint/blend_position";
	private static readonly StringName _aTimeScale = "parameters/TimeScale/scale";
	private static readonly StringName _aAimStancePose = "parameters/AimStancePose/blend_position";
	private static readonly StringName _aAdsPose = "parameters/ADS_Pose/blend_amount";
	private static readonly StringName _aUpperBodyMix = "parameters/UpperBodyMix/blend_amount";
	internal static readonly StringName _aFire = "parameters/Fire/request";
	private static readonly StringName _aReload = "parameters/Reload/request";
	private static readonly StringName _aReloadEmpty = "parameters/ReloadEmpty/request";
	private static readonly StringName _aInspect = "parameters/Inspect/request";

	[ExportGroup("TPS Aim & ADS")]
	[Export] public string TpsAimBoneName = "spine_03";
	[Export(PropertyHint.Range, "0,1,0.05")] public float TpsAimPitchScale = 0.6f;
	/// <summary>Alternative IK path: two Marker3D nodes wired as LookAtModifier3D targets in the weapon
	/// scene. When set, the code positions them per frame in the run/view direction and the
	/// LookAtModifier3D rotates the spine bone. Leave empty when using TpsAimModifier.</summary>
	[Export] public Marker3D AimYawTarget;
	[Export] public Marker3D AimPitchTarget;
	[Export] public float AimMarkerDistance = 2.0f;
	private int _tpsAimBoneIdx = -1;
	private Quaternion _tpsAimBoneRestRot;
	/// <summary>Optional TpsAimModifier child under the skeleton. When present, the preferred race-free
	/// modifier pipeline is used. When null, falls back to direct <see cref="Skeleton3D.SetBonePoseRotation"/>
	/// in <see cref="UpdateTpsBodyAim"/>.</summary>
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
	private float _preMoveVelocityY;
	private Vector3 _prevPhysicsPos;
	private Vector3 _currentPhysicsPos;
	private bool _isMantling;
	private Vector3 _mantleStart;
	private Vector3 _mantleTarget;
	private float _mantleTimer;
	/// <summary>Tick until which reconciliation is blocked (mantle plus grace window). Mantle state is not
	/// checkpointed into the prediction buffer. Without this block, replays during or after a mantle would
	/// abort the lerp and use stale pre-mantle server snapshots as truth, snapping the player back down.
	/// The grace window covers the ~80-150 ms between the client mantle ending and the first server
	/// post-mantle snapshot arriving.</summary>
	private uint _mantleReconcileBlockUntilTick;
	private MovementInput _lastMovementInput;
	/// <summary>Per-tick prediction buffer for reconciliation. Filled only for IsLocalPlayer.</summary>
	public readonly PredictionBuffer Prediction = new();
	/// <summary>Smooth-correction state: when ApplyServerCorrection detects a small drift it is faded out
	/// through this offset instead of snapping.</summary>
	private Vector3 _correctionPending;
	/// <summary>
	/// Visual error after a replay: the difference between the previously visible position and the new
	/// replay position. <see cref="_Process"/> adds this to GlobalPosition and fades it per tick. The
	/// user sees no snap — the physics position is authoritative-correct, the visual position glides
	/// smoothly toward it.
	/// </summary>
	private Vector3 _visualErrorOffset;
	/// <summary>Active bleed-out rate (1/sec) applied to <see cref="_visualErrorOffset"/> each tick.
	/// Updated by <see cref="ApplyServerCorrection"/> based on drift magnitude: small drifts use
	/// <see cref="ClConVars.ReconBleedNormal"/>, large drifts use <see cref="ClConVars.ReconBleedLarge"/>
	/// for a softer recovery feel on rubber-bands. Reset in <see cref="ResetInterpToCurrentPos"/>.</summary>
	private float _activeBleedRate = 6.5f;

	/// <summary>Wallclock (<see cref="Time.GetTicksUsec"/>) at the start of the current FixedTick.
	/// Subtick-fire uses this as the base from which the fire-press wallclock is offset to derive a
	/// fractional in-tick position. Captured at the top of <see cref="_PhysicsProcess"/>.</summary>
	protected ulong _tickStartUsec;
	/// <summary>Wallclock of the most recent fire-press edge (transition from not-pressed to pressed).
	/// Written by <see cref="LocalPlayer"/>'s <c>_Input</c> handler the moment the fire action edge
	/// fires (i.e. with sub-tick precision), read by <see cref="SendNetInput"/> to compute
	/// <see cref="Packets.InputPacket.FireSubTick"/>. Stays 0 on server agents and puppets — no
	/// observable side-effects there.</summary>
	protected ulong _lastFirePressUsec;

	protected PhysicsRayQueryParameters3D _rayQuery;
	protected Godot.Collections.Array<Rid> _selfExclude;

	/// <summary>Initializes physics tuning, audio banks, hitbox rig, and the third-person aim setup. Server
	/// agents take an early-out and skip all visual-only setup.</summary>
	public override void _Ready()
	{
		Engine.PhysicsTicksPerSecond = TickRate;
		_fixedDt = 1f / TickRate;

		_selfExclude = new Godot.Collections.Array<Rid> { GetRid() };
		_rayQuery = new PhysicsRayQueryParameters3D { Exclude = _selfExclude };

		SetupCapsule();
		SetupHeadPitch();
		ConfigureWeaponHolder();

		FloorMaxAngle = Mathf.DegToRad(FloorMaxAngleDeg);
		FloorSnapLength = FloorSnapDist;
		FloorBlockOnWall = true;
		FloorStopOnSlope = false;
		_movement.Stamina = ConVars.Sv.MaxStamina;
		_movement.ResetSpawnConsumables();
		// ServerAgent hat keinen WeaponHolder (server_*.tscn = kein FPS-Subtree). Fallback auf M4A1
		// damit der MovementController eine Mag hat zum Feuern — sonst CurrentMag=0 → Schuss-Check
		// "hasAmmo" failt → kein Damage trotz Client-Tracer.
		WeaponStats spawnWeapon = WeaponHolder?.ActiveWeapon ?? (IsServerAgent ? ConVars.Weapons.M4A1 : null);
		_movement.InitializeAmmo(spawnWeapon);
		if (spawnWeapon != null) _movement.FireMode = spawnWeapon.FireMode;
		GrenadeTrajectory.Gravity = GrenadeTrajectory.BaseGravity / Mathf.Max(0.1f, ConVars.Sv.GrenadeRangeScale);
		Audio = new PlayerAudio(
			GetNodeOrNull<FootstepAudio>("FootstepAudio"),
			GetNodeOrNull<WeaponAudio>("WeaponAudio"));
		Audio.Configure(IsLocalPlayer, WeaponHolder?.ActiveWeapon);
		// First-frame stutter prevention: pre-fire all action-audio paths silently +
		// off-screen so the lazy-init of clip resources + AudioStreamPlayer3D's first-
		// Play() setup happens at spawn (already a loading-friendly moment) instead of
		// during the first real jump / land / shot. Without this, the first jump shows
		// a ~50-100ms hitch on the proc-time graph as Godot materialises the audio
		// decoders + pool slots. loudness01=0 -> -80 dB; offscreen Y -10000 -> inaudible.
		if (IsLocalPlayer && Audio != null)
		{
			Vector3 hiddenPos = new Vector3(0f, -10000f, 0f);
			StringName warmMat = (StringName)"default";
			Audio.PlayStep(hiddenPos, warmMat, 0f, false, sprinting: false);
			Audio.PlayStep(hiddenPos, warmMat, 0f, false, sprinting: true);
			Audio.PlayJump(hiddenPos, warmMat, 0f, false);
			Audio.PlayLand(hiddenPos, warmMat, 0f, false);
			// Pre-fire the jump-animation one-shot too - its first Set on the AnimTree
			// can trigger node-graph compilation. Crouch / sprint state at spawn is
			// neutral so the one-shot doesn't visibly translate the body.
			TpsAnimTree?.Set(_aJumpStart, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		}
		_wasOnFloor = IsOnFloor();

		if (IsServerAgent)
		{
			CollisionLayer = 1u << 4;
			CollisionMask = 1u | (1u << 4);
		}
		else
		{
			CollisionLayer = 1u << 1;
			CollisionMask = 1u | (1u << 1);
		}
		HitscanMask = 1u | HitboxRig.Layer;

		// HitboxRig MUSS für ALLE Modes gebaut werden (LocalPlayer, Puppet, ServerAgent):
		//   - LocalPlayer + Puppet: Client-side Tracer/Decal-Cast trifft die Hitbox-Layer
		//   - ServerAgent: Authority-Lag-Comp-Cast in RunAuthoritativeHitscan trifft die Server-Hitboxen
		//     vom OPFER. Wenn ServerAgents keine Hitboxen haben → Cast geht durch Spieler durch → Wand →
		//     kein Damage, kein Kill. Genau der Bug warum Bots nicht starben!
		// VOR dem Early-Return für ServerAgent damit das auch für headless server greift.
		if (TpsSkeleton != null)
		{
			_hitboxRig = new HitboxRig { Skeleton = TpsSkeleton, Name = "HitboxRig" };
			AddChild(_hitboxRig);
			_hitboxRig.Build();
			// Bone-Pose-Rewind nur für ServerAgent (Lag-Comp ist authority-side). Snapshot der Hitbox-
			// GlobalTransforms pro Tick via NetServer.PushPositionsToRewind → RunAuthoritativeHitscan
			// rewindt auf den lag-comp-tick für korrekte Headshot-Erkennung bei animierten Targets.
			if (IsServerAgent && _hitboxRig.HitboxNodes.Count > 0)
			{
				BoneHistory = new BonePoseRewindBuffer();
				BoneHistory.Init(_hitboxRig.HitboxNodes.Count);
			}
		}

		// AimModifier-Setup MUSS für ALLE Modes laufen (LocalPlayer, Puppet, ServerAgent) — der Modifier
		// pitcht/twistet die Spine-Bones basierend auf HeadPitch (vertical) + SpineTwist (horizontal).
		// Wenn er auf ServerAgent fehlt, bleibt die Spine in der reinen Animation-Pose ohne Aim → Server-
		// Hitboxen (= Hand/Arm-Bones inkl. Chest) sitzen ANDERS als Client-Puppet die mit Aim rendered.
		// Resultat: Schuss auf sichtbaren Puppet trifft daneben auf Server-Cast. VOR ServerAgent-Early-
		// Return damit auch headless server greift.
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
				_tpsAimBoneIdx = TpsSkeleton.FindBone(TpsAimBoneName);
				if (_tpsAimBoneIdx < 0)
					GD.PushWarning($"[PlayerCore] TpsAimBoneName='{TpsAimBoneName}' not in skeleton — pitch/twist disabled");
				else
				{
					_tpsAimBoneRestRot = TpsSkeleton.GetBonePoseRotation(_tpsAimBoneIdx);

					foreach (var ch in TpsSkeleton.GetChildren())
					{
						if (ch is LookAtModifier3D look)
							look.Active = false;
					}

					AimModifier = new TpsAimModifier
					{
						Name = "aim_modifier_auto",
						HeadPitch = HeadPitch,
						AimBoneName = TpsAimBoneName,
						PitchScale = TpsAimPitchScale,
					};
					TpsSkeleton.AddChild(AimModifier);
					Dbg.Print($"[PlayerCore] Auto-Setup TpsAimModifier on {TpsAimBoneName} (rig-independent)");
				}
			}
		}

		if (IsServerAgent)
		{
			// KRITISCH: TpsAnimTree muss aktiv sein damit die Skeleton-Bones in die animierte Pose
			// (Idle/Crouch/Aim) gehen. Sonst bleiben sie an REST-POSE (= T-Pose) und die Hitboxen
			// sitzen auf der ScheneStandard-Position — Server-Cast trifft nie das was der Client-Puppet
			// (animiert) zeigt. CallbackModeProcess = Physics damit der AnimMixer im selben 128Hz-Takt
			// wie der Server-Cast advance't (deterministisch für Bone-Pose-Lag-Comp).
			if (TpsAnimTree != null)
			{
				TpsAnimTree.Active = true;
				TpsAnimTree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Physics;
			}
			ViewMode = ViewMode.Disabled;
			ApplyViewMode();
			DisableExpensiveSubtreeProcessing();
			_prevPhysicsPos = GlobalPosition;
			_currentPhysicsPos = GlobalPosition;
			return;
		}

		if (TpsAnimTree != null) TpsAnimTree.Active = true;

		// HitboxRig wurde bereits weiter oben (vor dem ServerAgent-Early-Return) gebaut.

		ApplyViewMode();

		_prevPhysicsPos = GlobalPosition;
		_currentPhysicsPos = GlobalPosition;
	}

	/// <summary>Active camera used by logic reads (shoot origin, grenade spawn, HUD FOV). Default null
	/// (puppets and server agents have no cameras). <see cref="LocalPlayer"/> overrides this.</summary>
	public virtual Camera3D ActiveCamera => null;

	/// <summary>Aggressive performance pass for puppets and server agents: disables expensive per-tick
	/// nodes only the local player needs.
	///
	/// Trick: ProcessMode = Disabled propagates recursively to children, so disabling the FPS subtree at
	/// head_pitch turns off every per-tick callback in the FPS stack (viewmodel layer, viewmodel camera,
	/// viewmodel light, ViewmodelLightSampler, weapon holder, fps_camera) with a single statement.
	/// HeadPitch.Rotation still remains writable (it is a property, not a process callback).</summary>
	private void DisableExpensiveSubtreeProcessing()
	{
		// HeadPitch wird auf ServerAgent NICHT mehr gefreed: BuildMovementInputFromNet/BuildFireInput
		// schreiben + lesen HeadPitch.Rotation.X für die ViewPitch (Shoot-Direction). Ohne den Node
		// schießt ServerAgent immer horizontal egal wo der Spieler hinguckt → kein Headshot, oft
		// kein Body-Hit. Nur WeaponHolder darf weg (server hat keinen FPS-Subtree).
		if (IsServerAgent)
		{
			WeaponHolder = null;
		}

		if (IsServerAgent)
		{
			// TpsAnimTree bleibt aktiv (war vorher .Active=false): die Skeleton-Bones müssen aus T-Pose in
			// die animierte IDLE/Crouch/Aim-Pose gehen damit die Hitboxen mit dem CLIENT-Puppet (auch
			// animiert) übereinstimmen. Sonst trifft ein Chest-Shot manchmal die Hand weil Hände in
			// T-Pose seitlich auf Hüfthöhe stehen, im IDLE aber vorne am Waffenhalt sind.
			// AnimTree.Set-Calls passieren in UpdateTpsAnimations das jetzt auch für ServerAgent läuft
			// (siehe _PhysicsProcess).
			if (TpsSkeleton != null)
			{
				foreach (Node ch in TpsSkeleton.GetChildren())
					if (ch is MeshInstance3D) ch.QueueFree();
			}
			if (TpsVisual != null) TpsVisual.Visible = false;
		}
	}

	/// <summary>Toggles the render camera and visual roots according to <see cref="ViewMode"/>. The default
	/// implementation handles only the puppet/server paths; <see cref="LocalPlayer"/> overrides it with
	/// the full FPS/TPS camera switch and shadow tuning.</summary>
	protected virtual void ApplyViewMode()
	{
		if (IsPuppet)
		{
			if (TpsVisual != null)
			{
				SetLayers(TpsVisual, 1u << 0);
				TpsVisual.Visible = true;
				RestoreFilteredVisibility(TpsVisual);
				SetShadowMode(TpsVisual, GeometryInstance3D.ShadowCastingSetting.On);
			}
			DisableExpensiveSubtreeProcessing();
			return;
		}

		if (IsServerAgent)
		{
			if (TpsVisual != null) TpsVisual.Visible = false;
			return;
		}
	}

	/// <summary>Recursively sets the CastShadow flag on every GeometryInstance3D under <paramref name="root"/>.</summary>
	protected static void SetShadowMode(Node root, GeometryInstance3D.ShadowCastingSetting mode)
	{
		if (root is GeometryInstance3D gi) gi.CastShadow = mode;
		foreach (Node child in root.GetChildren()) SetShadowMode(child, mode);
	}

	private static readonly StringName _OrigVisibleMeta = "_lc_orig_visible";

	/// <summary>Restores the original visibility of meshes previously hidden by SetShadowModeFiltered.</summary>
	protected static void RestoreFilteredVisibility(Node root)
	{
		if (root is GeometryInstance3D gi && gi.HasMeta(_OrigVisibleMeta))
		{
			gi.Visible = (bool)gi.GetMeta(_OrigVisibleMeta);
			gi.RemoveMeta(_OrigVisibleMeta);
		}
		foreach (Node child in root.GetChildren()) RestoreFilteredVisibility(child);
	}

	/// <summary>Recursively sets the VisualInstance3D.Layers bitmask on every visual under <paramref name="root"/>.</summary>
	protected static void SetLayers(Node root, uint layers)
	{
		if (root is VisualInstance3D vi) vi.Layers = layers;
		foreach (Node child in root.GetChildren()) SetLayers(child, layers);
	}

	/// <summary>FPS-mode body-shadow filter: only lower-body meshes cast (pants, belt, pouches), the upper
	/// body (shirt, plate carrier, sleeves, gloves, head gear, banger) is hidden entirely (Visible=false).
	/// Setting CastShadow=Off alone leaves the mesh visible. Prevents the shadow of forward-extended arms
	/// from projecting too far into the crosshair.</summary>
	protected static void SetShadowModeFiltered(Node root)
	{
		if (root is GeometryInstance3D gi)
		{
			if (!gi.HasMeta(_OrigVisibleMeta)) gi.SetMeta(_OrigVisibleMeta, gi.Visible);
			bool origVisible = (bool)gi.GetMeta(_OrigVisibleMeta);

			string n = gi.Name;
			bool isUpper = n.Contains("Shirt") || n.Contains("PlateCarrier") || n.Contains("Sleeve")
						 || n.Contains("Gloves") || n.Contains("HeadGear") || n.Contains("Banger");

			if (!origVisible || isUpper)
			{
				gi.Visible = false;
				gi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			}
			else
			{
				gi.Visible = true;
				gi.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
			}
		}
		foreach (Node child in root.GetChildren()) SetShadowModeFiltered(child);
	}

	/// <summary>Authority position for server snapshots and reconciliation — always the real physics state,
	/// never the visually lerped value from _Process. Without this override the snapshot read would pick
	/// up the interpolated visual position during the inter-tick window and produce drift.</summary>
	public override Vector3 AuthorityPosition { get => _currentPhysicsPos; set { } }

	/// <summary>Ticks that have passed since spawn/respawn. Reconciliation is skipped while inside the
	/// settle window (30 ticks).</summary>
	private int _ticksSinceSpawn;
	private const int SpawnSettleTicks = 30;

	/// <summary>True while a server reconciliation is currently replaying the last ticks. Side effects
	/// (audio, tracers, decals, net-input send) are skipped during a replay — they already ran during the
	/// original tick.</summary>
	private bool _isReplaying;

	// === Bone-Pose Lag-Comp ===

	/// <summary>Vom NetServer pro Tick gerufen (nach BoneAttachment3D-Updates) — snapshotted alle
	/// Hitbox-GlobalTransforms in den Ring-Buffer. Nur ServerAgent.</summary>
	public void PushBoneHistory(uint tick)
	{
		if (BoneHistory == null || _hitboxRig == null) return;
		BoneHistory.Push(tick, _hitboxRig.CollisionShapes);
	}

	/// <summary>Resets the render-interp state after a teleport (spawn or respawn) so the first frame does
	/// not lerp from the old position. Also clears any in-flight visual reconciliation offset so a fresh
	/// spawn does not bleed an artefact from the pre-teleport state.</summary>
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
		using var _prof = IsServerAgent ? MiniProfiler.SampleServer("PlayerCore._PhysicsProcess") : MiniProfiler.SampleClient("PlayerCore._PhysicsProcess (Local)");

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

		if (!IsServerAgent)
		{
			using (MiniProfiler.SampleClient("PlayerCore.UpdateAimGuide")) UpdateAimGuide();
			using (MiniProfiler.SampleClient("PlayerCore.UpdateTpsCameraCollision")) UpdateTpsCameraCollision();
			using (MiniProfiler.SampleClient("PlayerCore.UpdateTpsAnimations")) UpdateTpsAnimations();
			using (MiniProfiler.SampleClient("PlayerCore.UpdateTpsBodyAim")) UpdateTpsBodyAim();
		}
		else
		{
			// ServerAgent: Animation-Tree muss laufen damit die Skeleton-Bones aus T-Pose in die
			// IDLE/Aim/Crouch-Pose gehen — sonst sitzen die Hitboxen am T-Pose-Standort (= Arme zur
			// Seite gestreckt, Hände auf Hüfthöhe) während der Client-Puppet animiert ist. Resultat
			// war: Body-Shot trifft die Hand-Hitbox weil Hände dort sind wo der Server T-Pose-mässig
			// die Arme stehen lässt. UpdateTpsAnimations sync't die AnimTree-Parameter (running/crouch/
			// adsBlend etc), UpdateTpsBodyAim setzt den Spine-Pitch via TpsAimModifier (= folgt
			// Client-ViewPitch der via NetInputSource im HeadPitch landet).
			UpdateTpsAnimations();
			UpdateTpsBodyAim();
		}
	}

	/// <summary>Per-frame visual interpolation between the last two physics ticks. Local player only.</summary>
	public override void _Process(double delta)
	{
		if (!IsLocalPlayer) return;
		float fraction = (float)Engine.GetPhysicsInterpolationFraction();
		GlobalPosition = _prevPhysicsPos.Lerp(_currentPhysicsPos, fraction) + _visualErrorOffset;
	}

	/// <summary>Server-replayable tick step. Called with a constant <paramref name="dt"/> = 1/TickRate. Only
	/// code that must run on the server too belongs here (movement, fire, stamina, etc.). Pure-visual side
	/// effects (HUD updates, crouch height for the camera) are fine because they have no gameplay impact.</summary>
	private void FixedTick(float dt)
	{
		_currentTick++;
		_ticksSinceSpawn++;

		// Death-State: kein Movement, kein Fire, keine Animation-Driver. Velocity bleibt 0 (von IsDead-
		// Setter), Collision ist auch null gedreht damit lebende Spieler durchgehen können. Sim wird beim
		// Respawn re-aktiviert (IsDead → false in DoRespawn).
		if (IsDead)
		{
			Velocity = Vector3.Zero;
			return;
		}

		if (_isMantling)
		{
			StepMantle(dt);
			ApplyCrouchHeight();
			SyncWeaponHolder();
			return;
		}

		// Sub-Sampling der FixedTick-Phasen — wir wollen sehen WELCHE Phase bei Collision den 1.35ms
		// Peak verursacht. Wahrscheinlich MoveAndSlide oder TryStepUp wegen Multi-Iteration Physics-
		// Queries an Wänden. Audio + Footsteps können bei Material-Wechsel auch teuer sein.
		MovementInput moveIn;
		using (MiniProfiler.SampleClient("PlayerCore.BuildMovementInput")) moveIn = BuildMovementInput(dt);
		_lastMovementInput = moveIn;
		_movement.Velocity = Velocity;
		using (MiniProfiler.SampleClient("PlayerCore.Movement.Step")) _movement.Step(moveIn);
		Velocity = _movement.Velocity;

		FireInput fireIn;
		using (MiniProfiler.SampleClient("PlayerCore.BuildFireInput")) fireIn = BuildFireInput(dt);
		_movement.FireStep(fireIn);
		if (_movement.DidFireThisFrame)
		{
			using (MiniProfiler.SampleClient("PlayerCore.HandleHitscan")) HandleHitscan();
			Dbg.Print($"[fire] tick={_currentTick} shot #{_movement.ShotIndex} ({WeaponHolder?.ActiveWeapon?.Name}) | next in {_movement.FireCooldown * 1000f:0}ms");
		}

		using (MiniProfiler.SampleClient("PlayerCore.HandleGrenades")) HandleGrenades(dt);

		_preMoveVelocityY = Velocity.Y;
		_movement.PreMoveHorizSpeed = new Vector3(Velocity.X, 0f, Velocity.Z).Length();

		using (MiniProfiler.SampleClient("PlayerCore.TryStepUp")) TryStepUp(dt);
		using (MiniProfiler.SampleClient("PlayerCore.MoveAndSlide")) MoveAndSlide();
		using (MiniProfiler.SampleClient("PlayerCore.TryMantle")) TryMantle();

		ApplyCrouchHeight();
		using (MiniProfiler.SampleClient("PlayerCore.HandleLandingDetection")) HandleLandingDetection();
		HandleJumpAnimation();
		using (MiniProfiler.SampleClient("PlayerCore.HandleFootsteps")) HandleFootsteps();
		using (MiniProfiler.SampleClient("PlayerCore.HandleWeaponAudio")) HandleWeaponAudio();
		SyncWeaponHolder();

		if (IsLocalPlayer)
		{
			Prediction.Push(_currentTick, _lastMovementInput, _movement.Snapshot(), GlobalPosition, Velocity);
			LastAppliedInputTick = _currentTick;
		}
		else if (IsServerAgent && NetInputSource.HasValue)
		{
			LastAppliedInputTick = NetInputSource.Value.TickIndex;
		}
		SendNetInput();

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

	/// <summary>Called by <see cref="NetClient"/> after each received snapshot. Compares the server position
	/// at the acked tick with the locally stored prediction. Small drifts are bled out smoothly, large
	/// drifts trigger a full replay with a visual smoothing offset.</summary>
	public void ApplyServerCorrection(uint ackedTick, Vector3 serverPos, Vector3 serverVel)
	{
		if (!IsLocalPlayer || ackedTick == 0u) return;
		if (_ticksSinceSpawn < SpawnSettleTicks) return;
		if (_isMantling || _currentTick < _mantleReconcileBlockUntilTick) return;
		if (!Prediction.TryGet(ackedTick, out var entry)) return;

		Vector3 drift = serverPos - entry.PostPos;
		float driftLen = drift.Length();

		// Epsilon hochgesetzt von 0.02m + vel*dt → 0.06m + vel*dt × 2. Reduce frequency of triggered
		// reconciles drastically — bei Wall-Slide drift'd Position oft 2-5cm wegen Client/Server-
		// Collision-Timing-Differenz, was VOR dem Fix jeden Snapshot (64Hz) Reconcile + 8-tick Replay
		// triggert (= 64*8 = 512 ReplayTicks/sec). Mit 6cm Schwelle nur noch echte Drifts (>5cm
		// off = visible mismatch) korrigiert. Sub-Schwelle bleibt durch normales Forward-Sim corrected.
		float epsilon = 0.06f + Velocity.Length() * _fixedDt * 2f;
		if (driftLen < epsilon) return;

		Vector3 visualPosBefore = _currentPhysicsPos + _visualErrorOffset;

		_movement.Restore(entry.State);
		GlobalPosition = serverPos;
		Velocity = serverVel;
		_movement.Velocity = serverVel;
		_isMantling = false;

		// Zero-alloc replay-loop + Cap auf MaxReplayPerFrame Ticks. Cap=8 (war 32, dann 32 × ~0.15ms
		// = 5ms Spike beim Wall-Collide). Bei N-Tick-Drift wird über mehrere Snapshots verteilt
		// (Snapshots kommen ~64Hz, also 8 Ticks/Snapshot = 64 Ticks/Sekunde Convergence — schneller
		// als jede Bewegung sich akkumulieren kann). Hard-Snap im Visual-Bleed bleibt aktiv.
		const int MaxReplayPerFrame = 8;
		_isReplaying = true;
		try
		{
			int startIdx = Prediction.FindFirstIndexAfter(ackedTick);
			int endIdx = Mathf.Min(Prediction.Count, startIdx + MaxReplayPerFrame);
			for (int i = startIdx; i < endIdx; i++)
			{
				var laterEntry = Prediction.GetAt(i);
				ReplayOneTick(laterEntry.Input);
				Prediction.UpdateEntryState(laterEntry.Tick, _movement.Snapshot(), GlobalPosition, Velocity);
			}
		}
		finally
		{
			_isReplaying = false;
		}

		_prevPhysicsPos = GlobalPosition;
		_currentPhysicsPos = GlobalPosition;
		_correctionPending = Vector3.Zero;

		Vector3 visualDelta = visualPosBefore - GlobalPosition;
		float visualMag = visualDelta.Length();
		float hardSnapThreshold = Mathf.Max(0.01f, ConVars.Cl.ReconSnapThresholdM);
		if (visualMag > hardSnapThreshold)
		{
			_visualErrorOffset = Vector3.Zero;
			_activeBleedRate = Mathf.Max(0.01f, ConVars.Cl.ReconBleedNormal);
		}
		else
		{
			_visualErrorOffset = visualDelta;
			float largeThreshold = Mathf.Max(0f, ConVars.Cl.ReconBleedLargeThresholdM);
			_activeBleedRate = visualMag > largeThreshold
				? Mathf.Max(0.01f, ConVars.Cl.ReconBleedLarge)
				: Mathf.Max(0.01f, ConVars.Cl.ReconBleedNormal);
		}

		if (driftLen > 0.5f && Dbg.Enabled)
			Dbg.Print($"[NetReconcile] REPLAY @ tick={ackedTick} drift={driftLen:F2}m replayed-ticks={Prediction.Count - 1} visualBleed={visualDelta.Length():F2}m");

		NetStats.LastReconcileDriftM = driftLen;
		NetStats.LastReconcileTimeSec = Time.GetTicksMsec() / 1000.0;
		_reconcileCountWindow++;
	}

	private int _reconcileCountWindow;
	private double _reconcileWindowStartSec;

	/// <summary>Server-authoritative hitscan with lag compensation. Other players are temporarily rewound
	/// to their historical positions (the way the shooter saw them at the time of the shot), the raycast
	/// runs, and the positions are restored. On a hit: apply damage, trigger death if HP hits zero, and
	/// broadcast a ShotFired event. Called from both ServerAgent (dedicated) and HostAuthority
	/// (LocalPlayer in listen mode).</summary>
	private void RunAuthoritativeHitscan(PhysicsDirectSpaceState3D space)
	{
		var server = NetMain.Instance?.Server;
		if (server == null) return;
		var myState = server.GetPeerStateForNetId(NetId);
		int rttMs = myState?.LastPingMs ?? 0;
		int halfRttTicks = Mathf.Clamp((rttMs * TickRate) / 2000, 0, 64);
		const int InterpDelayTicks = 6;
		long target = (long)_currentTick - halfRttTicks - InterpDelayTicks;
		uint lagCompTick = (uint)Mathf.Max(0L, target);
		byte fireSubTick = myState != null && myState.HasLatestInput ? myState.LatestInput.FireSubTick : (byte)0;
		float fractionalLagCompTick = (float)lagCompTick + (fireSubTick / 256f);

		// Build target list für manuellen Hitbox-Cast. rewound[i] enthält jetzt die SHAPE-World-Transform
		// (BoneHistory speichert cs.GlobalTransform, nicht hitbox.GlobalTransform) — d.h. kein
		// `* cs.Transform` mehr nötig. NoRewind-Toggle für Debug: nimmt LIVE Shape-Position statt History.
		_boneCastTargets ??= new System.Collections.Generic.List<(Node3D, Transform3D, Shape3D)>();
		_boneCastTargets.Clear();
		bool useRewind = !ConVars.Sv.NoRewind;
		foreach (var other in server.AllPeers)
		{
			if (other == myState) continue;
			if (other.ServerAgent is not PlayerCore otherPc) continue;
			if (otherPc._hitboxRig == null) continue;
			var shapes = otherPc._hitboxRig.CollisionShapes;
			var hitboxes = otherPc._hitboxRig.HitboxNodes;
			Transform3D[] rewound = useRewind
				? (fireSubTick > 0
					? otherPc.BoneHistory?.QueryFractional(fractionalLagCompTick)
					: otherPc.BoneHistory?.Query(lagCompTick))
				: null;
			int n = hitboxes.Count;
			if (useRewind && rewound != null) n = Mathf.Min(n, rewound.Length);
			for (int i = 0; i < n; i++)
			{
				var hb = hitboxes[i];
				var cs = shapes[i];
				if (hb == null || !GodotObject.IsInstanceValid(hb)) continue;
				if (cs?.Shape == null) continue;
				Transform3D worldXform = (useRewind && rewound != null) ? rewound[i] : cs.GlobalTransform;
				_boneCastTargets.Add((hb, worldXform, cs.Shape));
			}
		}

		// World-Cast (Wände/Boden) — Mask nur Layer 1, NICHT die Hitbox-Layer weil der Manual-Cast
		// die Hitboxen handhabt. Exclude shooter's own body (sonst trifft Cast vom Cam-Spawn die
		// eigene Char-Capsule sofort).
		if (_lagCompExcludes == null) _lagCompExcludes = new Godot.Collections.Array<Rid>();
		_lagCompExcludes.Clear();
		_lagCompExcludes.Add(GetRid());

		HitInfo worldHit = Hitscan.CastMulti(space, _movement.LastShotOrigin, _movement.LastShotDirection,
			HitscanRange, _lagCompExcludes, mask: 1u);
		float maxDist = worldHit.Hit ? worldHit.Distance : HitscanRange;

		// Manual Hitbox-Cast — wenn ein Hitbox NÄHER als die Wand ist, gewinnt der Hitbox-Hit.
		HitInfo boneHit = Hitscan.CastVsBoneShapes(_movement.LastShotOrigin, _movement.LastShotDirection,
			_boneCastTargets, maxDist);
		HitInfo lagHit = boneHit.Hit ? boneHit : worldHit;

		// Diagnose-Log gated auf sv_debug_hitboxes — dump pro Schuss was der manuelle Cast sieht,
		// damit "schießt durch" debugged werden kann ohne weitere Iterationen. Inkludiert:
		//   - bone-target count (= 0 heißt BoneHistory leer oder Query null)
		//   - world-cast distance (= maxDist cutoff)
		//   - pro Target: distance Ray-zu-Center (kleinste = beste Aim) und ob's t > maxDist war
		if (ConVars.Sv.DebugHitboxes && !boneHit.Hit)
		{
			System.Text.StringBuilder sb = new();
			sb.Append($"[sv-cast-miss] targets={_boneCastTargets.Count} worldHit={worldHit.Hit} maxDist={maxDist:F2} | ");
			foreach (var (hb, xform, shape) in _boneCastTargets)
			{
				Vector3 toCenter = xform.Origin - _movement.LastShotOrigin;
				float along = toCenter.Dot(_movement.LastShotDirection);
				Vector3 perpendicular = toCenter - _movement.LastShotDirection * along;
				float perpDist = perpendicular.Length();
				sb.Append($"{hb.Name}@dist{along:F1}/perp{perpDist:F2} ");
			}
			Dbg.Print(sb.ToString());
		}

		BaseCharacter victim = lagHit.Hit ? HitboxRig.FindOwner(lagHit.Collider) : null;
		// Server-Damage-Debug: zeigt warum ein Schuss u.U. KEIN Damage applied — Kette ist
		// fire-ready → cast → hit-or-miss → find-victim → apply. Jeder Schritt loggt sich
		// einzeln damit ein Bug an einer Stelle sofort sichtbar ist. Dbg.Print mit Interpolated-
		// StringHandler → String-Interpolation komplett übersprungen wenn Dbg.Enabled false.
		Dbg.Print($"[sv-fire] netId={NetId} tick={_currentTick} origin={_movement.LastShotOrigin:F2} dir={_movement.LastShotDirection:F2} | hit={lagHit.Hit}{(lagHit.Hit ? $" collider={lagHit.Collider?.Name} ownerNetId={victim?.NetId.ToString() ?? "null"} dist={lagHit.Distance:F2}" : "")}");

		// Zusätzlich: dump aktuelle Server-Hitbox-Positionen vom GETROFFENSTEN OTHER-Peer (egal ob
		// Cast getroffen). So sieht man ob die Hitboxen tatsächlich am erwarteten Body-Spot sitzen
		// oder z.B. ans Welt-Origin (= bone-attachment hat nicht aktualisiert).
		if (Dbg.Enabled)
		{
			foreach (var other in server.AllPeers)
			{
				if (other == myState || other.ServerAgent == null) continue;
				if (other.ServerAgent is not PlayerCore otherPc) continue;
				if (otherPc._hitboxRig == null || otherPc._hitboxRig.HitboxNodes.Count == 0) continue;
				var headHb = otherPc._hitboxRig.HitboxNodes[0];   // typisch head (erstes Entry in DefaultSpecs)
				Dbg.Print($"[sv-hitbox] netId={other.NetId} body={other.ServerAgent.GlobalPosition:F2} firstHitbox={headHb.Name} @ {headHb.GlobalPosition:F2}");
			}
		}
		if (victim != null && victim.NetId != NetId && victim.NetId > 0)
		{
			var vs = server.GetPeerStateForNetId(victim.NetId);
			if (vs != null && vs.Hp > 0)
			{
				HitboxGroup group = HitboxRig.ReadGroup(lagHit.Collider);
				// WeaponHolder ist auf ServerAgent null (server_*.tscn = kein FPS-Subtree) → Fallback
				// auf M4A1. Sonst dmg=25 hardcoded für jeden Hit (war Bug: alles −25 statt 120 Headshot).
				var weapon = WeaponHolder?.ActiveWeapon ?? ConVars.Weapons.M4A1;
				int dmg = weapon.DamageFor(group);
				bool isHead = group == HitboxGroup.Head;

				// Armor-Logic: Headshots bypassen Kevlar komplett (CS2-Style → Helm-Mechanik wäre separat).
				// Body: Armor absorbiert 50% des Damage bis er aufgebraucht ist; Rest geht auf HP.
				// Wenn Armor < damage/2 → nur Teil-Absorption, Überschuss + andere Hälfte auf HP.
				int dmgToArmor = 0, dmgToHp = dmg;
				if (!isHead && vs.Armor > 0)
				{
					dmgToArmor = Mathf.Min(dmg / 2, vs.Armor);
					dmgToHp = dmg - dmgToArmor;
				}
				vs.Armor = (byte)Mathf.Max(0, vs.Armor - dmgToArmor);
				vs.Hp = (byte)Mathf.Max(0, vs.Hp - dmgToHp);
				vs.LastDamageTickMs = (long)Time.GetTicksMsec();

				string headTag = isHead ? " [HEAD]" : "";
				Dbg.Print($"[NetServer] HIT shooter={NetId} → victim={victim.NetId} weapon={weapon?.Name ?? "?"} part={group}{headTag} dmg={dmg} (armor-absorb={dmgToArmor}, hp-loss={dmgToHp}) → hp={vs.Hp} armor={vs.Armor}");
				server.SendHitTo(NetId, victim.NetId, group, (byte)Mathf.Min(255, dmg), vs.Hp, weaponId: 0);
				if (vs.Hp == 0)
				{
					Dbg.Print($"[NetServer] KILL shooter={NetId} killed victim={victim.NetId} via {group}{headTag} (weapon={weapon?.Name ?? "?"})");
					server.TriggerDeath(victim.NetId, NetId, weaponId: 0, isHeadshot: isHead);
				}
			}
		}

		server.BroadcastShotFired(
			NetId, weaponId: 0,
			_movement.LastShotOrigin, _movement.LastShotDirection,
			tracer: true,
			lagHit.Hit, lagHit.Position, lagHit.Normal,
			lagHit.Material.ToString());
	}

	/// <summary>Re-simulates a single tick with the saved user input. Physics state (OnFloor etc.) is
	/// re-derived from the current position. Audio, FX and net-send are skipped via <see cref="_isReplaying"/>.
	/// A streamlined variant of <see cref="FixedTick"/>.</summary>
	private void ReplayOneTick(MovementInput savedInput)
	{
		_movement.Velocity = Velocity;
		_movement.Step(savedInput);
		Velocity = _movement.Velocity;

		var fireIn = new FireInput
		{
			TickIndex = savedInput.TickIndex,
			FirePressed = false,
			ReloadPressed = false,
			InspectPressed = false,
			AdsHeld = savedInput.AdsHeld,
			CanFire = false,
			Weapon = savedInput.Weapon,
			Speed = _movement.HorizontalSpeed,
			ShooterPosition = GlobalPosition,
			ViewYaw = savedInput.ViewYaw,
			ViewPitch = savedInput.ViewPitch,
			Dt = savedInput.Dt,
		};
		_movement.FireStep(fireIn);

		_preMoveVelocityY = Velocity.Y;
		_movement.PreMoveHorizSpeed = new Vector3(Velocity.X, 0f, Velocity.Z).Length();
		TryStepUp(_fixedDt);
		MoveAndSlide();
		// Footstep-Cadence-State im Replay SKIPPEN — ist rein visual (Audio-Trigger), beeinflusst
		// Position-Reproduktion nicht. Spart ~0.01-0.02ms pro Replay-Tick × 8 Ticks/Cap = 0.16ms.
		// Bei nächster echter FixedTick fängt der Cadence-Phase einfach am aktuellen State an —
		// für Schritt-Audio unmerklich.
	}

	/// <summary>Sends the latest <see cref="MovementInput"/> plus current action states to the server as
	/// an InputPacket. Skipped during replays and on server agents. Also computes the subtick-fire
	/// offset (0..255 quantisation of the in-tick fraction at which fire-press fired this tick) from
	/// <see cref="_lastFirePressUsec"/> and <see cref="_tickStartUsec"/> — non-zero only when the fire
	/// edge fired since this tick started, so held auto-fire stays aligned to tick boundaries.</summary>
	private void SendNetInput()
	{
		if (!IsLocalPlayer) return;
		if (_isReplaying) return;
		var client = NetMain.Instance?.Client;
		if (client == null || !client.Spawned) return;
		bool blocked = InputGate.Blocked;
		bool firePressed = !blocked && Input.IsActionPressed(InputActions.Fire);
		bool reloadPressed = !blocked && Input.IsActionPressed(InputActions.Reload);
		bool inspectPressed = !blocked && Input.IsActionPressed(InputActions.Inspect);
		bool slotIsGrenade = _activeSlot == 1;
		byte fireSubTick = ComputeFireSubTick(firePressed);
		client.SendInput(_currentTick, _lastMovementInput, firePressed, reloadPressed, inspectPressed, slotIsGrenade, fireSubTick);
	}

	/// <summary>Quantises the in-tick fraction at which fire was pressed to a byte (1/256 of a tick).
	/// Returns 0 when fire is not pressed, when no press has been recorded yet, or when the recorded
	/// press is from a previous tick (= held auto-fire — the server should use the tick boundary, not
	/// a stale sub-tick offset).</summary>
	private byte ComputeFireSubTick(bool firePressed)
	{
		if (!firePressed || _lastFirePressUsec == 0 || _tickStartUsec == 0) return 0;
		if (_lastFirePressUsec < _tickStartUsec) return 0;
		ulong tickPeriodUsec = (ulong)Mathf.Max(1, (int)(_fixedDt * 1_000_000f));
		ulong offsetUsec = _lastFirePressUsec - _tickStartUsec;
		if (offsetUsec >= tickPeriodUsec) offsetUsec = tickPeriodUsec - 1;
		return (byte)Mathf.Min(255, (int)((offsetUsec * 256UL) / tickPeriodUsec));
	}

	/// <summary>Steps the footstep cadence per tick and plays the sound on each step event. The cadence
	/// logic (<see cref="FootstepController"/>) is deterministic and server-replayable; the material
	/// probing and audio are client-side side effects. The server steps the controller and broadcasts the
	/// step events; remote clients play them through their own <see cref="FootstepAudio"/>.</summary>
	private void HandleFootsteps()
	{
		bool blocked = InputGate.Blocked;
		_footstepLogic.Step(new FootstepInput
		{
			Dt = _fixedDt,
			HorizontalSpeed = _movement.HorizontalSpeed,
			OnFloor = IsOnFloor(),
			ShiftHeld = !blocked && Input.IsActionPressed(InputActions.Shift),
			CrouchHeld = _movement.CrouchBlend > 0.5f,
			IsSprinting = _movement.ActuallySprinting,
			IsSliding = _movement.IsSliding,
		});

		if (!_footstepLogic.DidStepThisFrame) return;
		if (_isReplaying) return;

		HitInfo ground = CastGround();
		StringName material = ground.Hit ? ground.Material : (StringName)"default";

		if (IsServerAuthority)
		{
			byte loudByte = (byte)Mathf.Clamp(Mathf.RoundToInt(_footstepLogic.StepLoudness * 255f), 0, 255);
			NetMain.Instance?.Server?.BroadcastFootstep(NetId, GlobalPosition, material.ToString(),
				loudByte, _footstepLogic.StepIsLeftFoot, _movement.ActuallySprinting);
			if (IsServerAgent) return;
		}

		bool inTunnel = IsTunnelGround(ground);
		Audio.PlayStep(GlobalPosition, material, _footstepLogic.StepLoudness, inTunnel, _movement.ActuallySprinting);
		Dbg.Print($"[footstep] tick={_currentTick} {(_footstepLogic.StepIsLeftFoot ? "L" : "R")} mat={material}{(inTunnel ? " tunnel" : "")} loud={_footstepLogic.StepLoudness:F2} speed={_movement.HorizontalSpeed:F1}");
	}

	private bool _reloadAudioWasActive;
	private bool _tpsReloadAnimWasActive;
	private bool _tpsInspectAnimWasActive;
	private float _tpsOffFloorTime;
	private bool _tpsLandFired;
	private float _tpsLandLightLen = -1f;
	private float _tpsLandHeavyLen = -1f;

	/// <summary>Per-tick weapon audio: shoot, dry-fire and reload. Triggered on fire-state edges of the
	/// <see cref="MovementController"/>. Server broadcasts the edges via reliable events so remote clients
	/// play them through their own WeaponAudio.</summary>
	private void HandleWeaponAudio()
	{
		if (IsServerAgent) return;
		if (_isReplaying) return;
		WeaponStats weapon = WeaponHolder?.ActiveWeapon;
		if (weapon == null) return;

		Vector3 muzzlePos = WeaponHolder?.MuzzleWorldPosition ?? GlobalPosition;

		if (_movement.DidFireThisFrame)
			Audio.PlayShoot(weapon, muzzlePos, ProbeReverbEnv(CastGround()));

		if (_movement.DidDryFireThisFrame)
			Audio.PlayDryFire(weapon, muzzlePos);

		bool reloadingNow = _movement.IsReloading;
		if (reloadingNow && !_reloadAudioWasActive)
			Audio.PlayReload(weapon, muzzlePos);
		_reloadAudioWasActive = reloadingNow;
	}

	/// <summary>Classifies the gunshot reverb environment via an upward ceiling raycast.
	/// Tunnel-tagged ground returns Tunnel, a ceiling hit returns Indoor, otherwise Outdoor.</summary>
	private ReverbEnv ProbeReverbEnv(HitInfo ground)
	{
		if (IsTunnelGround(ground)) return ReverbEnv.Tunnel;
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return ReverbEnv.Outdoor;
		Vector3 from = GlobalPosition + Vector3.Up * 1.0f;
		HitInfo ceiling = Hitscan.Cast(space, from, Vector3.Up, 8f, exclude: GetRid(), mask: 1u);
		return ceiling.Hit ? ReverbEnv.Indoor : ReverbEnv.Outdoor;
	}

	/// <summary>Down-raycast under the feet. Uses the same material detection as <see cref="HandleHitscan"/>.</summary>
	private HitInfo CastGround()
	{
		var space = GetWorld3D().DirectSpaceState;
		Vector3 from = GlobalPosition + Vector3.Up * 0.4f;
		return Hitscan.CastMulti(space, from, Vector3.Down, 1.0f, _selfExclude, mask: 1u);
	}

	/// <summary>True when the ground collider is in the "tunnel" group, used to swap to tunnel reverb.</summary>
	private static bool IsTunnelGround(HitInfo ground)
		=> ground.Hit && ground.Collider != null && ground.Collider.IsInGroup("tunnel");

	/// <summary>Performs the hitscan after DoFire using LastShotOrigin and LastShotDirection from the movement
	/// controller. On the server agent or host authority, also performs lag-compensated damage and broadcasts
	/// the ShotFired event.</summary>
	private void HandleHitscan()
	{
		if (_isReplaying) return;
		var space = GetWorld3D().DirectSpaceState;

		if (IsServerAuthority)
		{
			using (MiniProfiler.SampleClient("PlayerCore.RunAuthoritativeHitscan")) RunAuthoritativeHitscan(space);
			if (IsServerAgent) return;
		}

		HitInfo hit;
		using (MiniProfiler.SampleClient("PlayerCore.Hitscan.Cast")) hit = Hitscan.Cast(space, _movement.LastShotOrigin, _movement.LastShotDirection,
			HitscanRange, exclude: GetRid(), mask: HitscanMask);

		float shotLength = hit.Hit ? hit.Distance : HitscanRange;
		using (MiniProfiler.SampleClient("PlayerCore.SmokeVoxelField.DisturbAll")) SmokeVoxelField.DisturbAll(_movement.LastShotOrigin, _movement.LastShotDirection, shotLength);

		if (hit.Hit)
		{
			using (MiniProfiler.SampleClient("PlayerCore.BulletImpactManager.Spawn")) BulletImpactManager.Instance?.Spawn(hit.Position, hit.Normal, hit.Material);
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
				Dbg.Print($"[hit] tick={_currentTick} | {hit.Collider?.Name ?? "?"} ({typeName}) parent={parent} | mat={hit.Material} groups=[{groupsStr}] | pos={hit.Position:F2} dist={hit.Distance:F2}m");
			}
		}
		else
		{
			Dbg.Print($"[hit] tick={_currentTick} | NO HIT (origin={_movement.LastShotOrigin:F2} dir={_movement.LastShotDirection:F2})");
		}
	}

	/// <summary>Builds the per-tick fire input, gating weapon actions when the grenade slot is active.
	/// Zwei Pfade: ServerAgent zieht alle Fire-Trigger aus dem replizierten <see cref="ServerBaseCharacter.NetInputSource"/>
	/// (Headless Server hat kein Godot Input — Live-Read würde IMMER false liefern → keine Schüsse, kein
	/// Damage, kein Kill. War der Grund warum auf dedicated server keine Damage applied wurde obwohl
	/// Client den Tracer/Decal sah). LocalPlayer + IsPuppet (rendered-only) lesen Live-Input wie gehabt.
	/// Weapon: ServerAgent fallback auf ConVars.Weapons.M4A1 weil server_*.tscn keinen WeaponHolder hat.</summary>
	private FireInput BuildFireInput(float dt)
	{
		bool blocked = InputGate.Blocked;
		bool weaponSlot = _activeSlot == 0;

		bool firePressed, reloadPressed, inspectPressed, adsHeld;
		if (IsServerAgent && NetInputSource.HasValue)
		{
			var p = NetInputSource.Value;
			firePressed    = weaponSlot && p.FirePressed;
			reloadPressed  = weaponSlot && p.ReloadPressed;
			inspectPressed = weaponSlot && p.InspectPressed;
			adsHeld        = weaponSlot && p.AdsHeld;
		}
		else
		{
			firePressed    = !blocked && weaponSlot && Input.IsActionPressed(InputActions.Fire);
			reloadPressed  = !blocked && weaponSlot && Input.IsActionPressed(InputActions.Reload);
			inspectPressed = !blocked && weaponSlot && Input.IsActionPressed(InputActions.Inspect);
			adsHeld        = !blocked && weaponSlot && Input.IsActionPressed(InputActions.Ads);
		}

		// WeaponHolder ist auf ServerAgent null (server_player.tscn / server_bot_player.tscn enthalten
		// keinen FPS-Subtree mit WeaponHolder/LocalAnimation). Fallback auf M4A1 damit der MovementController
		// einen WeaponStats hat zum Feuern. Sobald multiple Weapons → registry-lookup via weaponId.
		WeaponStats weapon = WeaponHolder?.ActiveWeapon ?? (IsServerAgent ? ConVars.Weapons.M4A1 : null);

		// ShooterPosition: LocalPlayer hat Camera (Eye-Level), ServerAgent hat nur HeadPitch (auch Eye-
		// Level). GlobalPosition wäre der Foot-Origin → Shot ginge unterhalb des Zielspielers durch.
		Vector3 shootOrigin;
		if (ActiveCamera != null) shootOrigin = ActiveCamera.GlobalPosition;
		else if (HeadPitch != null) shootOrigin = HeadPitch.GlobalPosition;
		else shootOrigin = GlobalPosition;

		// TickIndex: für ServerAgent CLIENT-Tick aus NetInputSource statt eigener Server-Tick.
		// Der FireRng-Seed in MovementController.DoFire = (TickIndex × prime) ^ (ShotIndex × prime).
		// Client und Server müssen identische Seeds bekommen sonst desyncen Spread/Pattern (war Grund
		// warum Hipfire-Pattern auf der Wand woanders saß als Client-Prediction sagte).
		uint fireTick = (IsServerAgent && NetInputSource.HasValue) ? NetInputSource.Value.TickIndex : _currentTick;

		return new FireInput
		{
			TickIndex = fireTick,
			FirePressed = firePressed,
			ReloadPressed = reloadPressed,
			InspectPressed = inspectPressed,
			AdsHeld = adsHeld,
			CanFire = CanFire && weapon != null,
			Weapon = weapon,
			Speed = _movement.HorizontalSpeed,
			ShooterPosition = shootOrigin,
			ViewYaw = Rotation.Y,
			ViewPitch = HeadPitch != null ? HeadPitch.Rotation.X : 0f,
			Dt = dt,
		};
	}

	/// <summary>Handles slot switching (weapon vs. grenade) and steps the grenade charge controller.
	/// On release of the fire action in the grenade slot, fires <see cref="ThrowGrenade"/>. Runs in
	/// FixedTick so it is deterministic and server-replayable. ServerAgent zieht den Slot aus dem
	/// replizierten InputPacket (SlotIsGrenade-bit), sonst stünde _activeSlot ständig auf 0 → Grenade-Wurf
	/// ginge nicht durch + Fire-Gate (weaponSlot) wäre immer "weapon" obwohl Client gerade Grenade hält.</summary>
	private void HandleGrenades(float dt)
	{
		bool blocked = InputGate.Blocked;

		if (IsServerAgent && NetInputSource.HasValue)
		{
			_activeSlot = NetInputSource.Value.SlotIsGrenade ? 1 : 0;
		}
		else if (!blocked)
		{
			if (Input.IsActionJustPressed(InputActions.SlotWeapon)) _activeSlot = 0;
			if (Input.IsActionJustPressed(InputActions.SlotGrenade)) _activeSlot = 1;
		}

		bool grenadeSlot = _activeSlot == 1;
		_grenade.Step(new GrenadeInput
		{
			SlotActive = grenadeSlot,
			ThrowHeld = grenadeSlot && !blocked && Input.IsActionPressed(InputActions.Fire),
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

	/// <summary>Spawns a <see cref="SmokeGrenade"/> in front of the camera. Throw speed lerps between
	/// the min/max settings based on the held charge. Test mode imposes no inventory limit.</summary>
	private void ThrowGrenade()
	{
		if (!_pendingThrowValid) return;
		var client = NetMain.Instance?.Client;
		uint projectileId = (IsLocalPlayer && client != null) ? client.AllocateProjectileId() : 0u;
		byte ownerNetId = IsLocalPlayer ? NetId : (byte)0;
		SmokeGrenade.Spawn(GetParent(), _pendingThrowOrigin, _pendingThrowVel, GetRid(),
			ownerNetId: ownerNetId, projectileId: projectileId, isPuppet: false);
		if (IsLocalPlayer && client != null)
			client.SendGrenadeSpawn(projectileId, grenadeType: 0, _pendingThrowOrigin, _pendingThrowVel);
		Dbg.Print($"[grenade] thrown tick={_currentTick} vel={_pendingThrowVel:F1} pid={projectileId}");
	}

	/// <summary>Computes the throw origin and velocity for a given charge. Shared by <see cref="ThrowGrenade"/>
	/// and the aim guide so the preview matches the actual flight path.</summary>
	private bool ComputeThrow(float charge, out Vector3 origin, out Vector3 vel)
	{
		origin = vel = Vector3.Zero;
		Camera3D cam = ActiveCamera;
		if (cam == null) return false;

		Vector3 fwd = -cam.GlobalTransform.Basis.Z;
		Vector3 dir = (fwd + Vector3.Up * ConVars.Sv.GrenadeThrowUpBias).Normalized();
		float speed = Mathf.Lerp(ConVars.Sv.GrenadeMinThrowSpeed, ConVars.Sv.GrenadeMaxThrowSpeed, charge);
		Vector3 inherit = new Vector3(Velocity.X, 0f, Velocity.Z) * ConVars.Sv.GrenadeInheritVelocity;
		vel = dir * speed + inherit;
		origin = cam.GlobalPosition + fwd * 0.4f;
		return true;
	}

	/// <summary>Per-physics-tick third-person camera spring-arm and ADS blend. Default is a no-op
	/// (puppets and server agents have no TpsCamera). <see cref="LocalPlayer"/> overrides this with the
	/// full spring-arm logic.</summary>
	protected virtual void UpdateTpsCameraCollision() { }

	/// <summary>Drives the third-person animation tree (locomotion state machine, blend positions, aim pose,
	/// and one-shot triggers). Public so <see cref="PuppetPlayer"/> can feed the tree for remote players.</summary>
	public void UpdateTpsAnimations()
	{
		if (TpsAnimTree == null) return;

		Vector3 localVel = GlobalTransform.Basis.Inverse() * new Vector3(Velocity.X, 0, Velocity.Z);
		float speed = new Vector2(localVel.X, localVel.Z).Length();

		bool moving = speed > 0.5f;
		bool shiftHeld, isCrouching, inSprint;
		if (IsPuppet)
		{
			isCrouching = _movement.CrouchBlend > 0.5f;
			inSprint = !isCrouching && moving && PuppetIsSprinting;
			shiftHeld = !inSprint && moving && speed < TpsWalkRunThreshold;
		}
		else if (IsServerAgent)
		{
			// ServerAgent: KEINEN Input.IsActionPressed (= liest HOST-Keyboard, nicht den remote-Player-
			// Input) → state aus _movement / NetInputSource. CrouchBlend wird in MovementController vom
			// CrouchHeld-Flag aus NetInputSource updated. Damit folgt die Skeleton-Crouch-Animation auch
			// auf dem ServerAgent → Hitboxen gehen mit runter. Lag-Comp rewindet automatisch korrekt
			// weil BoneHistory cs.GlobalTransform nach dem AnimMixer-Pass speichert.
			isCrouching = _movement.CrouchBlend > 0.5f;
			bool shift = NetInputSource.HasValue && NetInputSource.Value.ShiftHeld;
			inSprint = !isCrouching && moving && _movement.ActuallySprinting;
			shiftHeld = !inSprint && moving && shift;
		}
		else
		{
			shiftHeld = !InputGate.Blocked && Input.IsActionPressed(InputActions.Shift);
			isCrouching = !InputGate.Blocked && Input.IsActionPressed(InputActions.Crouch);
			inSprint = !isCrouching && moving && _movement.ActuallySprinting;
		}
		bool inWalk = !isCrouching && moving && shiftHeld && !inSprint;
		bool inRun = !isCrouching && moving && !inSprint && !inWalk;

		TpsAnimTree.Set(_aIsCrouching, isCrouching);
		TpsAnimTree.Set(_aIsNotCrouching, !isCrouching);
		TpsAnimTree.Set(_aIsRunning, inRun);
		TpsAnimTree.Set(_aIsNotRunning, inWalk);
		TpsAnimTree.Set(_aIsSprinting, inSprint);
		TpsAnimTree.Set(_aIsNotSprinting, !inSprint);

		bool floorContact = IsPuppet ? !PuppetIsAirborne : IsOnFloor();
		if (floorContact) _tpsOffFloorTime = 0f;
		else _tpsOffFloorTime += _fixedDt;
		bool inAir;
		if (IsPuppet)
		{
			inAir = PuppetIsAirborne;
		}
		else
		{
			inAir = _tpsOffFloorTime > TpsAirGraceTime || _movement.DidJumpThisFrame;
		}
		TpsAnimTree.Set(_aIsInAir, inAir);
		TpsAnimTree.Set(_aIsOnFloor, !inAir);

		if (!IsPuppet && _movement.DidJumpThisFrame)
		{
			TpsAnimTree.Set(_aJumpStart, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			_tpsLandFired = false;
		}

		if (!IsPuppet && inAir && !_tpsLandFired && Velocity.Y < -0.5f)
		{
			var space = GetWorld3D()?.DirectSpaceState;
			if (space != null)
			{
				float fallSpeed = Mathf.Abs(Velocity.Y);
				bool heavy = fallSpeed > TpsHeavyLandThreshold;
				float animLen = GetTpsLandAnimLength(heavy);
				float leadTime = animLen * TpsLandLeadFactor;
				float g = Mathf.Max(ConVars.Sv.Gravity, 0.01f);

				Vector3 from = GlobalPosition + Vector3.Up * 0.1f;
				float maxDist = Mathf.Min(fallSpeed * leadTime + 0.5f * g * leadTime * leadTime + 0.3f, TpsLandRayMaxDist);
				_rayQuery.From = from;
				_rayQuery.To = from + Vector3.Down * maxDist;
				_rayQuery.CollisionMask = uint.MaxValue;
				var hit = space.IntersectRay(_rayQuery);
				if (hit.Count > 0)
				{
					float distToGround = Mathf.Max(0f, from.Y - ((Vector3)hit["position"]).Y);
					float timeToGround = (-fallSpeed + Mathf.Sqrt(fallSpeed * fallSpeed + 2f * g * distToGround)) / g;
					if (timeToGround <= leadTime)
					{
						string param = heavy ? "parameters/JumpLandHeavy/request" : "parameters/JumpLand/request";
						TpsAnimTree.Set(param, (int)AnimationNodeOneShot.OneShotRequest.Fire);
						_tpsLandFired = true;
					}
				}
			}
		}

		if (!IsPuppet && !inAir && _tpsLandFired)
		{
			TpsAnimTree.Set(_aJumpLand, (int)AnimationNodeOneShot.OneShotRequest.Abort);
			TpsAnimTree.Set(_aJumpLandHeavy, (int)AnimationNodeOneShot.OneShotRequest.Abort);
			_tpsLandFired = false;
		}

		float naturalSpeed;
		if (isCrouching) naturalSpeed = TpsCrouchAnimNaturalSpeed;
		else if (inSprint) naturalSpeed = TpsSprintAnimNaturalSpeed;
		else if (inWalk) naturalSpeed = TpsWalkAnimNaturalSpeed;
		else naturalSpeed = TpsRunAnimNaturalSpeed;

		float perStateNorm = Mathf.Max(naturalSpeed, 0.1f);
		Vector2 perStateBlend = new Vector2(localVel.X / perStateNorm, -localVel.Z / perStateNorm);
		perStateBlend.X = Mathf.Clamp(perStateBlend.X, -1f, 1f);
		perStateBlend.Y = Mathf.Clamp(perStateBlend.Y, -1f, 1f);
		bool startingOrStopping = _tpsBlendSmoothed.Length() < 0.3f || perStateBlend.Length() < 0.3f;
		float rate = startingOrStopping ? TpsBlendStopRate : TpsBlendSmoothRate;
		float smoothT = 1f - Mathf.Exp(-rate * _fixedDt);
		_tpsBlendSmoothed = _tpsBlendSmoothed.Lerp(perStateBlend, smoothT);
		TpsAnimTree.Set(_aWalkBlend, _tpsBlendSmoothed);
		TpsAnimTree.Set(_aRunBlend, _tpsBlendSmoothed);
		TpsAnimTree.Set(_aCrouchBlend, _tpsBlendSmoothed);
		TpsAnimTree.Set(_aSprintBlend, _tpsBlendSmoothed.X);

		float timeScale = speed > 0.1f ? Mathf.Clamp(speed / perStateNorm, TpsAnimTimeScaleMin, TpsAnimTimeScaleMax) : 1f;
		TpsAnimTree.Set(_aTimeScale, timeScale);

		float aimStancePos = Mathf.Clamp(speed / Mathf.Max(TpsWalkAnimNaturalSpeed, 0.1f), 0f, 1f);
		TpsAnimTree.Set(_aAimStancePose, aimStancePos);

		// weaponSelected drivt UpperBodyMix (= activates Pose_Aim Spine-Replace) und AdsPose Blend.
		// ServerAgent hat keinen WeaponHolder (server_*.tscn enthält keinen FPS-Subtree) → wir können
		// nicht via WeaponHolder.ActiveWeapon checken. Stattdessen reicht _activeSlot == 0 (= Weapon
		// statt Grenade). ServerAgent's _activeSlot wird via NetInputSource.SlotIsGrenade synct.
		// Puppet liest aus PuppetActiveSlot (vom Snapshot gefüllt).
		bool weaponSelected = IsPuppet
			? PuppetActiveSlot == 0
			: IsServerAgent
				? _activeSlot == 0
				: _activeSlot == 0 && WeaponHolder?.ActiveWeapon != null;
		TpsAnimTree.Set(_aAdsPose, weaponSelected ? _movement.AdsBlend : 0f);
		TpsAnimTree.Set(_aUpperBodyMix, weaponSelected ? 1f : 0f);

		if (weaponSelected)
		{
			if (!IsPuppet)
			{
				if (_movement.DidFireThisFrame)
					TpsAnimTree.Set(_aFire, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			}

			bool reloadingNow = IsPuppet ? PuppetIsReloading : _movement.IsReloading;
			if (reloadingNow && !_tpsReloadAnimWasActive)
			{
				bool wasEmpty = !IsPuppet && _movement.CurrentMag == 0;
				StringName param = wasEmpty ? _aReloadEmpty : _aReload;
				TpsAnimTree.Set(param, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			}
			_tpsReloadAnimWasActive = reloadingNow;

			bool inspectingNow = IsPuppet ? PuppetIsInspecting : _movement.IsInspecting;
			if (inspectingNow && !_tpsInspectAnimWasActive)
				TpsAnimTree.Set(_aInspect, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			_tpsInspectAnimWasActive = inspectingNow;
		}
	}

	/// <summary>Returns the length of the jump-land animation (light or heavy) from the animation tree.
	/// Lazy-cached on first access. Falls back to <see cref="TpsLandFallbackDuration"/> when not resolvable.</summary>
	private float GetTpsLandAnimLength(bool heavy)
	{
		float cached = heavy ? _tpsLandHeavyLen : _tpsLandLightLen;
		if (cached > 0f) return cached;

		float len = TpsLandFallbackDuration;
		var anim = TpsAnimTree?.GetAnimation(heavy ? "Jump_Land_Heavy" : "Jump_Land_Light");
		if (anim != null && anim.Length > 0.01) len = (float)anim.Length;

		if (heavy) _tpsLandHeavyLen = len;
		else _tpsLandLightLen = len;
		return len;
	}

	private float _serverSmoothedBodyYaw;
	private bool _serverBodyYawInitialized;
	// Identische Konstanten wie PuppetPlayer damit ServerAgent's Body-Yaw 1:1 dem entspricht was
	// der Client-Puppet rendered. Sonst hätte der Puppet einen Spine-Twist (= viewYaw - puppetBodyYaw)
	// und der Server's Spine wäre nicht twisted → Schulter/Arm-Hitboxen verschoben gegenüber Puppet.
	private const float ServerBodyYawMaxTwistRad = 1.5708f;
	private const float ServerBodyYawRateMoving = 12f;
	private const float ServerBodyYawRateStanding = 6f;

	/// <summary>Syncs the spine twist and pitch onto one of three paths in priority order:
	/// <see cref="AimModifier"/> first, then the Marker3D targets, then a direct SetBonePoseRotation fallback.
	/// ServerAgent-Pfad: spiegelt das Puppet-Body-Yaw-Smoothing (lagged) und setzt AimModifier.SpineTwist
	/// + rotiert die Skeleton-Root so dass die Bone-Posen exakt dem Puppet entsprechen. Lag-Comp greift
	/// dann automatisch korrekt weil BoneHistory die nun aim+twist-korrekten cs.GlobalTransform speichert.</summary>
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
			// Body zur gesmootheten Yaw rotieren — Movement/Hitscan nutzen input.ViewYaw direkt (NICHT
			// Rotation.Y), also keine Gameplay-Side-Effects. Snapshots broadcasten weiterhin ViewYaw.
			var rot = Rotation; rot.Y = _serverSmoothedBodyYaw; Rotation = rot;
			AimModifier.SpineTwist = Mathf.Wrap(svViewYaw - _serverSmoothedBodyYaw, -Mathf.Pi, Mathf.Pi);
			return;
		}

		if (ViewMode != ViewMode.Tps)
		{
			if (AimModifier != null) AimModifier.SpineTwist = 0f;
			return;
		}

		float pitch = HeadPitch != null ? HeadPitch.Rotation.X : 0f;
		float twist = IsPuppet ? PuppetSpineTwist : 0f;

		if (AimModifier != null)
		{
			AimModifier.SpineTwist = twist;
			return;
		}

		if (AimPitchTarget != null)
		{
			Vector3 origin;
			if (TpsSkeleton != null && _tpsAimBoneIdx >= 0)
			{
				var bonePoseLocal = TpsSkeleton.GetBoneGlobalPose(_tpsAimBoneIdx);
				origin = TpsSkeleton.ToGlobal(bonePoseLocal.Origin);
			}
			else
			{
				origin = GlobalPosition + Vector3.Up * StandEyeHeight;
			}

			Vector3 bodyFwd = -GlobalTransform.Basis.Z;
			Vector3 bodyRight = GlobalTransform.Basis.X;
			float pScale = pitch * TpsAimPitchScale;

			float sinT = Mathf.Sin(twist);
			float cosT = Mathf.Cos(twist);
			Vector3 horizDir = bodyFwd * cosT + bodyRight * sinT;
			Vector3 lookDir = (horizDir * Mathf.Cos(pScale) + Vector3.Up * Mathf.Sin(pScale)).Normalized();
			AimPitchTarget.GlobalPosition = origin + lookDir * AimMarkerDistance;

			if (AimYawTarget != null && AimYawTarget != AimPitchTarget)
				AimYawTarget.GlobalPosition = origin + horizDir.Normalized() * AimMarkerDistance;
			return;
		}

		if (TpsSkeleton == null || _tpsAimBoneIdx < 0) return;
		float scaledPitch = pitch * TpsAimPitchScale;
		Quaternion pitchRot = new Quaternion(Vector3.Right, scaledPitch);
		Quaternion aimRot = pitchRot;
		if (Mathf.Abs(twist) > 0.001f)
		{
			Quaternion twistRot = new Quaternion(Vector3.Up, twist * TpsAimPitchScale);
			aimRot = twistRot * pitchRot;
		}
		TpsSkeleton.SetBonePoseRotation(_tpsAimBoneIdx, _tpsAimBoneRestRot * aimRot);
	}

	/// <summary>Per-frame throw aim-guide update. Default no-op (server agents and puppets do nothing).
	/// <see cref="LocalPlayer"/> overrides this with the trajectory rendering logic.</summary>
	protected virtual void UpdateAimGuide() { }

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

	/// <summary>Wires the movement and footstep controller references into the weapon holder for visual reads.</summary>
	private void ConfigureWeaponHolder()
	{
		if (WeaponHolder == null) return;
		WeaponHolder.Movement = _movement;
		WeaponHolder.Footstep = _footstepLogic;
	}

	/// <summary>Builds the per-tick movement input. Server agents pull from the latest net packet; local
	/// players read live Godot input gated by <see cref="InputGate"/>.</summary>
	private MovementInput BuildMovementInput(float dt)
	{
		if (IsServerAgent && NetInputSource.HasValue)
			return BuildMovementInputFromNet(dt, NetInputSource.Value);

		bool blocked = InputGate.Blocked;
		Vector3 wish = Vector3.Zero;
		if (!blocked)
		{
			if (Input.IsActionPressed(InputActions.Forward)) wish.Z -= 1f;
			if (Input.IsActionPressed(InputActions.Back)) wish.Z += 1f;
			if (Input.IsActionPressed(InputActions.Left)) wish.X -= 1f;
			if (Input.IsActionPressed(InputActions.Right)) wish.X += 1f;
		}

		return new MovementInput
		{
			TickIndex = _currentTick,
			WishDir = wish,
			ViewYaw = Rotation.Y,
			ViewPitch = HeadPitch != null ? HeadPitch.Rotation.X : 0f,
			SprintHeld = !blocked && Input.IsActionPressed(InputActions.Sprint),
			ShiftHeld = !blocked && Input.IsActionPressed(InputActions.Shift),
			CrouchHeld = !blocked && Input.IsActionPressed(InputActions.Crouch),
			CrouchPressed = !blocked && Input.IsActionJustPressed(InputActions.Crouch),
			AdsHeld = !blocked && Input.IsActionPressed(InputActions.Ads),
			BreathHoldHeld = !blocked && Input.IsActionPressed(InputActions.Breath),
			Weapon = WeaponHolder?.ActiveWeapon,
			JumpPressed = !blocked && Input.IsActionPressed(InputActions.Jump),
			OnFloor = IsOnFloor(),
			TouchingWall = IsOnWall(),
			WallNormal = IsOnWall() ? GetWallNormal() : Vector3.Zero,
			Dt = dt,
		};
	}

	/// <summary>Server agent: builds <see cref="MovementInput"/> from the latest <see cref="InputPacket"/>
	/// and applies body yaw and head pitch onto the nodes so dependent reads stay consistent.</summary>
	private MovementInput BuildMovementInputFromNet(float dt, in InputPacket p)
	{
		var rot = Rotation;
		rot.Y = p.ViewYaw;
		Rotation = rot;
		if (HeadPitch != null)
		{
			var hr = HeadPitch.Rotation;
			hr.X = p.ViewPitch;
			HeadPitch.Rotation = hr;
		}

		Vector3 wish = new(p.WishX, 0f, p.WishZ);
		if (wish.LengthSquared() > 1f) wish = wish.Normalized();

		return new MovementInput
		{
			TickIndex = _currentTick,
			WishDir = wish,
			ViewYaw = p.ViewYaw,
			ViewPitch = p.ViewPitch,
			SprintHeld = p.SprintHeld,
			ShiftHeld = p.ShiftHeld,
			CrouchHeld = p.CrouchHeld,
			CrouchPressed = p.CrouchPressed,
			AdsHeld = p.AdsHeld,
			BreathHoldHeld = p.BreathHoldHeld,
			Weapon = WeaponHolder?.ActiveWeapon,
			JumpPressed = p.JumpPressed,
			OnFloor = IsOnFloor(),
			TouchingWall = IsOnWall(),
			WallNormal = IsOnWall() ? GetWallNormal() : Vector3.Zero,
			Dt = dt,
		};
	}

	/// <summary>Override: also lerps the head-pitch eye height between stand and crouch. Only the local
	/// player needs this because the camera looks through HeadPitch. Base handles the capsule resize.</summary>
	protected override void ApplyCrouchHeight()
	{
		base.ApplyCrouchHeight();
		if (HeadPitch != null)
		{
			float blend = _movement.CrouchBlend;
			float y = Mathf.Lerp(StandEyeHeight, CrouchEyeHeight, blend);
			HeadPitch.Position = new Vector3(_headBasePos.X, y, _headBasePos.Z);
		}
	}

	/// <summary>Pre-MoveAndSlide step-up. Tests for a small lip in front of the character; if found,
	/// lifts the body up by the actual step height. MoveAndSlide then moves horizontally and FloorSnap
	/// pulls back down onto the new floor.
	///
	/// Runs EVERY physics tick (128 Hz) - the tick-gate (32 Hz, then 64 Hz) caused visible "stuck
	/// on stairs" stutter at sprint speed because the player rammed against the lip for 15-31 ms
	/// between checks, losing horizontal velocity. ~1 ms/s total cost = imperceptible.
	/// Skips when airborne (no step-up in air).</summary>
	private void TryStepUp(float dt)
	{
		if (StepMaxHeight <= 0f || !IsOnFloor()) return;

		Vector3 horizVel = new Vector3(Velocity.X, 0f, Velocity.Z);
		Vector3 wishLocal = _lastMovementInput.WishDir;
		bool hasInput = wishLocal.LengthSquared() > 0.01f;
		bool hasVel = horizVel.LengthSquared() >= 0.25f;
		if (!hasInput && !hasVel) return;

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
			return;
		}

		Transform3D elevated = startTrans.Translated(upMove);
		if (TestMove(elevated, horizMove))
		{
			if (Dbg.Enabled)
			{
				Vector3 dbgProbeFrom = startTrans.Origin + inputDir * (CapsuleRadius + 0.1f) + Vector3.Up * 5f;
				_rayQuery.From = dbgProbeFrom;
				_rayQuery.To = dbgProbeFrom + Vector3.Down * 10f;
				var topHit = GetWorld3D().DirectSpaceState.IntersectRay(_rayQuery);
				string heightStr = topHit.Count > 0
					? $"{((Vector3)topHit["position"]).Y - startTrans.Origin.Y:F2}m"
					: "unknown (raycast no hit)";
				Dbg.Print($"[stepup] BLOCKED — obstacle height ≈ {heightStr} (> StepMax {StepMaxHeight:F2}m) → crouch-jump (up to ~{MantleMinHeight:F1}m) or mantle (up to {MantleMaxHeight:F2}m) required.");
			}
			return;
		}

		Vector3 fwd = inputDir;
		Vector3 probeFrom = startTrans.Origin + fwd * (CapsuleRadius + 0.1f)
			+ new Vector3(0f, StepMaxHeight + 0.15f, 0f);
		var space = GetWorld3D().DirectSpaceState;
		_rayQuery.From = probeFrom;
		_rayQuery.To = probeFrom + new Vector3(0f, -(StepMaxHeight + 0.35f), 0f);
		var downHit = space.IntersectRay(_rayQuery);
		if (downHit.Count == 0)
		{
			Dbg.Print("[stepup] no step surface detected → no lift");
			return;
		}
		float actualStep = ((Vector3)downHit["position"]).Y - startTrans.Origin.Y;
		if (actualStep <= 0.02f || actualStep > StepMaxHeight)
		{
			Dbg.Print($"[stepup] obstacle {actualStep:F2}m outside [0.02..{StepMaxHeight:F2}m] → no lift (jump/mantle required)");
			return;
		}
		GlobalPosition += new Vector3(0f, actualStep, 0f);
		// Clear vertical velocity nach dem Lift — sonst nimmt next-frame MoveAndSlide eine bereits
		// akkumulierte Falling-Velocity mit und schubst den Spieler durch das Step durch (besonders
		// bei tall steps und negative pre-step Velocity).
		var v = Velocity;
		if (v.Y < 0f) { v.Y = 0f; Velocity = v; }
		Dbg.Print($"[stepup] +{actualStep:F3}m (ground threshold) | speed={horizVel.Length():F1}");
	}

	/// <summary>Auto-mantle: when airborne with forward wish input and crouch held, and an obstacle with a
	/// reachable flat top is detected in front, lerps the player smoothly onto the top over
	/// <see cref="MantleDuration"/>. The crouch gate prevents unintended mantles on regular run-jumps.</summary>
	private void TryMantle()
	{
		if (_isMantling) return;
		if (!_movement.IsAirborne) return;
		if (Velocity.Y < -0.5f) return;
		if (_movement.LastWishDir.LengthSquared() < 0.01f) return;
		if (!_lastMovementInput.CrouchHeld) return;

		Vector3 forward = -Transform.Basis.Z;

		Vector3 chest = GlobalPosition + new Vector3(0f, StandHeight * 0.5f, 0f);
		var space = GetWorld3D().DirectSpaceState;

		// Mantle Mask = NUR Welt (Layer 1) — NICHT Player-Capsules (Layer 2/5) oder Hitboxen (Layer 3).
		// Sonst klettert man auf andere Spieler hoch was komplett broken ist (CS2 macht das auch nicht).
		_rayQuery.CollisionMask = 1u;
		_rayQuery.From = chest;
		_rayQuery.To = chest + forward * MantleReach;
		var fwdResult = space.IntersectRay(_rayQuery);
		if (fwdResult.Count == 0) return;

		Vector3 fwdNormal = (Vector3)fwdResult["normal"];
		if (Mathf.Abs(fwdNormal.Y) > 0.4f) return;
		Vector3 fwdHit = (Vector3)fwdResult["position"];

		Vector3 topPos = default;
		float heightDiff = 0f;
		bool foundTop = false;
		float lastRejectedDiff = float.NaN;
		foreach (float fwdOff in _mantleForwardOffsets)
		{
			Vector3 above = fwdHit + forward * fwdOff + new Vector3(0f, MantleMaxHeight, 0f);
			_rayQuery.From = above;
			_rayQuery.To = above + new Vector3(0f, -MantleMaxHeight * 1.5f, 0f);
			var dh = space.IntersectRay(_rayQuery);
			if (dh.Count == 0) continue;
			if (((Vector3)dh["normal"]).Y < 0.7f) continue;
			Vector3 p = (Vector3)dh["position"];
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
		var inHit = space.IntersectRay(_rayQuery);
		if (inHit.Count > 0
			&& ((Vector3)inHit["normal"]).Y >= 0.7f
			&& Mathf.Abs(((Vector3)inHit["position"]).Y - topPos.Y) < 0.15f)
		{
			landingPos = (Vector3)inHit["position"];
		}

		Dbg.Print($"[mantle] FIRED (climb) heightDiff={heightDiff:F2}m | topY={topPos.Y:F2} playerY={GlobalPosition.Y:F2} → target=({landingPos.X:F2},{landingPos.Y:F2},{landingPos.Z:F2})");

		_isMantling = true;
		_mantleStart = GlobalPosition;
		_mantleTarget = landingPos + new Vector3(0f, 0.05f, 0f);
		_mantleTimer = MantleDuration;
		Velocity = Vector3.Zero;
		_mantleReconcileBlockUntilTick = _currentTick + (uint)Mathf.CeilToInt(MantleDuration * TickRate) + 30;
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
			float impact = Mathf.Max(0f, -_preMoveVelocityY);

			if (_isReplaying) { _wasOnFloor = onFloorNow; return; }

			if (IsServerAuthority)
			{
				NetMain.Instance?.Server?.BroadcastLand(NetId, impact);
				if (IsServerAgent) { _wasOnFloor = onFloorNow; return; }
			}

			WeaponHolder?.TriggerLand(impact);
			if (impact > 1.5f)
			{
				float impact01 = Mathf.Clamp((impact - 1.5f) / 7f, 0f, 1f);
				HitInfo ground = CastGround();
				StringName mat = ground.Hit ? ground.Material : (StringName)"default";
				Audio.PlayLand(GlobalPosition, mat, impact01, IsTunnelGround(ground));
			}
			Dbg.Print($"[land] impact={impact:F1} m/s | pos=({GlobalPosition.X:F1},{GlobalPosition.Y:F1},{GlobalPosition.Z:F1})");
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
		if (_movement.DidJumpThisFrame)
		{
			if (_isReplaying) return;

			if (IsServerAuthority)
			{
				NetMain.Instance?.Server?.BroadcastJump(NetId);
				if (IsServerAgent) return;
			}

			WeaponHolder?.TriggerJump(autoLand: false);
			{
				HitInfo ground = CastGround();
				StringName mat = ground.Hit ? ground.Material : (StringName)"default";
				Audio.PlayJump(GlobalPosition, mat, _movement.ActuallySprinting ? 1f : 0.75f, IsTunnelGround(ground));
			}
			if (Dbg.Enabled)
			{
				string label = _lastMovementInput.CrouchHeld ? "crouch-jump" : "jump";
				Dbg.Print($"[{label}] vY={Velocity.Y:F2} | horizSpeed={_movement.HorizontalSpeed:F1} | crouch={_movement.CrouchBlend:F1}");
			}
		}
		if (_movement.DidWallJumpThisFrame && Dbg.Enabled)
			Dbg.Print($"[walljump] vY={Velocity.Y:F2} | horizSpeed={_movement.HorizontalSpeed:F1}");
	}

	/// <summary>Pushes the current body-local velocity and yaw into the weapon holder for visual reads.</summary>
	private void SyncWeaponHolder()
	{
		if (WeaponHolder == null) return;
		Vector3 horizWorld = new Vector3(Velocity.X, 0f, Velocity.Z);
		WeaponHolder.CurrentVelocity = Transform.Basis.Inverse() * horizWorld;
		WeaponHolder.CurrentBodyYaw = Rotation.Y;
	}

}
