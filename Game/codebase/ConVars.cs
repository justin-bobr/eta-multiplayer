using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

/// <summary>Server-authoritative ConVars (sv_*). Gameplay-relevant, must match on server and client.</summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)]
public class SvConVars
{
	public float ShiftSpeed = 1.9f;
	public float WalkSpeed = 4.0f;
	public float SprintSpeed = 5.0f;
	public float CrouchSpeed = 1.9f;

	public float GroundAcceleration = 15f;
	public float GroundFriction = 5.2f;
	public float StopSpeed = 1.6f;
	public float AirAcceleration = 100f;
	public float AirMaxWishSpeed = 0.6f;
	public float JumpVelocity = 4.95f;
	public float JumpSpeedBonus = 0.65f;
	public float JumpSpeedBonusThreshold = 3.0f;
	public float JumpSprintForwardBoost = 0.5f;
	public float Gravity = 17.5f;
	public float ApexHangThreshold = 0.5f;
	public float ApexHangGravityMul = 1.0f;
	public float CoyoteTime = 0.10f;
	public float JumpBufferTime = 0.20f;
	public float CrouchJumpBufferTime = 0.15f;

	public float WallJumpVertical = 3.5f;
	public float WallJumpHorizontal = 2.0f;
	public float WallJumpMomentumKeep = 0.65f;
	public float WallJumpMinSpeed = 5.5f;
	public float WallJumpSpeedRef = 7.5f;
	public float WallJumpLookWeight = 0.65f;

	public bool WallClingEnabled = true;
	public float WallClingDuration = 1.25f;
	public int WallClingChargesPerSpawn = 1;
	public float WallClingMinSpeed = 5.5f;
	public float WallClingIntoWallDot = 0.45f;
	/// <summary>Grace window in seconds after a regular jump during which wall-cling cannot trigger. Prevents accidental "blocked" jumps when sprinting toward a wall and pressing space.</summary>
	public float WallClingPostJumpGrace = 0.25f;

	public float WallAssistBonus = 1.2f;

	public float CrouchJumpBonus = 1.35f;
	public float JumpForwardBoost = 2.0f;

	public bool CrouchCancelJumpEnabled = true;
	public float CrouchCancelJumpBonus = 1.85f;
	public float CrouchCancelJumpWindowStart = 0.06f;
	public float CrouchCancelJumpWindowEnd = 0.18f;

	public bool SlideEnabled = true;
	public float SlideStartSpeedMin = 5.5f;
	public float SlideBoostSpeed = 9.0f;
	public float SlideFriction = 6f;
	public float SlideMinSpeed = 3.0f;
	public float SlideMaxTime = 1.0f;

	public bool SlideStopAccuracyEnabled = true;
	public float SlideStopAccuracyWindow = 0.20f;
	public float SlideStopAccuracySpreadMul = 0.5f;
	public bool SlideStopHardBrake = true;

	public bool BreathHoldEnabled = true;
	public float BreathHoldDuration = 3.0f;
	public float BreathHoldRecoverDuration = 1.0f;
	public float BreathHoldSwayMul = 0.20f;
	public float BreathHoldShakySwayMul = 2.20f;
	public float BreathHoldBreathingMul = 0.45f;
	public float BreathHoldShakyBreathingMul = 1.60f;
	public float BreathHoldSpreadMul = 0.70f;
	public float BreathHoldShakySpreadMul = 1.45f;
	public float BreathHoldCooldownAfterRecover = 0.5f;

	public float CrouchTransitionSpeed = 5.0f;

	public float MaxStamina = 100f;
	public float StaminaDrainRate = 18.5f;
	public float StaminaRegenRate = 20f;
	public float StaminaRegenDelay = 0.5f;
	public float StaminaExhaustTimeout = 1.0f;
	public float StaminaSprintThreshold = 10f;

	public float SprintRaiseTime = 0.20f;
	public float SprintLowerTime = 0.06f;
	public float SprintFireGateBlend = 0.8f;

	public bool UnlimitedAmmoDefault = true;

	public float FootstepStrideLength = 2.05f;
	public float FootstepSprintStrideMul = 0.82f;
	public float FootstepCrouchStrideMul = 1.25f;
	public float FootstepMinSpeed = 0.7f;
	public float FootstepInitialStepFraction = 0.7f;
	public float FootstepMinLoudness = 0.12f;
	public float FootstepWalkLoudness = 0.62f;
	public float FootstepSprintLoudness = 1.0f;
	public float FootstepCrouchLoudnessMul = 0.45f;

	public float GrenadeMinThrowSpeed = 6f;
	public float GrenadeMaxThrowSpeed = 18.5f;
	public float GrenadeRangeScale = 1.2f;
	public float GrenadeThrowUpBias = 0.25f;
	public float GrenadeInheritVelocity = 0.6f;
	public float GrenadeChargeToFull = 0.7f;
	public float GrenadeMinCharge = 0.12f;

	// === Anti-Cheat (server-authoritative input + position validation) ===
	/// <summary>Master toggle for all anti-cheat detection. When false, no detection runs, no violations
	/// are registered, no kicks. Leave on at all times; granular tuning happens through the thresholds below.</summary>
	public bool AntiCheatEnabled = true;
	/// <summary>Automatically disconnect peers that exceed <see cref="AntiCheatKickThreshold"/> violations
	/// inside <see cref="AntiCheatViolationWindowMs"/>. Default off — production servers turn this on
	/// once thresholds are tuned. With this off, violations are only logged + counted.</summary>
	public bool AntiCheatAutoKick = false;
	/// <summary>Sliding window (ms) for grouping violations. 10 s default = a peer that triggers 5
	/// violations in 10 s gets kicked (when AutoKick is on). Older violations age out.</summary>
	public int AntiCheatViolationWindowMs = 10_000;
	/// <summary>Violations-within-window threshold that triggers a kick.</summary>
	public int AntiCheatKickThreshold = 5;
	/// <summary>Bot combat skill level (0-3). Higher = faster reaction + better aim point.
	///   0 = ~500ms reaction, aims at feet (most shots miss into the floor)
	///   1 = ~350ms reaction, aims at body
	///   2 = ~200ms reaction, alternates body / head
	///   3 = ~80ms reaction, aims at head
	/// Below 0 is clamped to 0, above 3 to 3. Default 1 = "casual"; bump for hard scrims.</summary>
	public int BotDifficulty = 1;

	/// <summary>Per-peer cap on InputPackets processed in one server tick. Real-world clients at 128 Hz
	/// average 1 packet per server tick, but LiteNetLib batches + network jitter regularly deliver
	/// 3-5 packets in one server-tick window during normal play (especially around route changes / brief
	/// stalls). 8 covers all realistic legit bursts; sustained floods at this rate would still light up
	/// the violation counter within a second. Anything above is dropped + counted as a violation.</summary>
	public int MaxClientPacketsPerServerTick = 8;
	/// <summary>Max plausible angular yaw rate in rad/s. 250 ≈ 14000 °/s. Pro flicks peak around 5000 °/s,
	/// so 250 leaves headroom for legit play but flags snap-aim bots (instantaneous ≥ 20000 °/s).</summary>
	public float MaxClientYawRateRadPerSec = 250f;
	/// <summary>How many ticks the client's <c>TickIndex</c> may legitimately run AHEAD of the server's
	/// current tick. Network jitter + client-side timing usually puts the client ~ RTT/2 ticks ahead, so
	/// 64 covers up to ~500 ms RTT at 128 Hz. TickIndex past this bound = spoof or clock-attack.</summary>
	public int MaxClientTickAheadOfServer = 64;
	/// <summary>Max plausible position-delta per server-tick (m/s). Any server-simulated translation faster
	/// than this counts as a violation. 25 m/s covers sprint + diagonals + slide-boost + small knockback
	/// margins; sustained motion above is either a physics-engine bug or a bypassed clamp.</summary>
	public float MaxClientPositionDeltaMps = 25f;

	// === Server-Debug-Visualization (server-weit, alle Clients sehen den gleichen Toggle-State) ===
	// Default OFF — Toggle per Console: `sv_debug_hitboxes 1` etc. Wird über ConVarSync-Packet vom
	// Server zu allen Clients broadcastet, persistiert über Reconnects (Initial-Sync nach SpawnAck).
	/// <summary>Server sendet DebugHitboxes-Packet (Hitbox-Transforms aller Agents @ 10Hz); Clients
	/// rendern rote Capsules/Spheres an den ECHTEN Server-Hitbox-Positionen.</summary>
	public bool DebugHitboxes = false;
	/// <summary>Clients rendern roten transparenten Body-Capsule an der letzten Server-AuthorityPos
	/// pro Puppet. Verbraucht keine extra Bandbreite (nutzt Snapshot.Pos).</summary>
	public bool DebugCapsule = false;
	/// <summary>Clients rendern gelben Strahl vom local-Cam zum Server-Aim-Endpoint. Verbraucht keine
	/// extra Bandbreite (nutzt Snapshot.Yaw/Pitch + AimPunch).</summary>
	public bool DebugAimRay = false;
	/// <summary>Rote Punkte (5s lifetime) an den Server-authoritativen Hit-Positionen vom OWN-Shot.
	/// Zeigt wo der Server die Bullets tatsächlich gecasted hat (= post Lag-Comp). Vergleich gegen
	/// Client-side decals (BulletImpactManager) zeigt ob Client/Server-Hit-Pos auseinanderdriften.</summary>
	public bool DebugBullets = false;
	/// <summary>Debug: Lag-Comp-Bone-Rewind ABSCHALTEN. Cast nutzt LIVE Server-Hitbox-Positionen
	/// (= aktueller Tick, kein Rewind in die Vergangenheit). Marker zeigen ebenfalls live Pose
	/// (BroadcastDebugHitboxes nutzt cs.GlobalTransform direkt statt BoneHistory.Query). Damit
	/// kann man testen ob das Rewind-System die Misses verursacht oder die Position-Übergabe.</summary>
	public bool NoRewind = false;

	/// <summary>Server-side Profiler-Dumper — periodischer GD.PushWarning für [SV]-prefixed Samples
	/// über <see cref="ProfilerThresholdMs"/>. Funktioniert auch im dedicated server (= ohne HUD).
	/// Im Listen-Mode liest er nur das vom HUD bereits geflushte snapshot, ohne double-clear.</summary>
	public bool Profiler = false;

	/// <summary>Schwelle in ms für sv_profiler Warnings. Defaults to 2ms (Server-Tick-Budget bei 128Hz
	/// = 7.8ms, also 2ms ist ~25% davon = deutlich auffällig). Höher setzen wenn Output spammt.</summary>
	public float ProfilerThresholdMs = 2.0f;

	/// <summary>Distance-based PVS cutoff for snapshot broadcasting. Per receiver, entities beyond this Manhattan-distance (metres) are stripped from the snapshot unless the receiver is a teammate (compass/minimap awareness is always preserved). 0 disables PVS entirely (= broadcast everything to everyone). Default 200m is liberal — fine for sniper engagements on big maps; smaller competitive maps can drop to ~80m for a tighter bandwidth win. CS2 / Source typically uses ~100m equivalent.</summary>
	public float PvsCutoffDistance = 200f;

	/// <summary>Valorant-style Fog of War: server strips enemies (and their position-leaking event
	/// broadcasts — footsteps, shots, jumps, lands) from receivers that have no line-of-sight to them,
	/// based on a precomputed voxel-PVS. Teammates and self are always visible regardless of LOS.
	/// Kills the ESP/wallhack cheat category entirely. Falls back to the distance-based
	/// <see cref="PvsCutoffDistance"/> gate when off. Default OFF until the PVS build is made
	/// incremental — the blocking build currently freezes the server (and listen-mode client) for
	/// 10-30s on first map load, which times out the loopback connection. Opt-in via <c>sv_fog_of_war 1</c>.</summary>
	public bool FogOfWar = false;

	/// <summary>Voxel cell size (metres) for <see cref="VoxelPvs"/>. Smaller = finer occlusion at quadratic
	/// memory + build cost (N²). 4m is the de_dust2-tested sweet spot — ~1280 voxels, ~200KB memory,
	/// ~15-30s first-build (cached for subsequent server starts). Drop to 2.5m for tight competitive
	/// maps; raise to 6m for large open maps where bandwidth matters more than tight occlusion.</summary>
	public float FowVoxelSize = 4.0f;
}

/// <summary>Client-side ConVars (cl_*). Local only, cosmetic, each player has own values.</summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)]
public class ClConVars
{
	// === Debug-Visualization Toggle (lokal, kein Server-Roundtrip) ===
	/// <summary>Grüne Wireframes um die LOKALEN Puppet-Hitboxen (= an der interpolierten Animation-Pose).
	/// Pure client-side, no server involvement.</summary>
	public bool DebugHitbox = false;

	/// <summary>Toggle <see cref="HudMiniProfiler"/> Overlay — zeigt NUR Samples > <see cref="ProfilerThresholdMs"/>.
	/// Warnings bleiben sticky für ~2 Sekunden damit lesbar. GD.Print bei jedem neuen Warning.</summary>
	public bool Profiler = false;

	/// <summary>Schwelle in ms für Profiler-Warnings. Samples deren Total-Time pro Frame über diesem
	/// Wert liegen werden im HUD angezeigt + via GD.Print geloggt. Default 1ms (= bei 60Hz/16ms-Budget
	/// schon eine deutliche Belastung). Höher setzen wenn HUD spammt.</summary>
	public float ProfilerThresholdMs = 1.0f;

	public float MouseSensitivity = 2.0f;
	/// <summary>Per-axis yaw multiplier applied to raw mouse Relative.X. Source-derived default of 0.022
	/// matches the well-known "Source sens" curves so muscle memory transfers. Tune via <c>cl_m_yaw</c>.</summary>
	public float MYaw = 0.022f;
	/// <summary>Per-axis pitch multiplier applied to raw mouse Relative.Y. Defaults to <see cref="MYaw"/>
	/// (1:1) but can be lowered (e.g. 0.015) for a slower vertical curve while keeping horizontal flicks
	/// fast — popular tweak among controller-to-mouse converts.</summary>
	public float MPitch = 0.022f;
	public float MinPitch = -89f;
	public float MaxPitch = 89f;
	public bool InvertMouseY = false;

	/// <summary>Visual-bleed rate (1/sec exponential decay) for the post-reconcile <c>_visualErrorOffset</c>
	/// when the corrected drift is small (≤ <see cref="ReconBleedLargeThresholdM"/>). 6.5/sec ≈ 154ms decay.</summary>
	public float ReconBleedNormal = 6.5f;
	/// <summary>Visual-bleed rate (1/sec) for large drifts above <see cref="ReconBleedLargeThresholdM"/>.
	/// Lower than <see cref="ReconBleedNormal"/> so a big rubber-band recovery feels smooth instead of snappy.
	/// 3.0/sec ≈ 333ms decay.</summary>
	public float ReconBleedLarge = 3.0f;
	/// <summary>Drift threshold (meters) above which <see cref="ReconBleedLarge"/> is used instead of
	/// <see cref="ReconBleedNormal"/>. 0.5m matches typical "you got moved" rubber-band feel.</summary>
	public float ReconBleedLargeThresholdM = 0.5f;
	/// <summary>Drift threshold (meters) above which the visual offset is zeroed instead of bled out (=
	/// hard snap). Set very high to disable. Previously hardcoded to 2m; widened to 5m so most
	/// rubber-bands smooth out instead of snapping. Teleport-scale corrections still snap.</summary>
	public float ReconSnapThresholdM = 5.0f;

	/// <summary>Adaptive puppet interpolation delay. 0 = adaptive (recommended for casual): the delay
	/// tracks <see cref="NetStats.JitterDownMs"/> within [<see cref="InterpMinTicks"/>, <see cref="InterpMaxTicks"/>].
	/// &gt;0 = lock to this exact tick count. Recommended for competitive play: <c>cl_interp_lock 6</c>
	/// to keep the client-side render delay in sync with the server's hardcoded lag-comp rewind of 6
	/// ticks (otherwise hit-reg can be off by a few ticks under adaptive conditions).</summary>
	public int InterpLockTicks = 0;
	/// <summary>Lower bound for adaptive interp delay (ticks). 3 ≈ 23ms at 128Hz — covers minimum jitter
	/// while staying responsive.</summary>
	public int InterpMinTicks = 3;
	/// <summary>Upper bound for adaptive interp delay (ticks). 12 ≈ 94ms at 128Hz — past this the puppet
	/// feels visibly delayed and competitive play suffers.</summary>
	public int InterpMaxTicks = 12;

	public float Fov = 90.0f;
	public float FovBoost = 6.0f;
	public float CameraSwayMul = 0.15f;
	public float FovBlendSpeed = 6.0f;

	public float CamShakeImpulsePitch = 1.2f;
	public float CamShakeImpulseYaw = 0.8f;
	public float CamShakeImpulseRoll = 0.6f;
	public float CamShakeStiffness = 2000f;
	public float CamShakeDamping = 55f;

	public float BreathSpeed = 0.22f;
	public float BreathPosAmount = 0.005f;
	public float BreathRotAmount = 0.16f;
	public float BreathForwardAmount = 0.002f;
	public float InhaleFraction = 0.42f;
	public float BreathBlendSpeed = 3.0f;

	public float BobFreqShift = 1.05f;
	public float BobFreqWalk = 1.5f;
	public float BobFreqRun = 1.85f;
	public float BobVerticalAmount = 0.0035f;
	public float BobHorizontalAmount = 0.0028f;
	public float BobRollAmount = 0.22f;
	public float BobPitchAmount = 0.13f;
	public float RunAmpMultiplier = 0.9f;
	public float BobBlendSpeed = 6.0f;
	public float RunSharpness = 0.35f;

	public float StrafeLeanRoll = 2.0f;
	public float StrafeLeanPos = 0.015f;
	public float ForwardLeanPitch = 0.45f;
	public float ForwardLeanPosDown = 0.005f;
	public float ForwardLeanPosForward = 0.012f;
	public float DirectionBlendSpeed = 7.0f;
	public float DirectionLeanStiffness = 120f;
	public float DirectionLeanDamping = 16f;
	public float BackwardSpeedFactor = 0.75f;

	public float SwayMinSpeed = 5.5f;
	public float SwayFreqMul = 1.0f;
	public float SwayHorizontal = 0.022f;
	public float SwayVertical = 0.010f;
	public float SwayDepth = 0.008f;
	public float SwayYaw = 1.6f;
	public float SwayRoll = 2.8f;
	public float SwayPitch = 0.70f;
	public float SwayBlendSpeed = 3.5f;

	public float MouseInertiaYaw = 0.014f;
	public float MouseInertiaPitch = 0.011f;
	public float MouseInertiaMaxYaw = 1.0f;
	public float MouseInertiaMaxPitch = 0.7f;
	public float MouseInertiaRecovery = 7.0f;
	public float MouseInertiaSmoothingIn = 20.0f;
	public float MouseInertiaSmoothingOut = 8.0f;
	public float MouseInertiaRollMul = 0.2f;

	public float InertiaTiltStrength = 0.08f;
	public float InertiaTiltMax = 1.8f;
	public float InertiaTiltRecovery = 6.0f;

	public float AirDriftUp = 0.008f;
	public float AirPitchTilt = 0.55f;
	public float AirBlendSpeed = 2.5f;

	public float LowerIdleDelay = 6.0f;
	public float LowerOffsetDown = 0.025f;
	public float LowerOffsetForward = 0.010f;
	public float LowerPitch = 7.0f;
	public float LowerRoll = 1.5f;
	public float LowerBlendSpeed = 2.0f;
	public float LowerExitSpeed = 9.0f;

	public float CrouchWeaponDrop = 0.030f;
	public float CrouchWeaponBack = 0.020f;
	public float CrouchWeaponInward = 0.010f;
	public float CrouchWeaponPitch = 3.5f;
	public float CrouchWeaponYaw = 2.0f;
	public float CrouchWeaponRoll = 1.5f;
	public float CrouchBobScale = 0.5f;
	public float CrouchBlendSpeedVisual = 5.0f;

	public float SprintLowerPosDown = 0.032f;
	public float SprintLowerPosRight = 0.025f;
	public float SprintLowerPosBack = 0.020f;
	public float SprintLowerPitch = -18f;
	public float SprintLowerYaw = 14f;
	public float SprintLowerRoll = 16f;
	public float SprintLowerForwardPitch = 4f;
	public float SprintLowerSideStrafeRange = 0.6f;
	public float SprintLowerSideStiffness = 140f;
	public float SprintLowerSideDamping = 18f;

	public float BodyYawLagStrength = 0.012f;
	public float BodyYawLagMax = 6.0f;
	public float BodyYawLagSmoothing = 14.0f;

	public float AirTime = 4.0f;
	public float JumpImpulseUp = 0.045f;
	public float JumpPitchAmount = 1.6f;
	public float LandImpulseDown = 0.090f;
	public float LandImpulseForward = 0.04f;
	public float LandPitchAmount = 4.5f;
	public float JumpKickStiffness = 70f;
	public float JumpKickDamping = 14f;
	public float LandImpactMinSpeed = 2.0f;
	public float LandImpactSpeedRef = 6.0f;
	public float LandImpactMaxScale = 2.5f;

	public bool ShowStaminaLabel = true;

	public bool CrosshairEnabled = true;
	public bool CrosshairShowDot = false;
	public bool CrosshairShowOutline = true;
	public Godot.Color CrosshairColor = new(1f, 1f, 1f, 0.95f);
	public Godot.Color CrosshairOutlineColor = new(0f, 0f, 0f, 0.85f);
	public Godot.Color CrosshairDotColor = new(1f, 1f, 1f, 1f);
	public float CrosshairThickness = 2f;
	public float CrosshairLength = 5f;
	public float CrosshairInnerGap = 8f;
	public float CrosshairOutlineThickness = 1f;
	public float CrosshairDotSize = 1.5f;
	public bool CrosshairDynamicMovement = true;
	public float CrosshairMovementMul = 0.5f;
	public bool CrosshairDynamicFiring = true;
	public float CrosshairFireKickAmount = 1.5f;
	public float CrosshairFireRecoverSpeed = 16f;
	public bool CrosshairHideDuringAds = true;
}

/// <summary>
/// Global ConVar container. Singleton style — instances are passed around in code
/// (via constructor/property) instead of static access so code stays testable
/// (mock instances are possible).
/// </summary>
public static class ConVars
{
	public static readonly SvConVars Sv = new();
	public static readonly ClConVars Cl = new();

	/// <summary>Weapon registry. One immutable definition per weapon. Add new weapons here.</summary>
	public static class Weapons
	{
		public static readonly WeaponStats M4A1 = new()
		{
			Name = "M4A1",
			FireRate = 8.0f,
			FireMode = 0,
			MoveSpeedMul = 0.86f,
			ReloadTime = 2.6f,
			MagazineSize = 30,
			MaxReserveAmmo = 90,
			PatternScale = 1.0f,
			PatternResetDelay = 0.35f,
			AimPunchMaxClimb = 4.5f,
			AimPunchRecoveryFiring = 3.0f,
			AimPunchRecoveryReleased = 18.0f,
			HipfireBaseSpread = 2.5f,
			MovementSpread = 1.4f,
			MovementSpreadShift = 0.15f,
			MovementSpreadWalk = 0.55f,
			CameraAimPunchMul = 0.50f,
			WeaponKickPitch = 0.3f,
			WeaponKickYaw = 0.10f,
			WeaponKickRoll = 0.0f,
			WeaponKickBack = 0.015f,
			WeaponKickUp = 0.020f,
			WeaponKickStiffness = 200f,
			WeaponKickDamping = 28f,
			WeaponRandomness = 0.2f,
			SpreadWeaponMul = 0.5f,
			AimPunchSmoothing = 18.0f,
			FingerKickZ = -4.0f,
			FingerKickRecovery = 12.0f,
			AdsFov = 60f,
			AdsPosOffset = new Godot.Vector3(-0.084f, 0.01f, 0.076f),
			AdsRotOffset = new Godot.Vector3(3.28f, 5.285f, -1.23f),
			AdsKickMul = 0.08f,
			AdsKickPosMul = 0.18f,
			AdsAmbientMul = 0.3f,
			RecoilPattern = new Godot.Vector2[]
			{
				new(0.00f, 0.40f),
				new(0.05f, 0.95f),
				new(0.10f, 1.25f),
				new(0.05f, 1.30f),
				new(-0.05f, 1.15f),
				new(-0.18f, 0.95f),
				new(-0.10f, 0.75f),
				new(0.15f, 0.55f),
				new(0.35f, 0.45f),
				new(0.50f, 0.40f),
				new(0.45f, 0.35f),
				new(0.25f, 0.30f),
				new(-0.05f, 0.30f),
				new(-0.35f, 0.25f),
				new(-0.60f, 0.25f),
				new(-0.70f, 0.20f),
				new(-0.65f, 0.20f),
				new(-0.45f, 0.20f),
				new(-0.20f, 0.15f),
				new(0.05f, 0.15f),
				new(0.30f, 0.15f),
				new(0.45f, 0.10f),
				new(0.50f, 0.10f),
				new(0.40f, 0.10f),
				new(0.20f, 0.10f),
				new(-0.05f, 0.10f),
				new(-0.25f, 0.05f),
				new(-0.30f, 0.05f),
				new(-0.25f, 0.05f),
				new(-0.15f, 0.05f),
			},
			ShootBodyClips    = System.Array.Empty<string>(),
			ShootMechClips    = System.Array.Empty<string>(),
			ShootTailClips    = System.Array.Empty<string>(),
			ShootDistantClips = System.Array.Empty<string>(),
			ReloadClips       = System.Array.Empty<string>(),
			DryFireClips      = System.Array.Empty<string>(),
			ShootVolumeDb     = 0f,
			DistantCrossoverM = 28f,
		};
	}

	/// <summary>Tries to set a ConVar by string name (sv_* or cl_*). Returns true on success. AOT-safe: dispatches on the sv_/cl_ prefix to a type-explicit GetField call so the trimmer can statically prove which type's fields are reached.</summary>
	public static bool TrySet(string name, string value)
	{
		if (string.IsNullOrEmpty(name)) return false;
		string lower = name.ToLowerInvariant();
		if (lower.StartsWith("sv_")) return TrySetOn(Sv, lower[3..].Replace("_", ""), value);
		if (lower.StartsWith("cl_")) return TrySetOn(Cl, lower[3..].Replace("_", ""), value);
		return false;
	}

	/// <summary>Type-explicit set helper. The DynamicallyAccessedMembers attribute on SvConVars/ClConVars propagates here so AOT keeps the field metadata.</summary>
	private static bool TrySetOn<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(T instance, string fieldName, string value)
	{
		if (instance == null) return false;
		var field = typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
		if (field == null) return false;
		try
		{
			object parsed = ParseValue(value, field.FieldType);
			if (parsed == null) return false;
			field.SetValue(instance, parsed);
			return true;
		}
		catch { return false; }
	}

	/// <summary>Gets the current value of a ConVar as string, or null if not found. AOT-safe via the same type-explicit dispatch as TrySet.</summary>
	public static string Get(string name)
	{
		if (string.IsNullOrEmpty(name)) return null;
		string lower = name.ToLowerInvariant();
		if (lower.StartsWith("sv_")) return GetOn(Sv, lower[3..].Replace("_", ""));
		if (lower.StartsWith("cl_")) return GetOn(Cl, lower[3..].Replace("_", ""));
		return null;
	}

	/// <summary>Type-explicit get helper.</summary>
	private static string GetOn<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(T instance, string fieldName)
	{
		if (instance == null) return null;
		var field = typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
		return field?.GetValue(instance)?.ToString();
	}

	/// <summary>Enumerates all ConVar names im snake_case-Format (z.B. "sv_debug_hitboxes" für
	/// Feld "DebugHitboxes"), optional gefiltert nach prefix ("sv_" oder "cl_").</summary>
	public static IEnumerable<string> List(string prefix = null)
	{
		foreach (var f in typeof(SvConVars).GetFields(BindingFlags.Public | BindingFlags.Instance))
			if (prefix == null || prefix.Equals("sv_", StringComparison.OrdinalIgnoreCase))
				yield return "sv_" + ToSnakeCase(f.Name);
		foreach (var f in typeof(ClConVars).GetFields(BindingFlags.Public | BindingFlags.Instance))
			if (prefix == null || prefix.Equals("cl_", StringComparison.OrdinalIgnoreCase))
				yield return "cl_" + ToSnakeCase(f.Name);
	}

	/// <summary>Konvertiert "DebugHitboxes" → "debug_hitboxes" für Console-User-Friendly Display + Match.</summary>
	private static string ToSnakeCase(string camelCase)
	{
		if (string.IsNullOrEmpty(camelCase)) return camelCase;
		var sb = new System.Text.StringBuilder(camelCase.Length + 4);
		for (int i = 0; i < camelCase.Length; i++)
		{
			char c = camelCase[i];
			if (char.IsUpper(c) && i > 0) sb.Append('_');
			sb.Append(char.ToLowerInvariant(c));
		}
		return sb.ToString();
	}

	/// <summary>Liefert den .NET-Typ einer ConVar (float/int/bool/string). Null wenn unbekannt.
	/// Genutzt vom Console-Typeahead und der Pre-Send-Validierung. AOT-safe.</summary>
	public static Type GetFieldType(string name)
	{
		if (string.IsNullOrEmpty(name)) return null;
		string lower = name.ToLowerInvariant();
		if (lower.StartsWith("sv_")) return GetFieldTypeOn<SvConVars>(lower[3..].Replace("_", ""));
		if (lower.StartsWith("cl_")) return GetFieldTypeOn<ClConVars>(lower[3..].Replace("_", ""));
		return null;
	}

	/// <summary>Type-explicit field-type lookup.</summary>
	private static Type GetFieldTypeOn<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(string fieldName)
	{
		var field = typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
		return field?.FieldType;
	}

	/// <summary>Prüft ob ein Value-String zum Field-Type passt OHNE den Wert zu setzen.
	/// Returns (ok, friendlyTypeName) — bei false ist friendlyTypeName z.B. "bool" für Fehlermeldung.</summary>
	public static (bool ok, string typeName) ValidateValue(string name, string value)
	{
		var type = GetFieldType(name);
		if (type == null) return (false, "unknown");
		string typeName = TypeFriendlyName(type);
		try
		{
			object parsed = ParseValue(value, type);
			return (parsed != null, typeName);
		}
		catch { return (false, typeName); }
	}

	/// <summary>Friendly-Name für UI/Errors: float/int/bool/string statt Single/Int32/Boolean/String.</summary>
	public static string TypeFriendlyName(Type t)
	{
		if (t == typeof(float)) return "float";
		if (t == typeof(int)) return "int";
		if (t == typeof(bool)) return "bool";
		if (t == typeof(string)) return "string";
		return t.Name.ToLowerInvariant();
	}

/// <summary>Parses a string into the requested primitive type (float/int/bool/string).</summary>
	private static object ParseValue(string value, Type targetType)
	{
		var culture = CultureInfo.InvariantCulture;
		if (targetType == typeof(float)) return float.Parse(value, culture);
		if (targetType == typeof(int)) return int.Parse(value, culture);
		if (targetType == typeof(bool))
			return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
		if (targetType == typeof(string)) return value;
		return null;
	}
}
