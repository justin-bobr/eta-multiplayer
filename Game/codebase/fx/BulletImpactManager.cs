using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Vantix.Fx;

/// <summary>
/// Spawns a bullet-hole Decal3D and spark/dust GPUParticles3D at each hit point.
/// Surface material comes from the collider's Godot group (e.g. "metal"); decal is skipped when no texture is configured.
/// </summary>
public partial class BulletImpactManager : Node3D
{
	public static BulletImpactManager Instance;

	/// <summary>Logs per-impact CPU time of decal and particle spawning.</summary>
	[Export] public bool LogImpactTiming = true;

	[ExportGroup("Decal Sets (Arrays — random pick per hit)")]
	[Export] public Godot.Collections.Array<BulletDecalSet> DefaultDecals = new();
	[Export] public Godot.Collections.Array<BulletDecalSet> MetalDecals = new();
	[Export] public Godot.Collections.Array<BulletDecalSet> WoodDecals = new();
	[Export] public Godot.Collections.Array<BulletDecalSet> ConcreteDecals = new();
	[Export] public Godot.Collections.Array<BulletDecalSet> GlassDecals = new();

	[ExportGroup("Decal Settings")]
	[Export] public Vector2 DecalSize = new(0.18f, 0.18f);
	[Export] public float DecalDepth = 0.25f;
	[Export] public float DecalLifetime = 30f;
	/// <summary>Distance fade hides depth-precision flicker on far decals and saves render cost.</summary>
	[Export] public bool DecalDistanceFade = true;
	[Export] public float DecalFadeBegin = 16f;
	[Export] public float DecalFadeLength = 9f;

	[ExportGroup("Random Variation")]
	[Export(PropertyHint.Range, "0.3,1,0.05")] public float ScaleMin = 0.85f;
	[Export(PropertyHint.Range, "1,2,0.05")] public float ScaleMax = 1.20f;
	[Export] public bool RandomRotation = true;

	private RandomNumberGenerator _rng = new();

	[ExportGroup("Particle Settings")]
	[Export] public float ParticleLifetime = 0.6f;
	[Export] public float ParticleSpread = 35f;
	[Export] public float ParticleSpeedMin = 2.0f;
	[Export] public float ParticleSpeedMax = 5.0f;
	[Export] public float ParticleScale = 1.0f;

	/// <summary>Registers the singleton, starts async decal prewarm, and pre-allocates the Decal + Particle
	/// pools (deferred so SceneTree.Root is reachable). Avoids a first-shot hitch.</summary>
	public override void _Ready()
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server) { QueueFree(); return; }
		Instance = this;
		PrewarmDecalsAsync();
		CallDeferred(MethodName.PrewarmPools);
	}

	private void PrewarmPools()
	{
		EnsureDecalPool();
		EnsureParticlePool();
	}

	/// <summary>Warms all decal sets on a background thread at level load so texture packing doesn't run
	/// on the main thread on the first shot. Lazy path covers the gap until prewarm finishes (lock-guarded).</summary>
	private void PrewarmDecalsAsync()
	{
		var sets = new HashSet<BulletDecalSet>();
		foreach (var pool in new[] { DefaultDecals, MetalDecals, WoodDecals, ConcreteDecals, GlassDecals })
			if (pool != null)
				foreach (var s in pool)
					if (s != null) sets.Add(s);
		if (sets.Count == 0) return;

		Task.Run(() =>
		{
			foreach (var s in sets)
			{
				try { s.GetEffectiveOrm(); s.GetEffectiveAlbedo(); }
				catch (Exception e) { GD.PushError($"[BulletImpactManager] decal prewarm failed: {e.Message}"); }
			}
			Dbg.Print($"[BulletImpactManager] {sets.Count} decal set(s) prepared async");
		});
	}

	/// <summary>Clears the singleton and frees the pools. Pool nodes are parented to SceneTree.Root
	/// (persists across scenes), so they must be freed manually or they leak on every scene reload.</summary>
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (_decalPool != null)
		{
			for (int i = 0; i < _decalPool.Length; i++)
				if (GodotObject.IsInstanceValid(_decalPool[i])) _decalPool[i].QueueFree();
			_decalPool = null;
			_decalExpiryMs = null;
			_decalCursor = 0;
		}
		if (_particlePool != null)
		{
			for (int i = 0; i < _particlePool.Length; i++)
				if (GodotObject.IsInstanceValid(_particlePool[i])) _particlePool[i].QueueFree();
			_particlePool = null;
			_particleProcMats = null;
			_particleExpiryMs = null;
			_particleCursor = 0;
		}
	}

	private float _recycleAccum;
	private const float RecycleInterval = 0.25f;

	/// <summary>Hides decals and stops particles whose expiry has elapsed, throttled to <see cref="RecycleInterval"/>.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("BulletImpactManager._Process");
		_recycleAccum += (float)delta;
		if (_recycleAccum < RecycleInterval) return;
		_recycleAccum = 0f;
		ulong now = Time.GetTicksMsec();

		if (_decalPool != null)
			for (int i = 0; i < _decalPool.Length; i++)
				if (_decalExpiryMs[i] != 0 && now >= _decalExpiryMs[i])
				{
					_decalPool[i].Visible = false;
					_decalExpiryMs[i] = 0;
				}

		if (_particlePool != null)
			for (int i = 0; i < _particlePool.Length; i++)
				if (_particleExpiryMs[i] != 0 && now >= _particleExpiryMs[i])
				{
					_particlePool[i].Visible = false;
					_particlePool[i].Emitting = false;
					_particleExpiryMs[i] = 0;
				}
	}

	/// <summary>Spawns an impact: decal (when a texture is available) plus material-coded particles.</summary>
	public void Spawn(Vector3 pos, Vector3 normal, StringName material)
	{
		if (!LogImpactTiming)
		{
			SpawnDecal(pos, normal, material);
			SpawnParticles(pos, normal, material);
			return;
		}
		ulong t0 = Time.GetTicksUsec();
		SpawnDecal(pos, normal, material);
		ulong t1 = Time.GetTicksUsec();
		SpawnParticles(pos, normal, material);
		ulong t2 = Time.GetTicksUsec();
		Dbg.Print($"[impact] decal={(t1 - t0) / 1000.0:F1}ms particles={(t2 - t1) / 1000.0:F1}ms (mat={material})");
	}

	/// <summary>Random-picks a valid set from the pool, trying others in circular order; null if none valid.</summary>
	private BulletDecalSet TryPickValid(Godot.Collections.Array<BulletDecalSet> pool)
	{
		if (pool == null || pool.Count == 0) return null;
		int start = _rng.RandiRange(0, pool.Count - 1);
		for (int i = 0; i < pool.Count; i++)
		{
			var s = pool[(start + i) % pool.Count];
			if (s != null && s.Albedo != null) return s;
		}
		return null;
	}

	[Export(PropertyHint.Range, "16,256,1")] public int DecalPoolSize = 96;
	private Decal[] _decalPool;
	private ulong[] _decalExpiryMs;
	private int _decalCursor;

	private void EnsureDecalPool()
	{
		if (_decalPool != null) return;
		_decalPool = new Decal[DecalPoolSize];
		_decalExpiryMs = new ulong[DecalPoolSize];
		var root = GetTree().Root;
		for (int i = 0; i < DecalPoolSize; i++)
		{
			var d = new Decal
			{
				Size = new Vector3(DecalSize.X, DecalDepth, DecalSize.Y),
				DistanceFadeEnabled = DecalDistanceFade,
				DistanceFadeBegin = DecalFadeBegin,
				DistanceFadeLength = DecalFadeLength,
				Visible = false,
			};
			root.AddChild(d);
			d.Owner = null;
			_decalPool[i] = d;
		}
	}

	/// <summary>Spawns the decal node for the given hit using the material-specific pool with default fallback.</summary>
	private void SpawnDecal(Vector3 pos, Vector3 normal, StringName material)
	{
		if (material == "flesh") return;

		Godot.Collections.Array<BulletDecalSet> specificPool = material.ToString() switch
		{
			"metal" => MetalDecals,
			"wood" => WoodDecals,
			"concrete" => ConcreteDecals,
			"glass" => GlassDecals,
			_ => null,
		};

		var set = TryPickValid(specificPool) ?? TryPickValid(DefaultDecals);
		if (set == null) return;

		EnsureDecalPool();
		float scale = (float)_rng.RandfRange(ScaleMin, ScaleMax);
		float rotAngle = RandomRotation ? (float)_rng.RandfRange(0f, Mathf.Tau) : 0f;

		int slot = _decalCursor;
		_decalCursor = (_decalCursor + 1) % _decalPool.Length;
		var decal = _decalPool[slot];

		decal.TextureAlbedo = set.GetEffectiveAlbedo();
		decal.TextureNormal = set.Normal;
		decal.TextureOrm = set.GetEffectiveOrm();
		decal.TextureEmission = set.Emission;
		decal.Modulate = set.Modulate;
		decal.AlbedoMix = set.AlbedoMix;
		decal.NormalFade = set.NormalFade;
		decal.Size = new Vector3(DecalSize.X * scale, DecalDepth, DecalSize.Y * scale);

		Basis basis = BasisFromNormal(normal);
		if (rotAngle != 0f) basis = basis.Rotated(normal.Normalized(), rotAngle);
		decal.GlobalTransform = new Transform3D(basis, pos + normal * 0.005f);
		decal.Visible = true;
		_decalExpiryMs[slot] = Time.GetTicksMsec() + (ulong)(DecalLifetime * 1000f);
	}

	/// <summary>Cached, reusable rendering resources for one particle material profile.</summary>
	private class MaterialCache
	{
		public QuadMesh DrawMesh;
		public StandardMaterial3D DrawMat;
		public GradientTexture1D ColorRamp;
		public CurveTexture ScaleCurve;
	}
	private static readonly System.Collections.Generic.Dictionary<string, MaterialCache> _matCache = new();

	/// <summary>Returns the cached material/mesh bundle for a material key, building it on first request.</summary>
	private MaterialCache GetOrBuildMaterialCache(string materialKey, in ParticleProfile p)
	{
		if (_matCache.TryGetValue(materialKey, out var cached)) return cached;

		var ramp = new Gradient
		{
			Offsets = new float[] { 0f, 0.3f, 1f },
			Colors = new Color[] { p.color, p.color * 0.7f, new Color(p.color.R * 0.2f, p.color.G * 0.2f, p.color.B * 0.2f, 0f) },
		};
		var scaleCurve = new Curve();
		scaleCurve.AddPoint(new Vector2(0f, 1f));
		scaleCurve.AddPoint(new Vector2(1f, 0.2f));

		float baseSize = 0.03f * p.sizeMul;
		cached = new MaterialCache
		{
			DrawMesh = new QuadMesh { Size = new Vector2(baseSize, baseSize) },
			ColorRamp = new GradientTexture1D { Gradient = ramp },
			ScaleCurve = new CurveTexture { Curve = scaleCurve },
			DrawMat = new StandardMaterial3D
			{
				AlbedoColor = p.color,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				BlendMode = p.additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
				VertexColorUseAsAlbedo = true,
				DisableReceiveShadows = true,
			},
		};
		_matCache[materialKey] = cached;
		return cached;
	}

	[Export(PropertyHint.Range, "16,128,1")] public int ParticlePoolSize = 48;
	private GpuParticles3D[] _particlePool;
	private ParticleProcessMaterial[] _particleProcMats;
	private ulong[] _particleExpiryMs;
	private int _particleCursor;

	private void EnsureParticlePool()
	{
		if (_particlePool != null) return;
		_particlePool = new GpuParticles3D[ParticlePoolSize];
		_particleProcMats = new ParticleProcessMaterial[ParticlePoolSize];
		_particleExpiryMs = new ulong[ParticlePoolSize];
		var root = GetTree().Root;
		for (int i = 0; i < ParticlePoolSize; i++)
		{
			var pm = new ParticleProcessMaterial
			{
				Spread = ParticleSpread,
				ScaleMin = ParticleScale * 0.5f,
				ScaleMax = ParticleScale,
				DampingMin = 0.5f,
				DampingMax = 2f,
			};
			var part = new GpuParticles3D
			{
				OneShot = true,
				Explosiveness = 1f,
				Emitting = false,
				Visible = false,
				ProcessMaterial = pm,
			};
			root.AddChild(part);
			part.Owner = null;
			_particlePool[i] = part;
			_particleProcMats[i] = pm;
		}
	}

	/// <summary>Spawns the GPU particle burst configured by the material's <see cref="ParticleProfile"/>.</summary>
	private void SpawnParticles(Vector3 pos, Vector3 normal, StringName material)
	{
		var p = GetParticleParams(material);
		string matKey = material.ToString();
		var cache = GetOrBuildMaterialCache(matKey, in p);

		EnsureParticlePool();
		int slot = _particleCursor;
		_particleCursor = (_particleCursor + 1) % _particlePool.Length;
		var particles = _particlePool[slot];
		var pm = _particleProcMats[slot];

		float life = ParticleLifetime * p.lifeMul;
		particles.Amount = p.count;
		particles.Lifetime = life;
		particles.DrawPass1 = cache.DrawMesh;
		particles.MaterialOverride = cache.DrawMat;

		pm.Direction = normal;
		pm.Spread = ParticleSpread;
		pm.InitialVelocityMin = ParticleSpeedMin * p.speedMul;
		pm.InitialVelocityMax = ParticleSpeedMax * p.speedMul;
		pm.Gravity = new Vector3(0f, -p.gravity, 0f);
		pm.ScaleMin = ParticleScale * 0.5f;
		pm.ScaleMax = ParticleScale;
		pm.ScaleCurve = cache.ScaleCurve;
		pm.Color = p.color;
		pm.ColorRamp = cache.ColorRamp;

		particles.GlobalPosition = pos;
		particles.Visible = true;
		particles.Restart();
		_particleExpiryMs[slot] = Time.GetTicksMsec() + (ulong)((life + 0.5f) * 1000f);
	}

	/// <summary>Per-material tuning for the impact particle burst (count, gravity, color, etc).</summary>
	private struct ParticleProfile
	{
		public Color color;
		public int count;
		public float gravity;
		public float speedMul;
		public bool additive;
		public float sizeMul;
		public float lifeMul;
	}

	/// <summary>Returns the particle profile (color, count, gravity, etc.) for a given material tag.</summary>
	private static ParticleProfile GetParticleParams(StringName material) => material.ToString() switch
	{
		"flesh"    => new ParticleProfile { color = new(0.85f, 0.05f, 0.05f, 1f), count = 16, gravity = 14f, speedMul = 0.9f, additive = false, sizeMul = 1.4f, lifeMul = 1.0f },
		"metal"    => new ParticleProfile { color = new(3.5f, 2.2f, 0.4f, 1f),    count = 28, gravity = 8f,  speedMul = 2.5f, additive = true,  sizeMul = 0.5f, lifeMul = 1.2f },
		"wood"     => new ParticleProfile { color = new(0.55f, 0.35f, 0.15f, 1f), count = 14, gravity = 10f, speedMul = 1.0f, additive = false, sizeMul = 1.1f, lifeMul = 0.9f },
		"concrete" => new ParticleProfile { color = new(0.7f, 0.68f, 0.65f, 1f),  count = 18, gravity = 3f,  speedMul = 0.7f, additive = false, sizeMul = 1.6f, lifeMul = 1.4f },
		"glass"    => new ParticleProfile { color = new(2.0f, 2.4f, 3.5f, 1f),    count = 22, gravity = 9f,  speedMul = 1.8f, additive = true,  sizeMul = 0.55f, lifeMul = 1.0f },
		_          => new ParticleProfile { color = new(0.6f, 0.6f, 0.6f, 1f),    count = 10, gravity = 6f,  speedMul = 1.0f, additive = false, sizeMul = 1.0f, lifeMul = 1.0f },
	};

	/// <summary>Builds an orthonormal basis where local-Y points along the surface normal (decals project along -Y).</summary>
	private static Basis BasisFromNormal(Vector3 normal)
	{
		Vector3 up = normal.Normalized();
		Vector3 reference = Mathf.Abs(up.Y) > 0.99f ? Vector3.Right : Vector3.Up;
		Vector3 right = reference.Cross(up).Normalized();
		Vector3 fwd = up.Cross(right).Normalized();
		return new Basis(right, up, fwd);
	}
}
