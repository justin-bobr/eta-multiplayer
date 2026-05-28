using System;
using System.Text;
using Godot;

/// <summary>
/// Adjusts the lighting of the viewmodel DirectionalLight (inside the SubViewport)
/// to match the surrounding world lighting in real time. Three sample sources
/// are mixed:
/// 1) Sun check (if <see cref="WorldSun"/> is set): a raycast toward the sun.
///    Open sky → add SunColor × SunEnergy × <see cref="SunInfluence"/>; hits
///    geometry → player is in shadow, no sun bonus. This is by far the most
///    important sample for "stand in the sun → weapon gets bright".
/// 2) Sky sample: raycasts upward and slightly upward. Sky hits = outdoor brightness.
/// 3) Ambient sample: raycasts left/right/forward. Geometry hits contribute
///    the material albedo as an environment tint (red wall → slightly red viewmodel).
/// Transitions are smoothed (<see cref="SmoothingSpeed"/> lerp per second) to
/// avoid flicker. Setting <see cref="DebugLog"/> = true prints state every
/// 0.5 s, including sun status, valid hits, target colour and energy.
/// </summary>
[Tool]
public partial class ViewmodelLightSampler : Node3D
{
	[Export] public DirectionalLight3D ViewmodelLight;
	[Export] public Camera3D MainCamera;
	[Export] public DirectionalLight3D WorldSun;
	[Export] public Vector3 SunDirectionWorld = new(0.3f, 0.85f, 0.45f);

	/// <summary>Optional secondary fill light from the opposite side of the key. AAA-style 3-point lighting: dim, slightly cool, fixed energy. Reduces the flat look on metal parts.</summary>
	[Export] public DirectionalLight3D FillLight;
	/// <summary>Optional rim light from behind the subject. Only activates when WorldSun is visible (backlight situation). Color matches sun + sky blend.</summary>
	[Export] public DirectionalLight3D RimLight;
	/// <summary>Optional WorldEnvironment of the actual world scene (NOT the viewmodel's own env). When set, the sampler reads its ambient colour and blends it into the viewmodel light. Auto-discovered at runtime if null.</summary>
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
	/// <summary>Constant energy for the fill light (3-point lighting: key + FILL + rim). 0.15 = subtle counter-shadow lift on the dark side of the gun.</summary>
	[Export] public float FillEnergy = 0.15f;
	/// <summary>Colour of the fill light. Slightly cool / blue-tinted by default — gives the gun a more cinematic look vs a uniform-coloured single light.</summary>
	[Export] public Color FillColor = new(0.92f, 0.95f, 1.0f, 1f);
	/// <summary>Max energy for the rim light when WorldSun is fully visible. 0.4 = clear outline glow on backlit gun silhouettes.</summary>
	[Export] public float RimMaxEnergy = 0.4f;
	/// <summary>Rim-light colour when WorldSun is occluded but Sky-Fallback applies (no direct backlight). Sky-blue gives a cool environment edge.</summary>
	[Export] public Color RimFallbackColor = new(0.85f, 0.9f, 1.0f, 1f);

	[ExportGroup("World Env Sync")]
	/// <summary>Blend weight from the raycast-derived ambient toward the world WorldEnvironment's AmbientLightColor. 0 = raycasts only (pre-Tier-2 behaviour), 1 = ignore raycasts and use only the world env. 0.5 = balanced mix. Architectural shift toward AAA-style "viewmodel follows scene env state".</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float WorldAmbientWeight = 0.5f;

	[ExportGroup("Debug")]
	[Export] public bool DebugLog = false;

	private Color _currentColor = Colors.White;
	private float _currentEnergy = 1.0f;
	private Color _targetColor = Colors.White;
	private float _targetEnergy = 1.0f;
	/// <summary>Smoothed light orientation — interpolated from _targetLightBasis each frame so the gun
	/// specular doesn't snap when the player walks out of shadow into sun. Was instant-set before;
	/// caused visible pop in the highlight position.</summary>
	private Basis _currentLightBasis = Basis.Identity;
	private Basis _targetLightBasis = Basis.Identity;
	private bool _lightBasisInitialised;
	private double _nextDebugAt;
	private double _nextSunRescanAt;
	private double _nextWorldEnvRescanAt;
	private double _sampleAccum;
	// 10Hz statt 20Hz — halbiert die Sample-Cost ohne sichtbare Reduktion der Reaktivität (Smoothing
	// gleicht jeden Tick eh aus). 20Hz war ~30ms/sec overhead, 10Hz ~15ms/sec.
	private const double SampleInterval = 1.0 / 10.0;
	private Godot.Collections.Array<Rid> _excludeBodies;

	// Pre-allokierte PhysicsRayQueryParameters3D — wird pro Sample mutiert statt new'd. Sparte
	// ~6 Allokationen pro Sample × 10Hz = 60 GC-Allocs/sec eliminiert.
	private PhysicsRayQueryParameters3D _reusableQuery;
	private PhysicsRayQueryParameters3D _reusableSunQuery;

	/// <summary>Samples world lighting at 20 Hz while smoothing the result every tick, and applies it to the viewmodel light.</summary>
	public override void _PhysicsProcess(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("ViewmodelLightSampler._PhysicsProcess");
		if (Engine.IsEditorHint()) return;
		if (ViewmodelLight == null || MainCamera == null) return;

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

		// Query-Object reusen statt new pro Ray (= 5× weniger GC-Allocs pro Sample). CollideWithAreas
		// + Mask einmal gesetzt, dann nur From/To mutieren.
		if (_reusableQuery == null)
		{
			_reusableQuery = new PhysicsRayQueryParameters3D { CollideWithAreas = false };
		}

		for (int i = 0; i < dirs.Length; i++)
		{
			Vector3 dir = dirs[i].Normalized();
			_reusableQuery.From = origin;
			_reusableQuery.To = origin + dir * SampleDistance;
			Godot.Collections.Dictionary hit = space.IntersectRay(_reusableQuery);

			if (hit.Count == 0)
			{
				colorSum += SkyFallbackColor;
				energySum += SkyFallbackEnergy;
				skyHits++;
			}
			else
			{
				colorSum += SampleMaterialColor(hit);
				energySum += 0.4f;
			}
			validHits++;
		}

		float sunVisibility = 0f;
		Color sunColor = Colors.White;
		float sunBonus = 0f;
		if (WorldSun != null)
		{
			var excludes = GetPlayerBodyExcludes();
			// Dynamic sun direction from the actual WorldSun rotation. Godot DLs shine along
			// -Basis.Z, so the direction "toward the light source" (= what we want to raycast
			// for sky visibility) is +Basis.Z. Falls back to the legacy hardcoded vector if
			// the transform somehow returns a zero basis.
			Vector3 sunDir = WorldSun.GlobalTransform.Basis.Z;
			if (sunDir.LengthSquared() < 0.001f) sunDir = SunDirectionWorld;
			sunDir = sunDir.Normalized();

			// Reuse query für sun-rays (Exclude muss gesetzt sein, anders als _reusableQuery).
			if (_reusableSunQuery == null)
				_reusableSunQuery = new PhysicsRayQueryParameters3D { CollideWithAreas = false };
			_reusableSunQuery.From = origin;
			_reusableSunQuery.To = origin + Vector3.Up * SunRayDistance;
			_reusableSunQuery.Exclude = excludes;
			bool upOpen = space.IntersectRay(_reusableSunQuery).Count == 0;

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
					// Reuse _reusableSunQuery — Exclude bleibt schon gesetzt aus dem up-Ray oben.
					_reusableSunQuery.From = origin;
					_reusableSunQuery.To = origin + sunDirs[i] * SunRayDistance;
					if (space.IntersectRay(_reusableSunQuery).Count == 0) openCount++;
				}
				sunVisibility = openCount / 5f;
			}

			if (sunVisibility > 0f)
			{
				sunColor = WorldSun.LightColor;
				sunBonus = WorldSun.LightEnergy * SunInfluence * sunVisibility;
			}

			// Compute the TARGET ViewmodelLight orientation — actually applied via smoothed
			// slerp in ApplySmoothing(), so highlights don't snap when the player transitions
			// shadow→sun. The target is the world sun direction expressed in the main
			// camera's local space (since the viewmodel scene is camera-aligned).
			if (ViewmodelLight != null)
			{
				Basis camBasis = MainCamera.GlobalTransform.Basis;
				Vector3 sunLocal = (camBasis.Inverse() * sunDir).Normalized();
				// Construct an orthonormal basis with Z = sunLocal (DirectionalLight shines
				// along -Z, so Z = "toward sun" gives light coming from the sun direction).
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

		// Tier-2 architectural shift: blend raycast-derived ambient toward the actual world
		// WorldEnvironment's AmbientLightColor. AAA pattern is "viewmodel light reflects
		// world env state directly" rather than purely sniffing local geometry. Weight 0..1
		// controls the mix; 0.5 default = raycasts still matter for local material tinting,
		// but the overall mood follows whatever the level designer set on the env (warm
		// outdoor amber on dust2, neutral office grey on a different map, etc).
		if (WorldEnv?.Environment is Godot.Environment env && WorldAmbientWeight > 0f)
		{
			Color worldAmb = env.AmbientLightColor;
			ambientColor = ambientColor.Lerp(worldAmb, WorldAmbientWeight);
		}

		_targetColor = sunVisible ? ambientColor.Lerp(sunColor, 0.6f) : ambientColor;
		_targetEnergy = Mathf.Clamp(ambientEnergy + sunBonus, MinEnergy, MaxEnergy);

		// 3-point lighting: drive Fill + Rim alongside the main Key light.
		// Fill modulates with sky-visibility (skyHits / validHits): when the player is
		// indoor / under a roof, fill drops to 30% of full so it doesn't add a noticeable
		// blue/cool cast on top of an already-dim key light. Outdoor with full sky exposure
		// it goes to 100% for proper 3-point look. Without this modulation the fill
		// dominated in tunnels and tinted the gun blue.
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

	/// <summary>
	/// Interpolates _current* toward _target* and applies the result to ViewmodelLight.
	/// Called every 60 Hz tick — even when the underlying sampling runs at 20 Hz —
	/// so transitions stay smooth.
	/// </summary>
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
			// Rotation slerp at HALF the speed of energy — direction changes are bigger
			// visual events than energy changes, so they need a gentler ramp. Quaternion
			// slerp avoids gimbal weirdness on the basis interpolation.
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

	/// <summary>Reads the albedo colour of the hit mesh as an environment hint. Supports
	/// StandardMaterial3D plus three ShaderMaterial conventions: Godot-default `albedo`
	/// Color parameter, and the Source-2 / csgo_complex pattern of `global_tint` ×
	/// `model_tint`. Returns gray if no recognised Color parameter is exposed.</summary>
	private static Color SampleMaterialColor(Godot.Collections.Dictionary hit)
	{
		if (hit["collider"].AsGodotObject() is not Node hitNode) return Colors.Gray;

		MeshInstance3D mesh = FindFirstMesh(hitNode);
		if (mesh == null || mesh.Mesh == null || mesh.Mesh.GetSurfaceCount() == 0) return Colors.Gray;

		Material mat = mesh.GetActiveMaterial(0);
		if (mat is StandardMaterial3D std) return std.AlbedoColor;
		if (mat is ShaderMaterial sm)
		{
			Variant val = sm.GetShaderParameter("albedo");
			if (val.VariantType == Variant.Type.Color) return val.AsColor();
			// Source-2 / csgo_complex convention: ApplyTint runs Mix.013 = global × model.
			// Approximate the effective albedo tint with the same product (texture content
			// is unknown at raycast-time, so we use the tint multipliers as a proxy).
			Variant gt = sm.GetShaderParameter("global_tint");
			Variant mt = sm.GetShaderParameter("model_tint");
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

	/// <summary>
	/// Caches the CollisionObject3D RIDs of all ancestors of MainCamera (typically the
	/// player's CharacterBody3D). Excluded from the sun raycast so the player does not
	/// count as a shadow caster against themselves.
	/// </summary>
	private Godot.Collections.Array<Rid> GetPlayerBodyExcludes()
	{
		if (_excludeBodies != null) return _excludeBodies;
		_excludeBodies = new Godot.Collections.Array<Rid>();
		Node n = MainCamera;
		while (n != null)
		{
			if (n is CollisionObject3D co) _excludeBodies.Add(co.GetRid());
			n = n.GetParent();
		}
		return _excludeBodies;
	}

	/// <summary>
	/// Finds the world sun: the first DirectionalLight3D in the tree that is NOT the
	/// viewmodel light and lives in the same World3D as the main camera.
	/// </summary>
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

	/// <summary>Finds the world WorldEnvironment — the one with a Compositor set (= the
	/// actual world env, not the viewmodel's own sub-resource env). Falls back to the
	/// first WorldEnvironment found if none have a Compositor.</summary>
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

	/// <summary>Recursively locates the first MeshInstance3D associated with a collider node.</summary>
	private static MeshInstance3D FindFirstMesh(Node n)
	{
		if (n is MeshInstance3D mi) return mi;
		if (n.GetParent() is Node parent)
			foreach (Node c in parent.GetChildren())
				if (c is MeshInstance3D mp) return mp;
		foreach (Node c in n.GetChildren())
		{
			MeshInstance3D r = FindFirstMesh(c);
			if (r != null) return r;
		}
		return null;
	}
}
