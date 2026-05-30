using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Voxel-based smoke with a real grid advection simulation. Flow:
/// 1) A one-time flood fill (BFS + raycasts) over a box domain decides which
///    cell faces are blocked by walls (wall connectivity).
/// 2) Every physics tick runs a deterministic advection sim on the grid:
///    emission at the source, buoyancy + wind advection, diffusion (spread),
///    dissipation (fade, stronger with height). The smoke rises, drifts with
///    the wind, accumulates, and fades upward while new smoke keeps coming.
/// 3) The density grid is baked into a 3D texture each tick and rendered via
///    a FogVolume (opaque, wall occlusion native, no transparency issues).
/// Multiplayer: the sim is fully deterministic — fixed timestep, fixed wind,
/// no random, fixed iteration order. Every client gets the same density field.
/// </summary>
public partial class SmokeVoxelField : Node3D
{
	public float VoxelSize = 0.6f;
	public float DomainWidth = 13.8f;
	public float DomainHeight = 5f;
	public float WallHeight = 2.2f;
	public float SmokeCore = 0.5f;
	public uint MapMask = 1;

	public float BurnTime = 24f;
	public float EmitRate = 52f;
	public float Buoyancy = 0.5f;
	public Vector3 Wind = new(0.13f, 0f, 0.08f);
	public float Diffusion = 0.8f;
	public float Dissipation = 0.018f;
	public float SkyFade = 0.3f;
	public float FadeRate = 1.6f;
	public float FadeRise = 1.2f;
	public float MaxDensity = 7f;

	public float DensityMul = 60f;
	public float EmissionStrength = 0.9f;

	public float ChannelRadius = 1.0f;
	public float ChannelDuration = 0.3f;

	/// <summary>Active fields — the hitscan calls <see cref="DisturbAll"/> over this list.</summary>
	public static readonly List<SmokeVoxelField> Active = new();

	/// <summary>3D density texture of the cloud — cloud_shadows samples it for the shadow mask.</summary>
	public Texture3D DensityTexture => _tex;
	/// <summary>World-space minimum corner of the density texture.</summary>
	public Vector3 GridMin { get; private set; }
	/// <summary>World-space size (edge lengths) of the density texture.</summary>
	public Vector3 GridSize { get; private set; }

	private const string FogShaderCode = @"
shader_type fog;

uniform sampler3D density_tex : repeat_disable, hint_default_white, filter_linear;
uniform vec3 smoke_albedo : source_color = vec3(0.85, 0.85, 0.87);
uniform float density_mul = 60.0;
uniform float emission_strength = 0.9;
uniform vec3 grid_min;
uniform vec3 grid_size = vec3(1.0);
uniform float noise_scale = 0.55;
uniform float noise_rise = 0.08;
uniform float noise_amount = 0.5;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}
float vnoise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i + vec3(0.0, 0.0, 0.0)), hash(i + vec3(1.0, 0.0, 0.0)), f.x),
	               mix(hash(i + vec3(0.0, 1.0, 0.0)), hash(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
	           mix(mix(hash(i + vec3(0.0, 0.0, 1.0)), hash(i + vec3(1.0, 0.0, 1.0)), f.x),
	               mix(hash(i + vec3(0.0, 1.0, 1.0)), hash(i + vec3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
}
float fbm(vec3 p) {
	float s = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.03; a *= 0.5; }
	return s;
}

void fog() {
	vec3 tuv = (WORLD_POSITION - grid_min) / grid_size;
	float d = texture(density_tex, clamp(tuv, vec3(0.0), vec3(1.0))).r;

	// Billowing: animated fbm breaks the smooth sim shape into rolling puffs and
	// frays the edges. Cloud motion comes from the sim — the noise adds detail.
	vec3 np = WORLD_POSITION * noise_scale + vec3(0.0, -TIME * noise_rise, TIME * 0.05);
	float n = clamp((fbm(np) - 0.30) * 2.4, 0.0, 1.0);   // remap a narrow fbm range to 0..1
	d *= mix(1.0 - noise_amount, 1.0 + noise_amount, n);

	DENSITY = max(0.0, d) * density_mul;
	ALBEDO = smoke_albedo;
	EMISSION = smoke_albedo * emission_strength * d;
}
";

	private int _nx, _ny, _nz, _n;
	private float _cell;
	private Vector3 _gridMin;
	private int _srcIdx;

	private float[] _density;
	private float[] _delta;
	private float[] _shapeMask;
	private bool[] _faceX, _faceY, _faceZ;

	private readonly Queue<int> _frontier = new();
	private bool[] _visited;
	private int _floodBudget;
	private bool _originSet, _floodDone, _built;
	private float _age;
	private float _bakeAccum;
	private int _wallMaxY;

	private ShaderMaterial _mat;
	private ImageTexture3D _tex;
	private Godot.Collections.Array<Image> _images;
	private byte[] _sliceBuf;

	private Vector3 _chanA, _chanB;
	private float _chanTimer;

	/// <summary>Spawns a voxel smoke field. <paramref name="origin"/> is the detonation point.</summary>
	public static SmokeVoxelField Spawn(Node parent, Vector3 origin)
	{
		var f = new SmokeVoxelField();
		parent.AddChild(f);
		f.GlobalPosition = origin + Vector3.Up * f.VoxelSize;
		return f;
	}

	/// <summary>Called by the hitscan — clears a channel in all active smoke fields.</summary>
	public static void DisturbAll(Vector3 rayOrigin, Vector3 rayDir, float rayLength)
	{
		for (int i = 0; i < Active.Count; i++)
			Active[i].DisturbRay(rayOrigin, rayDir, rayLength);
	}

	/// <summary>Registers this field with the global active list on tree entry.</summary>
	public override void _EnterTree() => Active.Add(this);
	/// <summary>Removes this field from the global active list on tree exit.</summary>
	public override void _ExitTree() => Active.Remove(this);

	/// <summary>Allocates grid arrays, computes the shape mask, and seeds the flood-fill frontier.</summary>
	public override void _Ready()
	{
		_cell = VoxelSize;
		_wallMaxY = 1 + Mathf.CeilToInt(WallHeight / _cell);
		_nx = Mathf.Max(5, Mathf.RoundToInt(DomainWidth / _cell));
		_nz = _nx;
		_ny = Mathf.Max(6, Mathf.RoundToInt(DomainHeight / _cell));
		_n = _nx * _ny * _nz;

		_density = new float[_n];
		_delta = new float[_n];
		_shapeMask = new float[_n];
		_faceX = new bool[_n];
		_faceY = new bool[_n];
		_faceZ = new bool[_n];
		_visited = new bool[_n];
		BuildShapeMask();

		_srcIdx = Idx(_nx / 2, 1, _nz / 2);
		_floodBudget = Mathf.Max(10, _n / 32);
		_visited[_srcIdx] = true;
		_frontier.Enqueue(_srcIdx);
	}

	/// <summary>Per-tick driver: completes flood-fill, builds the volume once, then runs the sim and periodic bakes.</summary>
	public override void _PhysicsProcess(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("SmokeVoxelField._PhysicsProcess");
		if (!_originSet)
		{
			_gridMin = GlobalPosition - new Vector3(_nx / 2, 1, _nz / 2) * _cell;
			_originSet = true;
		}
		if (!_floodDone) { StepFlood(); return; }
		if (!_built) { BuildVolume(); _built = true; return; }

		StepSim((float)delta);

		_bakeAccum += (float)delta;
		if (_bakeAccum >= 1f / 30f) { _bakeAccum -= 1f / 30f; Bake(); }
	}

	/// <summary>Flattens (x,y,z) cell coordinates into a linear array index.</summary>
	private int Idx(int x, int y, int z) => x + _nx * (y + _ny * z);
	/// <summary>Returns the world-space position of the centre of cell (x,y,z).</summary>
	private Vector3 CellWorld(int x, int y, int z) => _gridMin + new Vector3(x, y, z) * _cell;

	/// <summary>Processes a budget of frontier cells per tick, raycasting cell-to-cell to mark open faces.</summary>
	private void StepFlood()
	{
		var space = GetWorld3D().DirectSpaceState;
		int budget = _floodBudget;
		int layer = _nx * _ny;

		while (budget-- > 0 && _frontier.Count > 0)
		{
			int idx = _frontier.Dequeue();
			int z = idx / layer;
			int rem = idx - z * layer;
			int y = rem / _nx;
			int x = rem - y * _nx;
			Vector3 cw = CellWorld(x, y, z);

			FloodNeighbor(space, x, y, z, cw, 1, 0, 0);
			FloodNeighbor(space, x, y, z, cw, -1, 0, 0);
			FloodNeighbor(space, x, y, z, cw, 0, 1, 0);
			FloodNeighbor(space, x, y, z, cw, 0, -1, 0);
			FloodNeighbor(space, x, y, z, cw, 0, 0, 1);
			FloodNeighbor(space, x, y, z, cw, 0, 0, -1);
		}

		if (_frontier.Count == 0)
		{
			_floodDone = true;
			Dbg.Print($"[smoke] flood-fill done — Grid {_nx}x{_ny}x{_nz}");
		}
	}

	/// <summary>Checks one neighbour direction, raycasts when below WallHeight, and records the open face plus frontier entry.</summary>
	private void FloodNeighbor(PhysicsDirectSpaceState3D space, int x, int y, int z, Vector3 cw,
								 int dx, int dy, int dz)
	{
		int nx2 = x + dx, ny2 = y + dy, nz2 = z + dz;
		if (nx2 < 0 || nx2 >= _nx || ny2 < 0 || ny2 >= _ny || nz2 < 0 || nz2 >= _nz) return;
		bool checkWall = y <= _wallMaxY || ny2 <= _wallMaxY;
		if (checkWall && !EdgeClear(space, cw, CellWorld(nx2, ny2, nz2))) return;

		if (dx == 1) _faceX[Idx(x, y, z)] = true;
		else if (dx == -1) _faceX[Idx(nx2, ny2, nz2)] = true;
		else if (dy == 1) _faceY[Idx(x, y, z)] = true;
		else if (dy == -1) _faceY[Idx(nx2, ny2, nz2)] = true;
		else if (dz == 1) _faceZ[Idx(x, y, z)] = true;
		else _faceZ[Idx(nx2, ny2, nz2)] = true;

		int nIdx = Idx(nx2, ny2, nz2);
		if (!_visited[nIdx]) { _visited[nIdx] = true; _frontier.Enqueue(nIdx); }
	}

	/// <summary>Edge clear? Casts in both directions — single-sided trimesh walls only hit from the front.</summary>
	private bool EdgeClear(PhysicsDirectSpaceState3D space, Vector3 a, Vector3 b)
		=> !RayHits(space, a, b) && !RayHits(space, b, a);

	// Wiederverwendete Query — Flood-Fill macht potentiell tausende Casts in einem Burst beim Smoke-
	// Spawn. Allokierte vorher ein neues PhysicsRayQueryParameters3D pro Call.
	private PhysicsRayQueryParameters3D _floodQuery;

	/// <summary>Returns true if a raycast from <paramref name="from"/> to <paramref name="to"/> hits map geometry.</summary>
	private bool RayHits(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to)
	{
		if (_floodQuery == null)
		{
			_floodQuery = PhysicsRayQueryParameters3D.Create(from, to, MapMask);
		}
		_floodQuery.From = from;
		_floodQuery.To = to;
		_floodQuery.CollisionMask = MapMask;
		return space.IntersectRay(_floodQuery).Count > 0;
	}

	/// <summary>Allocates the 3D density texture and creates the FogVolume + shader material that render the smoke.</summary>
	private void BuildVolume()
	{
		_images = new Godot.Collections.Array<Image>();
		for (int z = 0; z < _nz; z++)
			_images.Add(Image.CreateEmpty(_nx, _ny, false, Image.Format.R8));

		_tex = new ImageTexture3D();
		_tex.Create(Image.Format.R8, _nx, _ny, _nz, false, _images);
		_sliceBuf = new byte[_nx * _ny];

		GridSize = new Vector3(_nx, _ny, _nz) * _cell;
		GridMin = _gridMin - Vector3.One * (_cell * 0.5f);

		_mat = new ShaderMaterial { Shader = new Shader { Code = FogShaderCode } };
		_mat.SetShaderParameter("density_tex", _tex);
		_mat.SetShaderParameter("grid_min", GridMin);
		_mat.SetShaderParameter("grid_size", GridSize);
		_mat.SetShaderParameter("density_mul", DensityMul);
		_mat.SetShaderParameter("emission_strength", EmissionStrength);

		var fog = new FogVolume
		{
			Shape = RenderingServer.FogVolumeShape.Box,
			Size = GridSize,
			Material = _mat,
			Position = GridMin + GridSize * 0.5f - GlobalPosition,
		};
		AddChild(fog);

		EnsureVolumetricFog();
		Dbg.Print("[smoke] FogVolume + Sim bereit");
	}

	/// <summary>Enables Volumetric Fog on the world Environment — without it no FogVolume renders.</summary>
	private void EnsureVolumetricFog()
	{
		Godot.Environment env = GetWorld3D()?.Environment;
		if (env == null)
		{
			GD.PushWarning("[smoke] No Environment — 'Volumetric Fog' must be enabled manually on the " +
							 "WorldEnvironment, otherwise the smoke is invisible.");
			return;
		}
		if (!env.VolumetricFogEnabled) env.VolumetricFogEnabled = true;
		if (env.VolumetricFogAmbientInject < 1f) env.VolumetricFogAmbientInject = 1f;
		env.VolumetricFogTemporalReprojectionEnabled = true;
		env.VolumetricFogTemporalReprojectionAmount = 0.9f;
	}

	/// <summary>Runs one deterministic advection + diffusion + dissipation step on the density grid.</summary>
	private void StepSim(float dt)
	{
		_age += dt;

		if (_age < BurnTime)
			_density[_srcIdx] += EmitRate * dt;

		Array.Clear(_delta, 0, _n);
		float fadePhase = Mathf.Clamp(_age - BurnTime, 0f, 1f);
		float cx = Wind.X * dt / _cell;
		float cyv = (Buoyancy + fadePhase * FadeRise) * dt / _cell;
		float cz = Wind.Z * dt / _cell;
		float diff = Mathf.Min(0.16f, Diffusion * dt / (_cell * _cell));

		for (int z = 0; z < _nz; z++)
			for (int y = 0; y < _ny; y++)
				for (int x = 0; x < _nx; x++)
				{
					int i = Idx(x, y, z);
					float di = _density[i];

					if (x < _nx - 1 && _faceX[i])
					{
						int j = i + 1;
						float adv = cx > 0f ? cx * di : cx * _density[j];
						float f = adv + diff * (di - _density[j]);
						_delta[i] -= f; _delta[j] += f;
					}
					if (y < _ny - 1 && _faceY[i])
					{
						int j = i + _nx;
						float adv = cyv > 0f ? cyv * di : cyv * _density[j];
						float f = adv + diff * (di - _density[j]);
						_delta[i] -= f; _delta[j] += f;
					}
					if (z < _nz - 1 && _faceZ[i])
					{
						int j = i + _nx * _ny;
						float adv = cz > 0f ? cz * di : cz * _density[j];
						float f = adv + diff * (di - _density[j]);
						_delta[i] -= f; _delta[j] += f;
					}
				}

		float invYmax = 1f / Mathf.Max(1, _ny - 1);
		float fade = FadeRate * fadePhase;
		for (int z = 0; z < _nz; z++)
			for (int y = 0; y < _ny; y++)
			{
				float hf = y * invYmax;
				float keep = Mathf.Max(0f, 1f - (Dissipation + fade * hf + SkyFade * hf * hf) * dt);
				int row = _nx * (y + _ny * z);
				for (int x = 0; x < _nx; x++)
				{
					int i = row + x;
					float v = (_density[i] + _delta[i]) * keep;
					_density[i] = v < 0f ? 0f : (v > MaxDensity ? MaxDensity : v);
				}
			}

		if (_chanTimer > 0f)
		{
			_chanTimer -= dt;
			float clear = Mathf.Pow(0.04f, dt / 0.2f);
			float r2 = ChannelRadius * ChannelRadius;
			for (int z = 0; z < _nz; z++)
				for (int y = 0; y < _ny; y++)
					for (int x = 0; x < _nx; x++)
					{
						int i = Idx(x, y, z);
						if (_density[i] <= 0.001f) continue;
						if (SegDistSq(CellWorld(x, y, z), _chanA, _chanB) < r2)
							_density[i] *= clear;
					}
		}
	}

	/// <summary>Squared distance from point to segment.</summary>
	private static float SegDistSq(Vector3 p, Vector3 a, Vector3 b)
	{
		Vector3 ab = b - a;
		float t = ab.LengthSquared() > 0.0001f
			? Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
		return (p - (a + ab * t)).LengthSquared();
	}

	/// <summary>
	/// Builds the static shape mask once: a grounded ellipsoid whose radius is
	/// distorted with 3D noise to give an irregular blob (not a perfect sphere
	/// and not a box). The centre sits slightly below the box mid-line so the
	/// grenade end has smoke. Multiplied into the density texture in
	/// <see cref="Bake"/>, affecting both the fog render and cloud_shadows.
	/// </summary>
	private void BuildShapeMask()
	{
		var noise = new FastNoiseLite { Seed = 1337, Frequency = 0.17f };
		float hx = _nx * 0.5f, hz = _nz * 0.5f;
		const float cy = 0.44f;
		const float halfH = 0.66f;
		const float halfXZ = 0.65f;
		for (int z = 0; z < _nz; z++)
			for (int y = 0; y < _ny; y++)
				for (int x = 0; x < _nx; x++)
				{
					float ex = (x + 0.5f - hx) / hx / halfXZ;
					float ez = (z + 0.5f - hz) / hz / halfXZ;
					float fy = y / Mathf.Max(1f, _ny - 1f);
					float ey = (fy - cy) / halfH;
					float r = Mathf.Sqrt(ex * ex + ey * ey + ez * ez);
					r += noise.GetNoise3D(x, y, z) * 0.20f;
					_shapeMask[Idx(x, y, z)] = 1f - Mathf.SmoothStep(SmokeCore, 1f, r);
				}
	}

	/// <summary>Copies the density grid into per-slice images, uploads the 3D texture, and frees the field once fully dissolved.</summary>
	private void Bake()
	{
		float total = 0f;
		int layer = _nx * _ny;
		float maskBlend = Mathf.Clamp(_age - BurnTime, 0f, 1f);

		for (int z = 0; z < _nz; z++)
		{
			for (int y = 0; y < _ny; y++)
				for (int x = 0; x < _nx; x++)
				{
					int i = z * layer + y * _nx + x;
					float d = _density[i] * Mathf.Lerp(_shapeMask[i], 1f, maskBlend);
					_sliceBuf[x + y * _nx] = (byte)(Mathf.Clamp(d, 0f, 1f) * 255f);
					total += d;
				}
			_images[z].SetData(_nx, _ny, false, Image.Format.R8, _sliceBuf);
		}
		_tex.Update(_images);

		if (total < 0.5f && _age > BurnTime) { Dbg.Print("[smoke] dissolved — freeing"); QueueFree(); }
	}

	/// <summary>Marks the shot line for clearing — the sim deterministically empties and refills the channel.</summary>
	public void DisturbRay(Vector3 origin, Vector3 dir, float length)
	{
		if (!_built) return;
		_chanA = origin;
		_chanB = origin + dir.Normalized() * length;
		_chanTimer = ChannelDuration;
	}

	/// <summary>
	/// True if any active cloud blocks the line of sight a→b. Deterministic
	/// (only samples the sim grid), so it matches on server and client.
	/// </summary>
	public static bool AnyBlocksSight(Vector3 a, Vector3 b)
	{
		for (int i = 0; i < Active.Count; i++)
			if (Active[i].BlocksSight(a, b)) return true;
		return false;
	}

	/// <summary>Does this cloud block the line of sight a→b? Integrates density along the segment.</summary>
	public bool BlocksSight(Vector3 a, Vector3 b)
	{
		if (!_built) return false;
		const int steps = 24;
		float od = 0f;
		for (int s = 0; s < steps; s++)
			od += SampleDensity(a.Lerp(b, (s + 0.5f) / steps));
		od *= a.DistanceTo(b) / steps;
		return 1f - Mathf.Exp(-od * DensityMul) > 0.65f;
	}

	/// <summary>Density at a world position (nearest cell, deterministic). Returns 0 outside the grid.</summary>
	private float SampleDensity(Vector3 world)
	{
		Vector3 c = (world - _gridMin) / _cell;
		int x = Mathf.RoundToInt(c.X), y = Mathf.RoundToInt(c.Y), z = Mathf.RoundToInt(c.Z);
		if (x < 0 || x >= _nx || y < 0 || y >= _ny || z < 0 || z >= _nz) return 0f;
		return _density[Idx(x, y, z)];
	}
}
