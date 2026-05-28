using Godot;

/// <summary>
/// Weapon component — bundles ALL per-weapon settings (refs, ADS, muzzle light,
/// tracer, shell) in a single scene. One .tscn per weapon, referenced by
/// LocalAnimation as <c>[Export] Weapon Weapon</c>. Each weapon has its own
/// values, so swapping the weapon swaps the entire look.
///
/// Per-weapon scene setup convention:
///   - root Node3D with this script
///   - mesh + skeleton from the GLB
///   - Marker3D (Muzzle) at the barrel end, with 3 GpuParticles3D children (flash/smoke/sparks)
///   - Marker3D (EjectionPort) at the ejection port
///   - AnimationPlayer + AnimationTree for weapon-specific anims (mag/bolt motion)
///   - Inspector values for ADS, muzzle light, tracer, shell tuned per weapon
/// </summary>
[Tool, GlobalClass]
public partial class Weapon : Node3D
{
	[ExportGroup("Refs")]
	[Export] public Node3D Muzzle;
	/// <summary>Flash particle system — Restart() per shot.</summary>
	[Export] public GpuParticles3D MuzzleFlashParticles;
	/// <summary>Template — duplicated per shot into world space.</summary>
	[Export] public GpuParticles3D MuzzleSmokeParticles;
	/// <summary>Forward sparks from the barrel — Restart() per shot.</summary>
	[Export] public GpuParticles3D MuzzleSparksParticles;
	[Export] public Node3D EjectionPort;
	/// <summary>Per-weapon animation tree (mag drop / bolt cycle / reload).</summary>
	[Export] public AnimationTree AnimTree;

	[ExportGroup("ADS Tuning")]
	[Export] public bool AdsTestMode;
	[Export] public Vector3 AdsTestPosOffset = new(-0.05f, 0.04f, -0.06f);
	[Export] public Vector3 AdsTestRotOffset = Vector3.Zero;
	/// <summary>FOV mode: true (default) → FOV lerps to hipfire × AdsFovScale (scales dynamically
	/// with the player FOV, regardless of 80/90/120). false → pure "dolly-in" via pos offset, FOV
	/// unchanged. Suggested scales: iron sights 0.75-0.85, scope 0.4-0.6, sniper 0.3.</summary>
	[Export] public bool AdsAffectsFov = true;
	[Export(PropertyHint.Range, "0.2,1.0,0.01")] public float AdsFovScale = 0.8f;
	/// <summary>Additive correction applied while crouching.</summary>
	[Export] public Vector3 AdsTestCrouchPosAdd = Vector3.Zero;
	[Export] public Vector3 AdsTestCrouchRotAdd = Vector3.Zero;

	[ExportGroup("ADS Calibration Marker")]
	/// <summary>Sphere distance forward for iron-sight targeting.</summary>
	[Export] public float AdsCalibrationDistance = 10f;
	[Export] public float AdsCalibrationSize = 0.04f;
	[Export] public Color AdsCalibrationColor = new(1f, 0.2f, 0.2f, 1f);

	[ExportGroup("Muzzle Light")]
	[Export] public Color MuzzleLightColor = new(1f, 0.75f, 0.35f);
	[Export] public float MuzzleLightEnergy = 5f;
	[Export] public float MuzzleLightRange = 3.5f;
	/// <summary>Seconds until fully faded out.</summary>
	[Export] public float MuzzleLightDuration = 0.05f;

	[ExportGroup("Tracer")]
	[Export] public bool TracerEnabled = true;
	[Export] public int TracerEveryNthShot = 5;
	/// <summary>HDR color for bloom.</summary>
	[Export] public Color TracerColor = new(2.5f, 1.6f, 0.5f, 1f);
	[Export] public float TracerWidth = 0.02f;
	/// <summary>Visible length (m).</summary>
	[Export] public float TracerStreakLength = 2f;
	/// <summary>Streak speed in m/s — slower than real bullets.</summary>
	[Export] public float TracerSpeed = 80f;
	[Export] public float TracerMaxRange = 80f;

	[ExportGroup("Shell Ejection")]
	[Export] public bool ShellEnabled = true;
	/// <summary>Local offset on EjectionPort.</summary>
	[Export] public Vector3 ShellSpawnOffset = Vector3.Zero;
	/// <summary>Local +X = right, +Y = up, +Z = back.</summary>
	[Export] public Vector3 ShellEjectDirection = new(0.6f, 0.1f, 1f);
	/// <summary>Initial rotation offset; 90° Z = horizontal-sideways.</summary>
	[Export] public Vector3 ShellInitialRotationDeg = new(0f, 0f, 90f);
	/// <summary>Base ejection speed in m/s.</summary>
	[Export] public float ShellEjectSpeed = 2.5f;
	/// <summary>Random spread around EjectDirection.</summary>
	[Export] public float ShellSpreadAngleDeg = 6f;
	/// <summary>Seconds until the slot is released.</summary>
	[Export] public float ShellLifetime = 30f;
}
