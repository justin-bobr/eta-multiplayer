using Godot;

namespace Vantix.Fx;

/// <summary>
/// Ground cloud-shadow overlay driving <c>cloud_shadows.gdshader</c> via the material_override
/// on this <see cref="MeshInstance3D"/>. Sun direction is auto-derived from <see cref="SunLightPath"/>.
/// Exports are pushed in _Ready and on inspector edits; _Process only feeds the per-frame smoke fields.
/// </summary>
[Tool]
[GlobalClass]
public partial class CloudShadows : MeshInstance3D
{
	private const int MaxSmokes = 40;

	[Export] public NodePath SunLightPath { get; set; }

	private Texture2D _cloudNoise;
	[Export] public Texture2D CloudNoise { get => _cloudNoise; set { _cloudNoise = value; if (value != null) _mat?.SetShaderParameter(_cloudNoiseParam, value); } }

	private float _cloudHeight = 20.0f;
	[Export(PropertyHint.Range, "1,200,0.5")] public float CloudHeight { get => _cloudHeight; set { _cloudHeight = value; _mat?.SetShaderParameter(_cloudHeightParam, value); } }

	private Vector2 _noiseTiling = new(3, 3);
	[Export] public Vector2 NoiseTiling { get => _noiseTiling; set { _noiseTiling = value; _mat?.SetShaderParameter(_noiseTilingParam, value); } }

	private Vector2 _windSpeed = new(0.05f, 0.05f);
	[Export] public Vector2 WindSpeed { get => _windSpeed; set { _windSpeed = value; _mat?.SetShaderParameter(_windSpeedParam, value); } }

	private float _cloudSag = 2.0f;
	[Export(PropertyHint.Range, "0.5,4,0.01")] public float CloudSag { get => _cloudSag; set { _cloudSag = value; _mat?.SetShaderParameter(_cloudSagParam, value); } }

	private float _coverage = 0.55f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Coverage { get => _coverage; set { _coverage = value; _mat?.SetShaderParameter(_coverageParam, value); } }

	private float _softness = 0.22f;
	[Export(PropertyHint.Range, "0.01,0.6,0.01")] public float Softness { get => _softness; set { _softness = value; _mat?.SetShaderParameter(_softnessParam, value); } }

	private float _shadowStrength = 0.65f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float ShadowStrength { get => _shadowStrength; set { _shadowStrength = value; _mat?.SetShaderParameter(_shadowStrengthParam, value); } }

	private Color _shadowTint = new(0.5f, 0.55f, 0.65f, 1f);
	[Export] public Color ShadowTint { get => _shadowTint; set { _shadowTint = value; _mat?.SetShaderParameter(_shadowTintParam, value); } }

	private float _surfaceFalloff = 0.4f;
	[Export(PropertyHint.Range, "0.05,1,0.01")] public float SurfaceFalloff { get => _surfaceFalloff; set { _surfaceFalloff = value; _mat?.SetShaderParameter(_surfaceFalloffParam, value); } }

	private float _shadowBrightnessFloor = 0.15f;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float ShadowBrightnessFloor { get => _shadowBrightnessFloor; set { _shadowBrightnessFloor = value; _mat?.SetShaderParameter(_shadowBrightnessFloorParam, value); } }

	private float _shadowBrightnessFull = 0.3f;
	[Export(PropertyHint.Range, "0.05,1,0.01")] public float ShadowBrightnessFull { get => _shadowBrightnessFull; set { _shadowBrightnessFull = value; _mat?.SetShaderParameter(_shadowBrightnessFullParam, value); } }

	private float _maxDistance = 120f;
	[Export(PropertyHint.Range, "20,500,1")] public float MaxDistance { get => _maxDistance; set { _maxDistance = value; _mat?.SetShaderParameter(_maxDistanceParam, value); } }

	private float _falloffRange = 80f;
	[Export(PropertyHint.Range, "5,200,1")] public float FalloffRange { get => _falloffRange; set { _falloffRange = value; _mat?.SetShaderParameter(_falloffRangeParam, value); } }

	private float _smokeDensityMul = 60f;
	[Export(PropertyHint.Range, "1,500,1")] public float SmokeDensityMul { get => _smokeDensityMul; set { _smokeDensityMul = value; _mat?.SetShaderParameter(_smokeDensityMulParam, value); } }

	// Pre-cached StringNames per parameter, avoiding string->StringName allocation on every setter and per-frame smoke update.
	private static readonly StringName _sunTravelDirParam = "sun_travel_dir";
	private static readonly StringName _cloudNoiseParam = "cloud_noise";
	private static readonly StringName _cloudHeightParam = "cloud_height";
	private static readonly StringName _noiseTilingParam = "noise_tiling";
	private static readonly StringName _windSpeedParam = "wind_speed";
	private static readonly StringName _cloudSagParam = "cloud_sag";
	private static readonly StringName _coverageParam = "coverage";
	private static readonly StringName _softnessParam = "softness";
	private static readonly StringName _shadowStrengthParam = "shadow_strength";
	private static readonly StringName _shadowTintParam = "shadow_tint";
	private static readonly StringName _surfaceFalloffParam = "surface_falloff";
	private static readonly StringName _shadowBrightnessFloorParam = "shadow_brightness_floor";
	private static readonly StringName _shadowBrightnessFullParam = "shadow_brightness_full";
	private static readonly StringName _maxDistanceParam = "max_distance";
	private static readonly StringName _falloffRangeParam = "falloff_range";
	private static readonly StringName _smokeDensityMulParam = "smoke_density_mul";
	private static readonly StringName[] _smokeTexParams = new StringName[MaxSmokes];
	private static readonly StringName[] _smokeMinParams = new StringName[MaxSmokes];
	private static readonly StringName[] _smokeSizeParams = new StringName[MaxSmokes];
	private static readonly StringName _smokeCountParam = "smoke_count";

	private ShaderMaterial _mat;
	private int _lastSmokeCount = -1;

	static CloudShadows()
	{
		for (int i = 0; i < MaxSmokes; i++)
		{
			_smokeTexParams[i] = "smoke_tex_" + i;
			_smokeMinParams[i] = "smoke_min_" + i;
			_smokeSizeParams[i] = "smoke_size_" + i;
		}
	}

	public override void _Ready()
	{
		_mat = MaterialOverride as ShaderMaterial;
		if (_mat == null)
		{
			GD.PushWarning("[cloud_shadows] No ShaderMaterial as material_override — component inactive.");
			return;
		}

		DirectionalLight3D sun = SunLightPath != null && !SunLightPath.IsEmpty
			? GetNodeOrNull<DirectionalLight3D>(SunLightPath)
			: null;
		if (sun != null)
		{
			_mat.SetShaderParameter(_sunTravelDirParam, -sun.GlobalTransform.Basis.Z);
		}
		else
		{
			GD.PushWarning("[cloud_shadows] SunLightPath not set or not a DirectionalLight3D — sun_travel_dir falls back to material default and will not track light rotation.");
		}

		PushAllExports();
	}

	private void PushAllExports()
	{
		if (_cloudNoise != null) _mat.SetShaderParameter(_cloudNoiseParam, _cloudNoise);
		_mat.SetShaderParameter(_cloudHeightParam, _cloudHeight);
		_mat.SetShaderParameter(_noiseTilingParam, _noiseTiling);
		_mat.SetShaderParameter(_windSpeedParam, _windSpeed);
		_mat.SetShaderParameter(_cloudSagParam, _cloudSag);
		_mat.SetShaderParameter(_coverageParam, _coverage);
		_mat.SetShaderParameter(_softnessParam, _softness);
		_mat.SetShaderParameter(_shadowStrengthParam, _shadowStrength);
		_mat.SetShaderParameter(_shadowTintParam, _shadowTint);
		_mat.SetShaderParameter(_surfaceFalloffParam, _surfaceFalloff);
		_mat.SetShaderParameter(_shadowBrightnessFloorParam, _shadowBrightnessFloor);
		_mat.SetShaderParameter(_shadowBrightnessFullParam, _shadowBrightnessFull);
		_mat.SetShaderParameter(_maxDistanceParam, _maxDistance);
		_mat.SetShaderParameter(_falloffRangeParam, _falloffRange);
		_mat.SetShaderParameter(_smokeDensityMulParam, _smokeDensityMul);
	}

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("CloudShadows._Process");
		if (_mat == null) return;
		if (!Settings.CloudShadows) return;

		var active = SmokeVoxelField.Active;
		var n = 0;
		for (var i = 0; i < active.Count && n < MaxSmokes; i++)
		{
			var f = active[i];
			if (f.DensityTexture == null) continue;
			_mat.SetShaderParameter(_smokeTexParams[n], f.DensityTexture);
			_mat.SetShaderParameter(_smokeMinParams[n], f.GridMin);
			_mat.SetShaderParameter(_smokeSizeParams[n], f.GridSize);
			n++;
		}
		if (n != _lastSmokeCount)
		{
			_mat.SetShaderParameter(_smokeCountParam, n);
			_lastSmokeCount = n;
		}
	}
}
