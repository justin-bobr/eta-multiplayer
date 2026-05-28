using Godot;

/// <summary>
/// MultiMesh-based shell-ejection pool. All cartridge cases render in a single
/// draw call instead of one node + draw call per shell, scaling to hundreds of
/// simultaneous shells. Drop a Node3D with this script into the scene root,
/// configure the mesh and scale, then assign it to LocalAnimation's Shell Pool
/// export. Physics per shell: velocity + gravity + tumble, floor bounce via
/// down raycast, settle at low impact speed. Compaction uses swap-and-pop so
/// dead slots are reused without leaks.
/// </summary>
public partial class ShellPool : Node3D
{
	/// <summary>Singleton instance. The world scene creates one pool; character scenes call <c>ShellPool.Instance?.Emit(...)</c>. Convention matches <see cref="BulletImpactManager.Instance"/>.</summary>
	public static ShellPool Instance;

	[Export] public Mesh ShellMesh;
	[Export] public float ShellScale = 1f;
	[Export] public int MaxShells = 512;
	[Export] public float Gravity = 9.8f;
	[Export] public float BounceRestitution = 0.15f;
	[Export] public float HorizontalDamping = 0.40f;
	[Export] public float MinBounceSpeed = 0.8f;
	[Export] public float FloorNormalThreshold = 0.3f;
	[Export] public float FloorSnapExtraBuffer = 0.002f;
	[Export] public float DefaultLifetime = 30f;
	[Export] public float SpawnGracePeriod = 0.1f;

	[Export] public Camera3D Camera;
	[Export] public float LodDistance = 25f;
	[Export] public float OffscreenDespawnTime = 15f;
	[Export] public float NearClipDistance = 0.4f;

	/// <summary>Collider RIDs excluded from the floor raycast (player bodies, etc.). Shells never collide with players, only world geometry. Populate via <see cref="AddExcludedBody"/>.</summary>
	private readonly Godot.Collections.Array<Rid> _excludedColliders = new();

	/// <summary>Adds a CollisionObject3D to the raycast-exclude list (idempotent).</summary>
	public void AddExcludedBody(CollisionObject3D body)
	{
		if (body == null) return;
		var rid = body.GetRid();
		if (!_excludedColliders.Contains(rid)) _excludedColliders.Add(rid);
	}

	/// <summary>Removes a CollisionObject3D from the raycast-exclude list.</summary>
	public void RemoveExcludedBody(CollisionObject3D body)
	{
		if (body == null) return;
		_excludedColliders.Remove(body.GetRid());
	}

	private MultiMeshInstance3D _mmi;
	private MultiMesh _mm;
	private ShellEntry[] _shells;
	private int _activeCount;
	private int _overflowCursor;
	private float _autoFloorOffset;
	private readonly PhysicsRayQueryParameters3D _floorRayQuery = new();

	/// <summary>Per-shell simulation state held in the pool array.</summary>
	private struct ShellEntry
	{
		public Vector3 Position;
		public Basis Rotation;
		public Vector3 Velocity;
		public Vector3 AngularVelocity;
		public float Age;
		public float Lifetime;
		public float OffscreenTime;
		public bool Grounded;
	}

	/// <summary>Initialises the MultiMesh, derives the auto floor offset from the shell AABB, and registers the singleton.</summary>
	public override void _Ready()
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server) { QueueFree(); return; }
		Instance = this;
		if (ShellMesh == null)
		{
			GD.PrintErr("[ShellPool] ShellMesh not assigned — pool inactive.");
			return;
		}
		Camera ??= GetViewport()?.GetCamera3D();
		_shells = new ShellEntry[MaxShells];

		var aabb = ShellMesh.GetAabb();
		float halfMaxDim = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z)) * 0.5f;
		_autoFloorOffset = halfMaxDim * ShellScale + FloorSnapExtraBuffer;

		_mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = ShellMesh,
			InstanceCount = MaxShells,
			VisibleInstanceCount = 0,
		};
		_mmi = new MultiMeshInstance3D
		{
			Multimesh = _mm,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
			CustomAabb = new Aabb(new Vector3(-1000f, -1000f, -1000f), new Vector3(2000f, 2000f, 2000f)),
			TopLevel = true,
		};
		AddChild(_mmi);
	}

	/// <summary>Clears the singleton reference when this pool leaves the tree.</summary>
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	/// <summary>Spawns a new shell with the given transform/velocity/tumble. Overflow recycles the oldest slot round-robin.</summary>
	public void Emit(Transform3D startTf, Vector3 velocity, Vector3 angularVel, float lifetime = -1f)
	{
		if (_shells == null) return;
		int slot;
		if (_activeCount < MaxShells)
		{
			slot = _activeCount++;
		}
		else
		{
			slot = _overflowCursor;
			_overflowCursor = (_overflowCursor + 1) % MaxShells;
		}
		_shells[slot] = new ShellEntry
		{
			Position = startTf.Origin,
			Rotation = startTf.Basis.Orthonormalized(),
			Velocity = velocity,
			AngularVelocity = angularVel,
			Age = 0f,
			Lifetime = lifetime > 0f ? lifetime : DefaultLifetime,
			Grounded = false,
		};
		WriteInstance(slot);
		_mm.VisibleInstanceCount = _activeCount;
	}

	/// <summary>Steps every active shell: applies gravity, raycasts, bounces, off-screen culling and lifetime expiry.</summary>
	public override void _PhysicsProcess(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("ShellPool._PhysicsProcess");
		if (_shells == null || _activeCount == 0) return;
		float dt = (float)delta;
		var space = GetWorld3D().DirectSpaceState;

		if (Camera == null || !Godot.GodotObject.IsInstanceValid(Camera))
			Camera = GetViewport()?.GetCamera3D();

		bool hasCam = Camera != null && Godot.GodotObject.IsInstanceValid(Camera);
		Vector3 camPos = hasCam ? Camera.GlobalPosition : Vector3.Zero;
		float lodDistSq = LodDistance * LodDistance;

		int i = 0;
		while (i < _activeCount)
		{
			ref var s = ref _shells[i];
			s.Age += dt;

			bool inFrustum = !hasCam || Camera.IsPositionInFrustum(s.Position);
			if (!inFrustum)
			{
				s.OffscreenTime += dt;
				if (s.OffscreenTime > OffscreenDespawnTime)
				{
					_shells[i] = _shells[--_activeCount];
					if (i < _activeCount) WriteInstance(i);
					continue;
				}
			}
			else
			{
				s.OffscreenTime = 0f;
			}

			if (s.Age >= s.Lifetime)
			{
				_shells[i] = _shells[--_activeCount];
				if (i < _activeCount) WriteInstance(i);
				continue;
			}

			if (!s.Grounded)
			{
				float distSq = hasCam ? (s.Position - camPos).LengthSquared() : 0f;
				bool isFar = hasCam && distSq > lodDistSq;

				Vector3 prevPos = s.Position;
				s.Velocity.Y -= Gravity * dt;
				s.Position += s.Velocity * dt;

				if (!isFar)
				{
					float angSpeed = s.AngularVelocity.Length();
					if (angSpeed > 0.001f)
					{
						Vector3 axis = s.AngularVelocity / angSpeed;
						float angle = angSpeed * dt;
						Quaternion deltaQ = new(axis, angle);
						s.Rotation = (new Basis(deltaQ) * s.Rotation).Orthonormalized();
					}

					if (s.Age > SpawnGracePeriod && s.Velocity.Y < 0f)
					{
						_floorRayQuery.From = prevPos;
						_floorRayQuery.To = s.Position + Vector3.Down * 0.02f;
						_floorRayQuery.Exclude = _excludedColliders;
						var hit = space.IntersectRay(_floorRayQuery);
						if (hit.Count > 0 && hit.ContainsKey("position") && hit.ContainsKey("normal"))
						{
							Vector3 hitNormal = (Vector3)hit["normal"];
							if (hitNormal.Y > FloorNormalThreshold)
							{
								Vector3 floorHit = (Vector3)hit["position"];
								s.Position = new Vector3(floorHit.X, floorHit.Y + _autoFloorOffset, floorHit.Z);
								float impactSpeed = Mathf.Abs(s.Velocity.Y);
								if (impactSpeed > MinBounceSpeed)
								{
									s.Velocity = new Vector3(s.Velocity.X * HorizontalDamping, -s.Velocity.Y * BounceRestitution, s.Velocity.Z * HorizontalDamping);
									s.AngularVelocity *= 0.6f;
								}
								else
								{
									s.Grounded = true;
									s.Velocity = Vector3.Zero;
									s.AngularVelocity = Vector3.Zero;
								}
							}
						}
					}
				}

			}

			WriteInstance(i);
			i++;
		}

		_mm.VisibleInstanceCount = _activeCount;
	}

	/// <summary>Writes a single shell's transform into the MultiMesh, applying near-clip culling.</summary>
	private void WriteInstance(int idx)
	{
		ref var s = ref _shells[idx];
		float scale = ShellScale;
		if (Camera != null && Godot.GodotObject.IsInstanceValid(Camera))
		{
			float distSq = (s.Position - Camera.GlobalPosition).LengthSquared();
			float nearSq = NearClipDistance * NearClipDistance;
			if (distSq < nearSq) scale = 0f;
		}
		var scaledBasis = s.Rotation.Scaled(Vector3.One * scale);
		_mm.SetInstanceTransform(idx, new Transform3D(scaledBasis, s.Position));
	}
}
