using System;
using System.Text;
using Godot;

/// <summary>
/// Drives the viewmodel DirectionalLight to match world lighting in real time by mixing three raycast
/// samples: a sun check (open sky adds <see cref="SunInfluence"/>, geometry = in shadow), an upward sky
/// sample for outdoor brightness, and left/right/forward ambient samples that tint by hit-material albedo.
/// Transitions are smoothed by <see cref="SmoothingSpeed"/>; <see cref="DebugLog"/> prints state every 0.5 s.
/// </summary>
[Tool]
public partial class ViewmodelLightSampler : Node3D
{
	[Export] public DirectionalLight3D ViewmodelLight;
	[Export] public Camera3D MainCamera;
	[Export] public DirectionalLight3D WorldSun;
	[Export] public Vector3 SunDirectionWorld = new(0.3f, 0.85f, 0.45f);

	/// <summary>Optional fill light opposite the key (dim, fixed energy).</summary>
	[Export] public DirectionalLight3D FillLight;
	/// <summary>Optional rim light; only active when WorldSun is visible.</summary>
	[Export] public DirectionalLight3D RimLight;
	/// <summary>Optional world-scene WorldEnvironment (not the viewmodel's own) whose ambient colour is blended in. Auto-discovered if null.</summary>
	[Export] public WorldEnvironment WorldEnv;

	[ExportGroup("Sampling")]
	[Export] public float SampleDistance = 8.0f;
	[Export] public float SunRayDistance = 100.0f;
	[Export] public float Intensity = 0.1f;
	[Export] public float SunInfluence = 0.35f;
	[Export] public float MinEnergy = 0.05f;
	[Export] public float MaxEnergy = 1.0f;
	[Export] public float SmoothingSpeed = 25.0f;
	[Export] public Color SkyFallbackColor = new(1.0f, 0.95f, 0.85f, 1f);
	[Export] public float SkyFallbackEnergy = 0.6f;

	[ExportGroup("3-Point Lighting")]
	/// <summary>Constant fill-light energy.</summary>
	[Export] public float FillEnergy = 0.15f;
	/// <summary>Fill-light colour (slightly cool by default).</summary>
	[Export] public Color FillColor = new(0.92f, 0.95f, 1.0f, 1f);
	/// <summary>Rim-light energy at full sun visibility.</summary>
	[Export] public float RimMaxEnergy = 0.4f;
	/// <summary>Rim-light colour when the sun is occluded (sky-fallback edge).</summary>
	[Export] public Color RimFallbackColor = new(0.85f, 0.9f, 1.0f, 1f);

	[ExportGroup("World Env Sync")]
	/// <summary>Blend from raycast ambient (0) toward the world env's AmbientLightColor (1).</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float WorldAmbientWeight = 0.5f;

	[ExportGroup("Debug")]
	[Export] public bool DebugLog = false;

	private Color _currentColor = Colors.White;
	private float _currentEnergy = 1.0f;
	private Color _targetColor = Colors.White;
	private float _targetEnergy = 1.0f;
	/// <summary>Smoothed light orientation, slerped toward _targetLightBasis so the specular doesn't snap.</summary>
	private Basis _currentLightBasis = Basis.Identity;
	private Basis _targetLightBasis = Basis.Identity;
	private bool _lightBasisInitialised;
	private double _nextDebugAt;
	private double _nextSunRescanAt;
	private double _nextWorldEnvRescanAt;
	private double _sampleAccum;
	private const double SampleInterval = 1.0 / 10.0;
	private RayCast3D[] _ambientCasts;
	private RayCast3D _sunUpCast;
	private RayCast3D[] _sunConeCasts;

	private static readonly StringName _pAlbedo = "albedo";
	private static readonly StringName _pGlobalTint = "global_tint";
	private static readonly StringName _pModelTint = "model_tint";

	private readonly System.Collections.Generic.Dictionary<ulong, Color> _materialColorCache = new();

	private bool _capturedDefault;
	private float _defaultEnergy;
	private Color _defaultColor;
	private bool _disabledApplied;

	/// <summary>Samples world lighting at 20 Hz while smoothing the result every tick, and applies it to the viewmodel light.</summary>
	public override void _PhysicsProcess(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("ViewmodelLightSampler._PhysicsProcess");
		if (Engine.IsEditorHint()) return;
		if (ViewmodelLight == null || MainCamera == null) return;

		if (!_capturedDefault)
		{
			_defaultEnergy = ViewmodelLight.LightEnergy;
			_defaultColor = ViewmodelLight.LightColor;
			_capturedDefault = true;
		}
		if (!Settings.WeaponLight)
		{
			if (!_disabledApplied)
			{
				ViewmodelLight.LightEnergy = _defaultEnergy;
				ViewmodelLight.LightColor = _defaultColor;
				_disabledApplied = true;
			}
			return;
		}
		_disabledApplied = false;

		_sampleAccum += delta;
		if (_sampleAccum < SampleInterval)
		{
			ApplySmoothing(delta);
			return;
		}
		_sampleAccum -= SampleInterval;

		double now = Time.GetTicksMsec() / 1000.0;
		if (WorldSun == null && now >= _nextSunRescanAt)
		{
			_nextSunRescanAt = now + 2.0;
			WorldSun = FindWorldSun(GetTree()?.Root, MainCamera.GetWorld3D());
			if (WorldSun != null) Dbg.Print($"[vm-light] WorldSun auto-found: {WorldSun.Name}");
		}
		if (WorldEnv == null && now >= _nextWorldEnvRescanAt)
		{
			_nextWorldEnvRescanAt = now + 2.0;
			WorldEnv = FindWorldEnvironment(GetTree()?.Root);
			if (WorldEnv != null) Dbg.Print($"[vm-light] WorldEnv auto-found: {WorldEnv.Name}");
		}

		Basis basis = MainCamera.GlobalTransform.Basis;
		Vector3 origin = MainCamera.GlobalPosition;

		PhysicsDirectSpaceState3D space = MainCamera.GetWorld3D().DirectSpaceState;

		Span<Vector3> dirs = stackalloc Vector3[5]
		{
			-basis.Z,
			-basis.X,
			 basis.X,
			 Vector3.Up,
			-basis.Z + Vector3.Up * 0.3f,
		};

		Color colorSum = Colors.Black;
		float energySum = 0f;
		int skyHits = 0;
		int validHits = 0;

		EnsureCastNodes();

		for (int i = 0; i < dirs.Length; i++)
		{
			Vector3 dir = dirs[i].Normalized();
			RayCast3D rc = _ambientCasts[i];
			rc.GlobalTransform = new Transform3D(Basis.Identity, origin);
			rc.TargetPosition = dir * SampleDistance;
			rc.ForceRaycastUpdate();

			if (!rc.IsColliding())
			{
				colorSum += SkyFallbackColor;
				energySum += SkyFallbackEnergy;
				skyHits++;
			}
			else
			{
				colorSum += SampleColliderColorCached(rc.GetCollider() as Node);
				energySum += 0.4f;
			}
			validHits++;
		}

		float sunVisibility = 0f;
		Color sunColor = Colors.White;
		float sunBonus = 0f;
		if (WorldSun != null)
		{
			Vector3 sunDir = WorldSun.GlobalTransform.Basis.Z;
			if (sunDir.LengthSquared() < 0.001f) sunDir = SunDirectionWorld;
			sunDir = sunDir.Normalized();

			_sunUpCast.GlobalTransform = new Transform3D(Basis.Identity, origin);
			_sunUpCast.TargetPosition = Vector3.Up * SunRayDistance;
			_sunUpCast.ForceRaycastUpdate();
			bool upOpen = !_sunUpCast.IsColliding();

			if (upOpen)
			{
				Vector3 tangent1 = sunDir.Cross(Vector3.Up).Normalized();
				if (tangent1.LengthSquared() < 0.001f) tangent1 = Vector3.Right;
				Vector3 tangent2 = sunDir.Cross(tangent1).Normalized();

				Span<Vector3> sunDirs = stackalloc Vector3[5];
				sunDirs[0] = sunDir;
				sunDirs[1] = (sunDir + tangent1 * 0.08f).Normalized();
				sunDirs[2] = (sunDir - tangent1 * 0.08f).Normalized();
				sunDirs[3] = (sunDir + tangent2 * 0.08f).Normalized();
				sunDirs[4] = (sunDir - tangent2 * 0.08f).Normalized();

				int openCount = 0;
				for (int i = 0; i < 5; i++)
				{
					RayCast3D rc = _sunConeCasts[i];
					rc.GlobalTransform = new Transform3D(Basis.Identity, origin);
					rc.TargetPosition = sunDirs[i] * SunRayDistance;
					rc.ForceRaycastUpdate();
					if (!rc.IsColliding()) openCount++;
				}
				sunVisibility = openCount / 5f;
			}

			if (sunVisibility > 0f)
			{
				sunColor = WorldSun.LightColor;
				sunBonus = WorldSun.LightEnergy * SunInfluence * sunVisibility;
			}

			if (ViewmodelLight != null)
			{
				Basis camBasis = MainCamera.GlobalTransform.Basis;
				Vector3 sunLocal = (camBasis.Inverse() * sunDir).Normalized();
				Vector3 worldUp = Vector3.Up;
				if (Mathf.Abs(sunLocal.Dot(worldUp)) > 0.99f) worldUp = Vector3.Right;
				Vector3 xAxis = worldUp.Cross(sunLocal).Normalized();
				Vector3 yAxis = sunLocal.Cross(xAxis).Normalized();
				_targetLightBasis = new Basis(xAxis, yAxis, sunLocal);
				if (!_lightBasisInitialised)
				{
					_currentLightBasis = _targetLightBasis;
					_lightBasisInitialised = true;
				}
			}
		}
		bool sunVisible = sunVisibility > 0.5f;

		if (validHits == 0) return;
		Color ambientColor = colorSum / validHits;
		float ambientEnergy = energySum / validHits * Intensity;

		if (WorldEnv?.Environment is Godot.Environment env && WorldAmbientWeight > 0f)
		{
			Color worldAmb = env.AmbientLightColor;
			ambientColor = ambientColor.Lerp(worldAmb, WorldAmbientWeight);
		}

		_targetColor = sunVisible ? ambientColor.Lerp(sunColor, 0.6f) : ambientColor;
		_targetEnergy = Mathf.Clamp(ambientEnergy + sunBonus, MinEnergy, MaxEnergy);

		float skyHitsRatio = validHits > 0 ? (float)skyHits / validHits : 0f;
		float fillScale = 0.3f + 0.7f * skyHitsRatio;
		if (FillLight != null)
		{
			FillLight.LightColor = FillColor;
			FillLight.LightEnergy = FillEnergy * fillScale;
		}
		if (RimLight != null)
		{
			float rimE = sunVisibility * RimMaxEnergy;
			Color rimC = sunVisible ? sunColor.Lerp(RimFallbackColor, 0.4f) : RimFallbackColor;
			RimLight.LightColor = rimC;
			RimLight.LightEnergy = rimE;
		}

		ApplySmoothing(delta);

		if (DebugLog)
		{
			if (now >= _nextDebugAt)
			{
				_nextDebugAt = now + 0.5;
				var sb = new StringBuilder();
				sb.Append($"[vm-light] sun={(sunVisible ? "YES" : "no")} ");
				sb.Append($"skyHits={skyHits}/{validHits} ");
				sb.Append($"ambEnergy={ambientEnergy:F2} sunBonus={sunBonus:F2} ");
				sb.Append($"target=({_targetColor.R:F2},{_targetColor.G:F2},{_targetColor.B:F2}) E={_targetEnergy:F2} ");
				sb.Append($"applied=E={_currentEnergy:F2}");
				Dbg.Print(sb.ToString());
			}
		}
	}

	/// <summary>Lazy-allocates the 11 RayCast3D probes, adding the player's colliders as exceptions so sun-cone rays don't self-intersect.</summary>
	private void EnsureCastNodes()
	{
		if (_ambientCasts != null) return;

		_ambientCasts = new RayCast3D[5];
		for (int i = 0; i < 5; i++) _ambientCasts[i] = CreateProbe($"vm_amb_{i}");
		_sunUpCast = CreateProbe("vm_sun_up");
		_sunConeCasts = new RayCast3D[5];
		for (int i = 0; i < 5; i++) _sunConeCasts[i] = CreateProbe($"vm_sun_{i}");

		Node n = MainCamera;
		while (n != null)
		{
			if (n is CollisionObject3D co)
			{
				for (int i = 0; i < 5; i++) _ambientCasts[i].AddException(co);
				_sunUpCast.AddException(co);
				for (int i = 0; i < 5; i++) _sunConeCasts[i].AddException(co);
			}
			n = n.GetParent();
		}
	}

	/// <summary>Spawns a world-space RayCast3D probe. Mask covers world layers 1 + 20 only; player/hitbox layers stay blind so puppets don't block sun-visibility or pollute ambient samples.</summary>
	private const uint WorldCollisionMask = 1u | (1u << 19);
	private RayCast3D CreateProbe(string name)
	{
		var rc = new RayCast3D
		{
			Name = name,
			Enabled = true,
			TopLevel = true,
			CollideWithAreas = false,
			CollideWithBodies = true,
			CollisionMask = WorldCollisionMask,
		};
		MainCamera.AddChild(rc);
		return rc;
	}

	/// <summary>Interpolates _current* toward _target* and applies it to ViewmodelLight. Runs every tick even though sampling is 10 Hz.</summary>
	private void ApplySmoothing(double delta)
	{
		if (SmoothingSpeed <= 0f)
		{
			_currentColor = _targetColor;
			_currentEnergy = _targetEnergy;
			_currentLightBasis = _targetLightBasis;
		}
		else
		{
			float tEnergy = Mathf.Min(1f, (float)delta * SmoothingSpeed * 1.5f);
			float tColor  = Mathf.Min(1f, (float)delta * SmoothingSpeed);
			float tBasis  = Mathf.Min(1f, (float)delta * SmoothingSpeed * 0.5f);
			_currentColor = _currentColor.Lerp(_targetColor, tColor);
			_currentEnergy = Mathf.Lerp(_currentEnergy, _targetEnergy, tEnergy);
			if (_lightBasisInitialised)
			{
				Quaternion cur = _currentLightBasis.GetRotationQuaternion();
				Quaternion tgt = _targetLightBasis.GetRotationQuaternion();
				_currentLightBasis = new Basis(cur.Slerp(tgt, tBasis));
			}
		}
		ViewmodelLight.LightColor = _currentColor;
		ViewmodelLight.LightEnergy = _currentEnergy;
		if (_lightBasisInitialised)
			ViewmodelLight.Transform = new Transform3D(_currentLightBasis, Vector3.Zero);
	}

	/// <summary>Caches <see cref="SampleMaterialColor"/> by collider InstanceId so material reads happen only on the first hit.</summary>
	private Color SampleColliderColorCached(Node collider)
	{
		if (collider == null) return Colors.Gray;
		ulong id = collider.GetInstanceId();
		if (_materialColorCache.TryGetValue(id, out Color cached))
			return cached;
		Color c = SampleMaterialColor(collider);
		_materialColorCache[id] = c;
		return c;
	}

	/// <summary>Reads the hit mesh's albedo as an environment hint (StandardMaterial3D, or shader `albedo` / `global_tint`x`model_tint`); gray if none.</summary>
	private static Color SampleMaterialColor(Node hitNode)
	{
		if (hitNode == null) return Colors.Gray;

		MeshInstance3D mesh = FindFirstMesh(hitNode);
		if (mesh == null || mesh.Mesh == null || mesh.Mesh.GetSurfaceCount() == 0) return Colors.Gray;

		Material mat = mesh.GetActiveMaterial(0);
		if (mat is StandardMaterial3D std) return std.AlbedoColor;
		if (mat is ShaderMaterial sm)
		{
			Variant val = sm.GetShaderParameter(_pAlbedo);
			if (val.VariantType == Variant.Type.Color) return val.AsColor();
			Variant gt = sm.GetShaderParameter(_pGlobalTint);
			Variant mt = sm.GetShaderParameter(_pModelTint);
			bool hasGt = gt.VariantType == Variant.Type.Color;
			bool hasMt = mt.VariantType == Variant.Type.Color;
			if (hasGt && hasMt)
			{
				Color a = gt.AsColor();
				Color b = mt.AsColor();
				return new Color(a.R * b.R, a.G * b.G, a.B * b.B);
			}
			if (hasGt) return gt.AsColor();
			if (hasMt) return mt.AsColor();
		}
		return Colors.Gray;
	}

	/// <summary>Finds the world sun: the first DirectionalLight3D (not the viewmodel/fill/rim lights) in the camera's World3D.</summary>
	private DirectionalLight3D FindWorldSun(Node n, World3D worldOfCamera)
	{
		if (n == null) return null;
		if (n is DirectionalLight3D dl && dl != ViewmodelLight && dl != FillLight && dl != RimLight)
		{
			if (dl.GetWorld3D() == worldOfCamera) return dl;
		}
		foreach (Node c in n.GetChildren())
		{
			DirectionalLight3D r = FindWorldSun(c, worldOfCamera);
			if (r != null) return r;
		}
		return null;
	}

	/// <summary>Finds the world WorldEnvironment (the one with a Compositor), else the first found.</summary>
	private WorldEnvironment FindWorldEnvironment(Node n)
	{
		if (n == null) return null;
		WorldEnvironment fallback = null;
		foreach (Node c in n.GetChildren())
		{
			if (c is WorldEnvironment we)
			{
				if (we.Compositor != null) return we;
				fallback ??= we;
			}
			WorldEnvironment r = FindWorldEnvironment(c);
			if (r != null)
			{
				if (r.Compositor != null) return r;
				fallback ??= r;
			}
		}
		return fallback;
	}

	/// <summary>Locates the first MeshInstance3D for a collider, iterating by child index to stay allocation-free on the per-ray hot path.</summary>
	private static MeshInstance3D FindFirstMesh(Node n)
	{
		if (n is MeshInstance3D mi) return mi;
		if (n.GetParent() is Node parent)
		{
			int siblingCount = parent.GetChildCount();
			for (int i = 0; i < siblingCount; i++)
				if (parent.GetChild(i) is MeshInstance3D mp) return mp;
		}
		int childCount = n.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			MeshInstance3D r = FindFirstMesh(n.GetChild(i));
			if (r != null) return r;
		}
		return null;
	}
}
