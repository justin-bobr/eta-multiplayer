using System.Diagnostics.CodeAnalysis;

namespace Vantix.Client;

/// <summary>Client-side ConVars (cl_*). Local only, cosmetic, each player has own values.</summary>
[DynamicallyAccessedMembers(
	DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
)]
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
