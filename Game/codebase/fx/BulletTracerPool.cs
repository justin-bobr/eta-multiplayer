using Godot;

/// <summary>
/// MultiMesh-based bullet tracer pool. All tracers render in a single draw call instead of one
/// Node3D + MeshInstance3D + StandardMaterial3D per shot. Drop a Node3D with this script into the
/// world scene; LocalAnimation.TriggerBulletTracer calls <c>BulletTracerPool.Instance?.Emit(...)</c>.
///
/// Pattern mirrors <see cref="ShellPool"/>: fixed-size ring buffer with swap-and-pop expiration,
/// per-instance Transform3D + Color (color via <see cref="MultiMesh.UseColors"/> = true + material's
/// VertexColorUseAsAlbedo for alpha fade).
///
/// Per Frame: front position = origin + dir × (age × speed). Cylinder midpoint sits StreakLength/2
/// behind the front. Expires when front passes the endpoint distance.
/// </summary>
public partial class BulletTracerPool : Node3D
{
	/// <summary>Singleton; LocalAnimation references via <c>BulletTracerPool.Instance?.Emit(...)</c>.</summary>
	public static BulletTracerPool Instance;

	[Export] public int MaxTracers = 64;
	[Export] public float TracerRadius = 0.012f;
	[Export] public Color DefaultColor = new(1.0f, 0.85f, 0.4f, 1.0f);

	private MultiMeshInstance3D _mmi;
	private MultiMesh _mm;
	private TracerEntry[] _tracers;
	private int _activeCount;
	private int _overflowCursor;

	/// <summary>Per-tracer simulation state held in the pool array.</summary>
	private struct TracerEntry
	{
		public Vector3 Origin;
		public Vector3 Direction;
		public float TotalDistance;
		public float Speed;
		public float StreakLength;
		public float Age;
		public float TotalLife;
		public Color StartColor;
	}

	/// <summary>Initialises the MultiMesh with a thin cylinder mesh and registers the singleton.</summary>
	public override void _Ready()
	{
		Instance = this;
		_tracers = new TracerEntry[MaxTracers];

		// Cylinder mit Y-Achse als Längsachse (Standard). Höhe 1.0, Radius = TracerRadius. Per
		// Instance wird Y-Scale auf StreakLength gesetzt damit jeder Tracer seine richtige Länge hat.
		var mesh = new CylinderMesh
		{
			TopRadius = TracerRadius,
			BottomRadius = TracerRadius,
			Height = 1.0f,
			RadialSegments = 6,
			Rings = 0,
		};
		var mat = new StandardMaterial3D
		{
			AlbedoColor = DefaultColor,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			DisableReceiveShadows = true,
			VertexColorUseAsAlbedo = true,
		};
		mesh.Material = mat;

		_mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = mesh,
			InstanceCount = MaxTracers,
			VisibleInstanceCount = 0,
		};
		_mmi = new MultiMeshInstance3D
		{
			Multimesh = _mm,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
			// Tracer können bis ~100m fliegen — sehr großzügiger AABB damit kein Frustum-Cull mid-flight.
			CustomAabb = new Aabb(new Vector3(-200f, -200f, -200f), new Vector3(400f, 400f, 400f)),
			TopLevel = true,
		};
		AddChild(_mmi);
	}

	/// <summary>Clears the singleton when the pool leaves the tree.</summary>
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	/// <summary>Spawns a tracer from origin toward endpoint. Color alpha fades over total flight + streak time.
	/// Overflow recycles the oldest slot.</summary>
	public void Emit(Vector3 origin, Vector3 endpoint, Color color, float speed, float streakLength)
	{
		if (_tracers == null) return;
		Vector3 delta = endpoint - origin;
		float totalDist = delta.Length();
		if (totalDist < 0.01f) return;
		Vector3 dir = delta / totalDist;
		float safeSpeed = Mathf.Max(50f, speed);
		float safeStreak = Mathf.Max(0.1f, streakLength);

		int slot;
		if (_activeCount < MaxTracers)
		{
			slot = _activeCount++;
		}
		else
		{
			slot = _overflowCursor;
			_overflowCursor = (_overflowCursor + 1) % MaxTracers;
		}
		_tracers[slot] = new TracerEntry
		{
			Origin = origin,
			Direction = dir,
			TotalDistance = totalDist,
			Speed = safeSpeed,
			StreakLength = safeStreak,
			Age = 0f,
			TotalLife = (totalDist + safeStreak) / safeSpeed,
			StartColor = color,
		};
		WriteInstance(slot);
		_mm.VisibleInstanceCount = _activeCount;
	}

	/// <summary>Advances every active tracer: moves the streak forward, fades alpha, expires when front passes endpoint.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("BulletTracerPool._Process");
		if (_tracers == null || _activeCount == 0) return;
		float dt = (float)delta;

		int i = 0;
		while (i < _activeCount)
		{
			ref var t = ref _tracers[i];
			t.Age += dt;
			float frontDist = t.Age * t.Speed;
			if (frontDist - t.StreakLength >= t.TotalDistance)
			{
				// Expired — swap-and-pop. WriteInstance updated the swapped slot's transform.
				_tracers[i] = _tracers[--_activeCount];
				if (i < _activeCount) WriteInstance(i);
				continue;
			}
			WriteInstance(i);
			i++;
		}
		_mm.VisibleInstanceCount = _activeCount;
	}

	/// <summary>Writes a tracer's transform + color into the MultiMesh. Cylinder Y-Achse aligned to direction,
	/// scale.Y = streakLength, position = midpoint of streak (= front - dir × halfStreak).</summary>
	private void WriteInstance(int idx)
	{
		ref var t = ref _tracers[idx];
		float clampedFront = Mathf.Min(t.Age * t.Speed, t.TotalDistance);
		Vector3 frontPos = t.Origin + t.Direction * clampedFront;
		Vector3 midpoint = frontPos - t.Direction * (t.StreakLength * 0.5f);

		// Basis: Y entlang direction. X perpendicular (cross mit Up oder Right wenn near-vertical).
		Vector3 refUp = Mathf.Abs(t.Direction.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
		Vector3 xAxis = t.Direction.Cross(refUp).Normalized();
		Vector3 zAxis = xAxis.Cross(t.Direction).Normalized();
		var basis = new Basis(xAxis * 1f, t.Direction * t.StreakLength, zAxis * 1f);

		_mm.SetInstanceTransform(idx, new Transform3D(basis, midpoint));

		// Alpha fade über totalLife. Linear decay.
		float ageT = Mathf.Clamp(t.Age / t.TotalLife, 0f, 1f);
		float alpha = (1f - ageT) * t.StartColor.A;
		_mm.SetInstanceColor(idx, new Color(t.StartColor.R, t.StartColor.G, t.StartColor.B, alpha));
	}
}
