using System.Diagnostics.CodeAnalysis;

namespace Vantix.Server;

/// <summary>Server-authoritative ConVars (sv_*). Gameplay-relevant, must match on server and client.</summary>
[DynamicallyAccessedMembers(
	DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
)]
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
