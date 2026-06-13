using System;
using Godot;

/// <summary>
/// Immutable definition of a weapon — gameplay and visual stats. One static instance per weapon
/// in <see cref="ConVars.Weapons"/>. Records are value-equality + init-only properties, ideal for
/// weapon definitions.
/// </summary>
public record WeaponStats
{
	public string Name { get; init; } = "Unknown";

	/// <summary>Damage per <see cref="HitboxGroup"/> (server-authoritative). Lookup via <see cref="DamageFor"/>.
	/// Defaults orientieren sich an CS2-Mid-Tier-Rifle: Headshot ist 120, Hände/Füße sind die niedrigsten.
	/// Overridable als immutable dictionary per Weapon in <see cref="ConVars.Weapons"/>.</summary>
	public System.Collections.Generic.Dictionary<HitboxGroup, int> Damages { get; init; } = new()
	{
		{ HitboxGroup.Head,  120 },   // Headshot = One-Shot vs unarmored
		{ HitboxGroup.Chest, 70 },
		{ HitboxGroup.Waist, 45 },
		{ HitboxGroup.Arm,   30 },
		{ HitboxGroup.Leg,   22 },
		{ HitboxGroup.Hand,  14 },    // Hände + Füße = niedrigster (peripherer Treffer)
		{ HitboxGroup.Foot,  14 },
		{ HitboxGroup.Body,  33 },    // Fallback
	};

	/// <summary>Damage lookup by hitbox group. Falls back to <see cref="HitboxGroup.Body"/>, then 25 if neither is set.</summary>
	public int DamageFor(HitboxGroup g)
	{
		if (Damages.TryGetValue(g, out int d)) return d;
		if (Damages.TryGetValue(HitboxGroup.Body, out int b)) return b;
		return 25;
	}

	public float FireRate { get; init; } = 10f;
	public Vector2[] RecoilPattern { get; init; } = Array.Empty<Vector2>();
	public float PatternScale { get; init; } = 1.0f;
	public float PatternResetDelay { get; init; } = 0.35f;
	public int FireMode { get; init; } = 0;
	public float AimPunchMaxClimb { get; init; } = 4.5f;
	public float AimPunchRecoveryFiring { get; init; } = 3.0f;
	public float AimPunchRecoveryReleased { get; init; } = 18.0f;
	/// <summary>Always-on hipfire spread (even while standing). ADS reduces it via AdsSpreadMul.</summary>
	public float HipfireBaseSpread { get; init; } = 0.35f;
	/// <summary>Maximum movement spread at sprint speed (additive on HipfireBaseSpread).</summary>
	public float MovementSpread { get; init; } = 1.4f;
	public float MovementSpreadShift { get; init; } = 0.15f;
	public float MovementSpreadWalk { get; init; } = 0.55f;
	/// <summary>Additive spread cone that grows with ShotIndex (exponential via HipfireBloomCurve).
	/// The deterministic RecoilPattern stays learnable — bloom is the additional random spread.</summary>
	public float HipfireBloomMax { get; init; } = 3.0f;
	/// <summary>Shot count until max bloom is reached.</summary>
	public float HipfireBloomShots { get; init; } = 8f;
	/// <summary>1.0 = linear (first shots already get bloom). >1 for an exponential ramp.</summary>
	public float HipfireBloomCurve { get; init; } = 1.0f;
	/// <summary>0 = camera stays calm, 1 = camera punches up — hipfire baseline.</summary>
	public float CameraAimPunchMul { get; init; } = 0.0f;
	/// <summary>Reduces aim-punch climb while aiming down sights (typical 50-65 % of hipfire amplitude).</summary>
	public float AdsCameraKickMul { get; init; } = 0.6f;
	/// <summary>Reduces high-frequency cam shake while ADS (typical ~40 %, scope is stabilized).</summary>
	public float AdsCamShakeMul { get; init; } = 0.4f;
	/// <summary>Per-weapon multiplier on cam-shake impulses (e.g. LMG=1.5, Sniper=2.0, SMG=0.7).</summary>
	public float CamShakeAmount { get; init; } = 1.0f;
	/// <summary>Reload duration in seconds (hardcoded per weapon, gates fire while reloading).</summary>
	public float ReloadTime { get; init; } = 2.0f;
	/// <summary>Inspect animation duration in seconds (gates ADS).</summary>
	public float InspectTime { get; init; } = 1.5f;

	/// <summary>Shots per magazine (e.g. 30 for AR, 7 for AWP).</summary>
	public int MagazineSize { get; init; } = 30;
	/// <summary>Maximum reserve ammo outside the magazine (e.g. 90 for AR, 30 for AWP).</summary>
	public int MaxReserveAmmo { get; init; } = 90;

	public float WeaponKickPitch { get; init; } = 2.0f;
	public float WeaponKickYaw { get; init; } = 0.15f;
	public float WeaponKickRoll { get; init; } = 0.0f;
	public float WeaponKickBack { get; init; } = 0.038f;
	public float WeaponKickUp { get; init; } = 0.012f;
	public float WeaponKickStiffness { get; init; } = 200f;
	public float WeaponKickDamping { get; init; } = 28f;
	public float WeaponRandomness { get; init; } = 0.2f;
	public float SpreadWeaponMul { get; init; } = 0.5f;
	public float AimPunchSmoothing { get; init; } = 18.0f;

	/// <summary>Trigger-finger pull-back (Marker3D Z offset, depends on the trigger geometry of the weapon).</summary>
	public float FingerKickZ { get; init; } = -2.0f;
	public float FingerKickRecovery { get; init; } = 12.0f;

	/// <summary>Target FOV during ADS (smaller = more zoom).</summary>
	public float AdsFov { get; init; } = 50f;
	/// <summary>Mouse sensitivity multiplier during ADS.</summary>
	public float AdsSensitivityMul { get; init; } = 0.5f;
	/// <summary>Movement speed multiplier during ADS (mix between hip-walk and tactical pace).</summary>
	public float AdsSpeedMul { get; init; } = 0.65f;
	/// <summary>Base hip-fire movement speed multiplier — applied on top of <see cref="SvConVars.WalkSpeed"/> / <see cref="SvConVars.SprintSpeed"/>. Matches CS2's per-weapon <c>max_player_speed</c> field. 1.0 = Knife/fastest (no penalty), ~0.86 = Rifle (M4/AK), ~0.80 = AWP/Sniper, ~0.78 = LMG. Stacks multiplicatively with <see cref="AdsSpeedMul"/> when aiming.</summary>
	public float MoveSpeedMul { get; init; } = 1.0f;
	/// <summary>Movement speed multiplier while SPRINTING (gun lowered). Separate from <see cref="MoveSpeedMul"/>
	/// so a weapon can have a different sprint penalty than its walk penalty. 1.0 = full SprintSpeed, no penalty.</summary>
	public float SprintSpeedMul { get; init; } = 1.0f;
	/// <summary>Base + movement spread multiplier while ADS (laser-tight first shot).</summary>
	public float AdsSpreadMul { get; init; } = 0.15f;
	/// <summary>Additional multiplier on the movement portion (strafe/walk) while ADS — keeps
	/// strafing spray tight so the pattern dominates over the random cone.</summary>
	public float AdsMovementSpreadMul { get; init; } = 0.25f;
	/// <summary>Bloom multiplier while ADS — almost off so the deterministic pattern dominates.</summary>
	public float AdsBloomMul { get; init; } = 0.10f;
	/// <summary>Spread multiplier while airborne (jumping is very inaccurate).</summary>
	public float AirborneSpreadMul { get; init; } = 2.5f;
	/// <summary>ADS spread multiplier while airborne (replaces AdsSpreadMul). ADS accuracy bonus is mostly lost mid-jump.</summary>
	public float AdsSpreadAirMul { get; init; } = 0.6f;
	/// <summary>Transition time hip ↔ ADS.</summary>
	public float AdsBlendTime { get; init; } = 0.18f;
	/// <summary>Visual kick multiplier at full ADS for ROTATION (pitch/yaw/roll = muzzle). Small = scope stays calm.</summary>
	public float AdsKickMul { get; init; } = 0.5f;
	/// <summary>Visual kick multiplier at full ADS for POSITION (back/up = stock). Enough wobble
	/// without blocking the viewport during a spray (the spring would otherwise accumulate too strongly).</summary>
	public float AdsKickPosMul { get; init; } = 0.40f;
	/// <summary>Ambient animations (bob/sway/lean/breath) multiplier at full ADS — subtle ones still pass through.</summary>
	public float AdsAmbientMul { get; init; } = 0.3f;
	/// <summary>Local-space ADS position offset, lerped additively.</summary>
	public Godot.Vector3 AdsPosOffset { get; init; } = new(-0.05f, 0.04f, -0.06f);
	/// <summary>Local-space ADS rotation offset in degrees, lerped additively.</summary>
	public Godot.Vector3 AdsRotOffset { get; init; } = Godot.Vector3.Zero;
	/// <summary>Additive correction scaled with CrouchBlend — without it the camera/head moves
	/// down while iron sights drift relative to the screen.</summary>
	public Godot.Vector3 AdsCrouchPosAdd { get; init; } = Godot.Vector3.Zero;
	public Godot.Vector3 AdsCrouchRotAdd { get; init; } = Godot.Vector3.Zero;

	/// <summary>Main muzzle "boom" layer.</summary>
	public string[] ShootBodyClips { get; init; } = Array.Empty<string>();
	/// <summary>Mechanical layer (bolt/action) — dry.</summary>
	public string[] ShootMechClips { get; init; } = Array.Empty<string>();
	/// <summary>Optional reverb tail sample.</summary>
	public string[] ShootTailClips { get; init; } = Array.Empty<string>();
	/// <summary>Distance variant ("boom" instead of "crack") for remote shooters.</summary>
	public string[] ShootDistantClips { get; init; } = Array.Empty<string>();
	public string[] ReloadClips { get; init; } = Array.Empty<string>();
	/// <summary>Empty-magazine click.</summary>
	public string[] DryFireClips { get; init; } = Array.Empty<string>();
	/// <summary>Per-weapon level offset on the shot sound.</summary>
	public float ShootVolumeDb { get; init; } = 0f;
	/// <summary>Above this listener distance, switch from body to distant layer.</summary>
	public float DistantCrossoverM { get; init; } = 28f;
}
