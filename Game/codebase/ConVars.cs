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
	public float WalkSpeed = 3.6f;
	public float SprintSpeed = 5.1f;
	public float CrouchSpeed = 2.1f;
	/// <summary>Fraction of horizontal speed bled off on each step-up, scaled by step height. 0 = no penalty.</summary>
	public float StepUpSpeedPenalty = 0.15f;

	public float GroundAcceleration = 15f;
	public float GroundFriction = 5.2f;
	public float StopSpeed = 1.6f;
	public float AirAcceleration = 100f;
	public float AirMaxWishSpeed = 0.6f;
	public float JumpVelocity = 4.95f;
	public float JumpSpeedBonus = 0.65f;
	public float JumpSpeedBonusThreshold = 2.0f;
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
	public float WallJumpSpeedRef = 6.8f;
	public float WallJumpLookWeight = 0.65f;

	public bool WallClingEnabled = true;
	public float WallClingDuration = 1.25f;
	public int WallClingChargesPerSpawn = 1;
	public float WallClingMinSpeed = 5.5f;
	public float WallClingIntoWallDot = 0.45f;
	/// <summary>Grace window (s) after a regular jump during which wall-cling cannot trigger.</summary>
	public float WallClingPostJumpGrace = 0.25f;

	public float WallAssistBonus = 1.12f;

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
	public float StaminaDrainRate = 12.5f;
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

	/// <summary>Master toggle for all anti-cheat detection. False = no detection, no violations, no kicks.</summary>
	public bool AntiCheatEnabled = true;
	/// <summary>Auto-disconnect peers exceeding <see cref="AntiCheatKickThreshold"/> violations inside
	/// <see cref="AntiCheatViolationWindowMs"/>. Off = violations are only logged and counted.</summary>
	public bool AntiCheatAutoKick = false;
	/// <summary>Sliding window (ms) for grouping violations; older ones age out.</summary>
	public int AntiCheatViolationWindowMs = 10_000;
	/// <summary>Violations-within-window threshold that triggers a kick.</summary>
	public int AntiCheatKickThreshold = 5;
	/// <summary>Bot combat skill (0-3, clamped). Higher = faster reaction + better aim point.
	///   0 = ~500ms, aims at feet; 1 = ~350ms, body; 2 = ~200ms, body/head; 3 = ~80ms, head.</summary>
	public int BotDifficulty = 1;

	/// <summary>Per-peer cap on InputPackets processed in one server tick. 8 covers legit jitter/batch
	/// bursts; excess is dropped and counted as a violation.</summary>
	public int MaxClientPacketsPerServerTick = 8;
	/// <summary>Max plausible yaw rate (rad/s). 250 ≈ 14000°/s — above pro flick peaks, flags snap-aim bots.</summary>
	public float MaxClientYawRateRadPerSec = 250f;
	/// <summary>Max ticks the client's <c>TickIndex</c> may run ahead of the server (≈500ms RTT at 128 Hz);
	/// beyond this = spoof or clock-attack.</summary>
	public int MaxClientTickAheadOfServer = 64;
	/// <summary>Max plausible position-delta per server-tick (m/s); sustained motion above is a bug or bypassed clamp.</summary>
	public float MaxClientPositionDeltaMps = 25f;

	/// <summary>Broadcast hitbox transforms at 10 Hz; clients render red capsules/spheres at server hitbox positions.</summary>
	public bool DebugHitboxes = false;
	/// <summary>Clients render a red body capsule at the last server position per puppet (uses Snapshot.Pos).</summary>
	public bool DebugCapsule = false;
	/// <summary>Clients render a yellow ray from camera to the server aim endpoint (uses Snapshot.Yaw/Pitch + AimPunch).</summary>
	public bool DebugAimRay = false;
	/// <summary>Red markers (5s) at server-authoritative hit positions of own shots; compare vs client decals to find drift.</summary>
	public bool DebugBullets = false;
	/// <summary>Disables lag-comp bone rewind — casts use live hitbox positions. Isolates rewind vs handoff misses.</summary>
	public bool NoRewind = false;

	/// <summary>Server-side profiler: periodic warning for [SV] samples over <see cref="ProfilerThresholdMs"/>.
	/// In listen-mode reads the HUD-flushed snapshot to avoid a double clear.</summary>
	public bool Profiler = false;

	/// <summary>Warning threshold (ms) for sv_profiler. ~25% of the 128 Hz tick budget.</summary>
	public float ProfilerThresholdMs = 2.0f;

	/// <summary>Distance-based PVS cutoff (Manhattan metres) for snapshot broadcasting; teammates always kept.
	/// 0 disables PVS (broadcast everything).</summary>
	public float PvsCutoffDistance = 200f;

	/// <summary>Fog of War: server strips enemies with no line-of-sight (and their position-leaking events)
	/// from receivers via a precomputed voxel-PVS; teammates and self always visible. Falls back to
	/// <see cref="PvsCutoffDistance"/> when off. Default off — the blocking build freezes the server
	/// 10-30s on first map load until made incremental. Opt-in via <c>sv_fog_of_war 1</c>.</summary>
	public bool FogOfWar = false;

	/// <summary>Voxel cell size (m) for <see cref="VoxelPvs"/>. Smaller = finer occlusion at N² memory/build cost.
	/// 4m de_dust2 sweet spot; 2.5m for tight maps, 6m for large open ones.</summary>
	public float FowVoxelSize = 4.0f;
}

/// <summary>Client-side ConVars (cl_*). Local only, cosmetic, each player has own values.</summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)]
public class ClConVars
{
	/// <summary>Toggles the <see cref="HudMiniProfiler"/> overlay — shows only samples above <see cref="ProfilerThresholdMs"/>.</summary>
	public bool Profiler = false;

	/// <summary>Threshold (ms) for profiler warnings shown in the HUD and logged. Raise if too spammy.</summary>
	public float ProfilerThresholdMs = 1.0f;

	public float MouseSensitivity = 2.0f;
	/// <summary>Per-axis yaw multiplier on raw mouse Relative.X. Source-derived 0.022 default. Tune via <c>cl_m_yaw</c>.</summary>
	public float MYaw = 0.022f;
	/// <summary>Per-axis pitch multiplier on raw mouse Relative.Y. Defaults to <see cref="MYaw"/>; lower for a slower vertical curve.</summary>
	public float MPitch = 0.022f;
	public float MinPitch = -89f;
	public float MaxPitch = 89f;
	public bool InvertMouseY = false;

	/// <summary>Visual-bleed rate (1/s) for the post-reconcile offset on small drift (≤ <see cref="ReconBleedLargeThresholdM"/>). 6.5 ≈ 154ms.</summary>
	public float ReconBleedNormal = 6.5f;
	/// <summary>Visual-bleed rate (1/s) for large drifts; lower than <see cref="ReconBleedNormal"/> so big recoveries stay smooth. 3.0 ≈ 333ms.</summary>
	public float ReconBleedLarge = 3.0f;
	/// <summary>Drift (m) above which <see cref="ReconBleedLarge"/> replaces <see cref="ReconBleedNormal"/>.</summary>
	public float ReconBleedLargeThresholdM = 0.5f;
	/// <summary>Drift (m) above which the visual offset hard-snaps to zero instead of bleeding. Set high to disable.</summary>
	public float ReconSnapThresholdM = 5.0f;

	/// <summary>Puppet interpolation delay. 0 = adaptive, tracking <see cref="NetStats.JitterDownMs"/> within
	/// [<see cref="InterpMinTicks"/>, <see cref="InterpMaxTicks"/>]. &gt;0 = lock to this tick count;
	/// competitive play wants <c>cl_interp_lock 6</c> to match the server's 6-tick lag-comp rewind.</summary>
	public int InterpLockTicks = 0;
	/// <summary>Lower bound for adaptive interp delay (ticks). 3 ≈ 23ms at 128Hz.</summary>
	public int InterpMinTicks = 3;
	/// <summary>Upper bound for adaptive interp delay (ticks). 12 ≈ 94ms at 128Hz; past this the puppet feels delayed.</summary>
	public int InterpMaxTicks = 12;

	public float Fov = 100.0f;
	public float FovBoost = 10.0f;
	public float CameraSwayMul = 0.15f;
	public float FovBlendSpeed = 6.0f;
	/// <summary>Blend speed for the peripheral sprint-blur fade (separate from FovBlendSpeed). Lower = gentler.</summary>
	public float SprintBlurBlendSpeed = 3.0f;
	/// <summary>Shift-walk locomotion bob magnitude (0..1).</summary>
	public float LocoShiftBobScale = 0.3f;
	/// <summary>Normal-walk locomotion bob magnitude (0..1); 1 = full baked bob.</summary>
	public float LocoWalkBobScale = 0.85f;
	/// <summary>Sprint head-bob scale (0..1); 1 = full baked sprint bob.</summary>
	public float LocoSprintBobScale = 0.85f;
	/// <summary>Locomotion bob multiplier while ADS (0..1).</summary>
	public float LocoAdsBobScale = 0.6f;

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
	public float JumpImpulseDip = 0.01f;
	public float LandImpulseDip = 0.03f;
	public float LandImpulseForward = 0.01f;
	public float LandPitchDown = 0.4f;
	public float JumpKickStiffness = 70f;
	public float JumpKickDamping = 14f;
	public float LandImpactSpeedRef = 10.0f;
	public float LandImpactMaxScale = 1.5f;
	public float JumpKickAdsMul = 0.25f;
	public bool JumpKickEnabled = true;
	/// <summary>Local jump/fall detection: an air cycle counts only on a jump press or a fall past this height (m).
	/// Height, not speed, separates descending stairs from a genuine drop.</summary>
	public float JumpMinFallHeight = 0.8f;
	/// <summary>Impact-speed gate for the puppet land sound (only impact is broadcast, not fall height).</summary>
	public float JumpMinFallSpeed = 4.5f;
	/// <summary>Cosmetic local-only camera step-smoothing; rate = catch-up speed.</summary>
	public bool StepSmoothEnabled = true;
	public float StepSmoothRate = 15f;
	public float StepSmoothMaxOffset = 0.22f;
	/// <summary>Low-pass rate for the speed driving the pose blend (lower = smoother). Cosmetic; real velocity unaffected.</summary>
	public float LocoSpeedSmoothRate = 6f;

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

/// <summary>Global ConVar container. Instances are passed around (constructor/property) rather than
/// accessed statically so code stays testable with mock instances.</summary>
public static class ConVars
{
	public static readonly SvConVars Sv = new();
	public static readonly ClConVars Cl = new();

	/// <summary>Weapon registry. One immutable definition per weapon. Add new weapons here.</summary>
	public static class Weapons
	{
		public static readonly WeaponStats AR15 = new()
		{
			Name = "AR15",
			FireRate = 8.0f,
			FireMode = 0,
			MoveSpeedMul = 1.0f,
			SprintSpeedMul = 1.0f,

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
			ShootBodyClips = System.Array.Empty<string>(),
			ShootMechClips = System.Array.Empty<string>(),
			ShootTailClips = System.Array.Empty<string>(),
			ShootDistantClips = System.Array.Empty<string>(),
			ReloadClips = System.Array.Empty<string>(),
			DryFireClips = System.Array.Empty<string>(),
			ShootVolumeDb = 0f,
			DistantCrossoverM = 28f,
		};
	}

	/// <summary>Sets a ConVar by name (sv_*/cl_*); returns true on success. AOT-safe via type-explicit prefix dispatch.</summary>
	public static bool TrySet(string name, string value)
	{
		if (string.IsNullOrEmpty(name))
			return false;
		string lower = name.ToLowerInvariant();
		if (lower.StartsWith("sv_"))
			return TrySetOn(Sv, lower[3..].Replace("_", ""), value);
		if (lower.StartsWith("cl_"))
			return TrySetOn(Cl, lower[3..].Replace("_", ""), value);
		return false;
	}

	/// <summary>Type-explicit set helper; the DynamicallyAccessedMembers attribute keeps field metadata under AOT.</summary>
	private static bool TrySetOn<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(T instance, string fieldName, string value)
	{
		if (instance == null)
			return false;
		var field = typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
		if (field == null)
			return false;
		try
		{
			object parsed = ParseValue(value, field.FieldType);
			if (parsed == null)
				return false;
			field.SetValue(instance, parsed);
			return true;
		}
		catch { return false; }
	}

	/// <summary>Gets a ConVar value as string, or null if not found. AOT-safe via the same dispatch as TrySet.</summary>
	public static string Get(string name)
	{
		if (string.IsNullOrEmpty(name))
			return null;
		string lower = name.ToLowerInvariant();
		if (lower.StartsWith("sv_"))
			return GetOn(Sv, lower[3..].Replace("_", ""));
		if (lower.StartsWith("cl_"))
			return GetOn(Cl, lower[3..].Replace("_", ""));
		return null;
	}

	/// <summary>Type-explicit get helper.</summary>
	private static string GetOn<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(T instance, string fieldName)
	{
		if (instance == null)
			return null;
		var field = typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
		return field?.GetValue(instance)?.ToString();
	}

	/// <summary>Enumerates all ConVar names in snake_case (e.g. "sv_debug_hitboxes"), optionally filtered by prefix.</summary>
	public static IEnumerable<string> List(string prefix = null)
	{
		foreach (var f in typeof(SvConVars).GetFields(BindingFlags.Public | BindingFlags.Instance))
			if (prefix == null || prefix.Equals("sv_", StringComparison.OrdinalIgnoreCase))
				yield return "sv_" + ToSnakeCase(f.Name);
		foreach (var f in typeof(ClConVars).GetFields(BindingFlags.Public | BindingFlags.Instance))
			if (prefix == null || prefix.Equals("cl_", StringComparison.OrdinalIgnoreCase))
				yield return "cl_" + ToSnakeCase(f.Name);
	}

	/// <summary>Converts "DebugHitboxes" → "debug_hitboxes" for console display and matching.</summary>
	private static string ToSnakeCase(string camelCase)
	{
		if (string.IsNullOrEmpty(camelCase))
			return camelCase;
		var sb = new System.Text.StringBuilder(camelCase.Length + 4);
		for (int i = 0; i < camelCase.Length; i++)
		{
			char c = camelCase[i];
			if (char.IsUpper(c) && i > 0)
				sb.Append('_');
			sb.Append(char.ToLowerInvariant(c));
		}
		return sb.ToString();
	}

	/// <summary>Returns a ConVar's .NET type (float/int/bool/string), or null if unknown. AOT-safe.</summary>
	public static Type GetFieldType(string name)
	{
		if (string.IsNullOrEmpty(name))
			return null;
		string lower = name.ToLowerInvariant();
		if (lower.StartsWith("sv_"))
			return GetFieldTypeOn<SvConVars>(lower[3..].Replace("_", ""));
		if (lower.StartsWith("cl_"))
			return GetFieldTypeOn<ClConVars>(lower[3..].Replace("_", ""));
		return null;
	}

	/// <summary>Type-explicit field-type lookup.</summary>
	private static Type GetFieldTypeOn<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(string fieldName)
	{
		var field = typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
		return field?.FieldType;
	}

	/// <summary>Checks whether a value string is compatible with the field type without setting it.
	/// Returns (ok, friendlyTypeName) for error messages.</summary>
	public static (bool ok, string typeName) ValidateValue(string name, string value)
	{
		var type = GetFieldType(name);
		if (type == null)
			return (false, "unknown");
		string typeName = TypeFriendlyName(type);
		try
		{
			object parsed = ParseValue(value, type);
			return (parsed != null, typeName);
		}
		catch { return (false, typeName); }
	}

	/// <summary>Friendly name for UI/errors: float/int/bool/string instead of Single/Int32/Boolean/String.</summary>
	public static string TypeFriendlyName(Type t)
	{
		if (t == typeof(float))
			return "float";
		if (t == typeof(int))
			return "int";
		if (t == typeof(bool))
			return "bool";
		if (t == typeof(string))
			return "string";
		return t.Name.ToLowerInvariant();
	}

	/// <summary>Parses a string into the requested primitive type (float/int/bool/string).</summary>
	private static object ParseValue(string value, Type targetType)
	{
		var culture = CultureInfo.InvariantCulture;
		if (targetType == typeof(float))
			return float.Parse(value, culture);
		if (targetType == typeof(int))
			return int.Parse(value, culture);
		if (targetType == typeof(bool))
			return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
		if (targetType == typeof(string))
			return value;
		return null;
	}
}
