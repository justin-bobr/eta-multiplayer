using Godot;

/// <summary>
/// Drives the ground cloud-shadow overlay (cloud_shadows.gdshader). Every frame this feeds the
/// currently active <see cref="SmokeVoxelField"/> clouds to the shader — their 3D density texture
/// and world AABB. The shader reconstructs world position from the depth buffer; pixels that are
/// in fact behind smoke (volumetric fog writes no depth) would otherwise paint the BACKGROUND's
/// cloud shadow onto the smoke and make it look translucent. With the density texture the shader
/// integrates real smoke opacity and masks those pixels precisely.
/// The [Tool] attribute lets _Ready run in the editor so the mesh can be hidden there — the cloud
/// shader otherwise runs in the 3D editor viewport and produces "Uniform not supplied" errors.
/// </summary>
[Tool]
public partial class CloudShadows : MeshInstance3D
{
	private const int MaxSmokes = 40;

	private ShaderMaterial _mat;
	// Pre-cached StringName-Arrays für SetShaderParameter — sonst allokiert "smoke_tex_" + n pro
	// Frame × pro Smoke × pro Param = 120+ String-Allocs/sec wenn 3 Smokes aktiv. Plus StringName
	// (Godot-internal hashed key) ist schneller als string.
	private static readonly StringName[] _smokeTexParams = new StringName[MaxSmokes];
	private static readonly StringName[] _smokeMinParams = new StringName[MaxSmokes];
	private static readonly StringName[] _smokeSizeParams = new StringName[MaxSmokes];
	private static readonly StringName _smokeCountParam = "smoke_count";
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

	/// <summary>Caches the ShaderMaterial reference and warns if no override material is set.</summary>
	public override void _Ready()
	{
		_mat = MaterialOverride as ShaderMaterial;
		if (_mat == null)
			GD.PushWarning("[cloud_shadows] No ShaderMaterial as material_override — smoke mask inactive.");
	}

	/// <summary>Pushes the active smoke fields' density textures and bounds into the shader each frame.
	/// Pre-cached StringNames + skip wenn count unverändert UND 0 aktive Smokes → 0 SetShaderParameter
	/// Calls in der häufigsten Frame (no smokes deployed).</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("CloudShadows._Process");
		if (_mat == null) return;
		if (!Settings.CloudShadows) return;

		var active = SmokeVoxelField.Active;
		int n = 0;
		for (int i = 0; i < active.Count && n < MaxSmokes; i++)
		{
			SmokeVoxelField f = active[i];
			if (f.DensityTexture == null) continue;
			_mat.SetShaderParameter(_smokeTexParams[n], f.DensityTexture);
			_mat.SetShaderParameter(_smokeMinParams[n], f.GridMin);
			_mat.SetShaderParameter(_smokeSizeParams[n], f.GridSize);
			n++;
		}
		// Count nur updaten wenn geändert — der häufigste Fall ist 0 Smokes, dann skipt das alle 4 SetShaderParameter-Calls.
		if (n != _lastSmokeCount)
		{
			_mat.SetShaderParameter(_smokeCountParam, n);
			_lastSmokeCount = n;
		}
	}
}
