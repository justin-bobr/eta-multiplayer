using Godot;

// Muzzle flash + smoke + casing ejection, triggered per shot. Built procedurally so no texture assets
// are needed. Place a muzzle point (barrel tip) and an eject point (BoneAttachment to Eject_Casing).
[Tool, GlobalClass]
public partial class WeaponVFX : Node3D
{
	[Export] public NodePath MuzzlePointPath;
	[Export] public NodePath EjectPointPath;
	[Export] public bool EnableMuzzleFlash = true;
	[Export] public bool EnableSmoke = true;
	[Export] public bool EnableCasing = true;
	[Export(PropertyHint.Range, "0,1,0.01")] public float FlashSize = 0.13f;

	private GpuParticles3D _flash;
	private GpuParticles3D _smoke;
	private GpuParticles3D _casing;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		Node3D muzzle = GetNodeOrNull<Node3D>(MuzzlePointPath) ?? this;
		Node3D eject = GetNodeOrNull<Node3D>(EjectPointPath) ?? muzzle;
		if (EnableMuzzleFlash) _flash = BuildFlash(muzzle);
		if (EnableSmoke) _smoke = BuildSmoke(muzzle);
		if (EnableCasing) _casing = BuildCasing(eject);
	}

	public void Fire()
	{
		_flash?.Restart();
		_smoke?.Restart();
		_casing?.Restart();
	}

	private GpuParticles3D BuildFlash(Node3D parent)
	{
		var p = NewBurst(parent, amount: 12, lifetime: 0.05);
		var pm = new ParticleProcessMaterial
		{
			Direction = new Vector3(0, 0, -1),
			Spread = 22f,
			InitialVelocityMin = 1.0f,
			InitialVelocityMax = 3.0f,
			ScaleMin = 0.5f,
			ScaleMax = 1.5f,
			Gravity = Vector3.Zero,
			Color = new Color(1f, 0.78f, 0.32f),
		};
		p.ProcessMaterial = pm;
		p.DrawPass1 = Billboard(FlashSize, new Color(1f, 0.85f, 0.45f), emission: new Color(1f, 0.65f, 0.2f), energy: 8f, additive: true, fade: true);
		return p;
	}

	private GpuParticles3D BuildSmoke(Node3D parent)
	{
		var p = NewBurst(parent, amount: 7, lifetime: 1.3);
		var pm = new ParticleProcessMaterial
		{
			Direction = new Vector3(0, 0.25f, -1),
			Spread = 18f,
			InitialVelocityMin = 0.4f,
			InitialVelocityMax = 0.9f,
			Gravity = new Vector3(0, 0.25f, 0),
			ScaleMin = 0.6f,
			ScaleMax = 1.1f,
			Color = new Color(0.55f, 0.55f, 0.57f),
		};
		pm.ScaleCurve = Ramp1D(0.4f, 2.6f);
		p.ProcessMaterial = pm;
		p.DrawPass1 = Billboard(0.18f, new Color(0.6f, 0.6f, 0.62f, 0.5f), emission: default, energy: 0f, additive: false, fade: true);
		return p;
	}

	private GpuParticles3D BuildCasing(Node3D parent)
	{
		var p = NewBurst(parent, amount: 1, lifetime: 1.6);
		var pm = new ParticleProcessMaterial
		{
			Direction = new Vector3(1, 0.4f, 0),
			Spread = 12f,
			InitialVelocityMin = 1.6f,
			InitialVelocityMax = 2.4f,
			Gravity = new Vector3(0, -9.8f, 0),
			AngularVelocityMin = 400f,
			AngularVelocityMax = 720f,
			ScaleMin = 1f,
			ScaleMax = 1f,
		};
		p.ProcessMaterial = pm;
		var casing = new CylinderMesh { TopRadius = 0.004f, BottomRadius = 0.004f, Height = 0.014f, RadialSegments = 6, Rings = 1 };
		var mat = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.55f, 0.2f), Metallic = 0.9f, Roughness = 0.35f };
		casing.Material = mat;
		p.DrawPass1 = casing;
		return p;
	}

	private static GpuParticles3D NewBurst(Node3D parent, int amount, double lifetime)
	{
		var p = new GpuParticles3D
		{
			OneShot = true,
			Emitting = false,
			Amount = amount,
			Lifetime = lifetime,
			Explosiveness = 1f,
			LocalCoords = false,
		};
		parent.AddChild(p);
		return p;
	}

	private static QuadMesh Billboard(float size, Color albedo, Color emission, float energy, bool additive, bool fade)
	{
		var mat = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			BillboardKeepScale = true,
			AlbedoColor = albedo,
			DisableReceiveShadows = true,
		};
		if (energy > 0f)
		{
			mat.EmissionEnabled = true;
			mat.Emission = emission;
			mat.EmissionEnergyMultiplier = energy;
		}
		if (fade)
		{
			mat.VertexColorUseAsAlbedo = true; // particle color (incl. alpha fade) modulates the billboard
		}
		return new QuadMesh { Size = new Vector2(size, size), Material = mat };
	}

	// Linear scale-over-lifetime curve baked into a CurveTexture (start -> end).
	private static CurveTexture Ramp1D(float start, float end)
	{
		var c = new Curve();
		c.AddPoint(new Vector2(0f, start));
		c.AddPoint(new Vector2(1f, end));
		return new CurveTexture { Curve = c };
	}
}
