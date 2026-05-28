using Godot;

/// <summary>
/// Deterministic smoke grenade. Per-tick raycast-based trajectory
/// (see <see cref="GrenadeTrajectory"/>); on impact it spawns a
/// <see cref="SmokeVoxelField"/>.
/// Network modes:
///   - Owner mode (local player throw): runs its own physics, broadcasts
///     ProjectileState every few ticks, and sends ProjectileDespawn on deploy.
///   - Puppet mode (remote throw): no physics, position is lerped from
///     ProjectileState snapshots and a ProjectileDespawn triggers Deploy.
/// The deterministic trajectory (fixed dt + raycasts against the static map)
/// keeps both modes visually consistent; position snapshots only correct
/// latency and floating-point drift.
/// </summary>
public partial class SmokeGrenade : Node3D
{
	private Vector3 _vel;
	private Rid _ownerExclude;
	private float _flyTimer;
	private float _restTimer;
	private PhysicsRayQueryParameters3D _query;

	private MeshInstance3D _body;
	private bool _deployed;
	private SmokeVoxelField _field;

	/// <summary>NetId of the player who threw this grenade. Owner spawn = LocalPlayer.NetId, puppet spawn = remote thrower NetId, 0 = non-replicated test spawn (e.g. single-player).</summary>
	public byte OwnerNetId;
	/// <summary>Projectile id unique per owner. Together with OwnerNetId it is globally unique.</summary>
	public uint ProjectileId;
	/// <summary>True = puppet (no physics, only position lerp from owner updates).</summary>
	public bool IsPuppet;

	private const int StateBroadcastEveryNthTick = 4;
	private int _stateBroadcastCounter;

	private Vector3 _puppetTargetPos;
	private Vector3 _puppetTargetVel;
	private const float PuppetLerpRate = 16f;

	/// <summary>
	/// Creates a grenade with the given start transform and owner exclusion.
	/// Network replication is optional via <paramref name="ownerNetId"/> +
	/// <paramref name="projectileId"/>; puppet mode disables local physics and
	/// follows ProjectileState updates from the owner.
	/// </summary>
	public static SmokeGrenade Spawn(Node parent, Vector3 origin, Vector3 velocity, Rid ownerExclude,
		byte ownerNetId = 0, uint projectileId = 0, bool isPuppet = false)
	{
		var g = new SmokeGrenade
		{
			_vel = velocity,
			_ownerExclude = ownerExclude,
			OwnerNetId = ownerNetId,
			ProjectileId = projectileId,
			IsPuppet = isPuppet,
			_puppetTargetPos = origin,
			_puppetTargetVel = velocity,
		};
		parent.AddChild(g);
		g.GlobalPosition = origin;
		return g;
	}

	/// <summary>Builds the visible can, prepares the reusable raycast query, and registers with the NetClient when replicated.</summary>
	public override void _Ready()
	{
		_query = new PhysicsRayQueryParameters3D
		{
			CollisionMask = GrenadeTrajectory.CollisionMask,
			Exclude = new Godot.Collections.Array<Rid> { _ownerExclude },
		};

		const float canHeight = 0.22f;
		_body = new MeshInstance3D
		{
			Mesh = new CylinderMesh { Height = canHeight, TopRadius = 0.055f, BottomRadius = 0.06f },
			Position = new Vector3(0f, canHeight * 0.5f - GrenadeTrajectory.Radius, 0f),
		};
		_body.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.16f, 0.20f, 0.14f),
			Metallic = 0.3f,
			Roughness = 0.55f,
		};
		AddChild(_body);

		if (OwnerNetId != 0)
		{
			var client = NetMain.Instance?.Client;
			if (IsPuppet) client?.RegisterPuppetProjectile(OwnerNetId, ProjectileId, this);
			else client?.RegisterOwnedProjectile(ProjectileId, this);
		}
	}

	/// <summary>Advances physics (owner) or lerps the position (puppet); periodically broadcasts ProjectileState while flying.</summary>
	public override void _PhysicsProcess(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("SmokeGrenade._PhysicsProcess");
		if (_deployed)
		{
			if (!IsInstanceValid(_field)) QueueFree();
			return;
		}

		if (IsPuppet)
		{
			float distSq = (_puppetTargetPos - GlobalPosition).LengthSquared();
			if (distSq > 4f)
				GlobalPosition = _puppetTargetPos;
			else
			{
				float t = 1f - Mathf.Exp(-PuppetLerpRate * (float)delta);
				GlobalPosition = GlobalPosition.Lerp(_puppetTargetPos, t);
			}
			_vel = _puppetTargetVel;
			return;
		}

		StepProjectile();

		if (OwnerNetId != 0)
		{
			_stateBroadcastCounter++;
			if (_stateBroadcastCounter >= StateBroadcastEveryNthTick)
			{
				_stateBroadcastCounter = 0;
				NetMain.Instance?.Client?.SendProjectileState(ProjectileId, GlobalPosition, _vel);
			}
		}
	}

	/// <summary>Advances one deterministic physics step and triggers deployment on rest or timeout.</summary>
	private void StepProjectile()
	{
		_flyTimer += GrenadeTrajectory.FixedDt;

		Vector3 pos = GlobalPosition, vel = _vel;
		bool grounded = GrenadeTrajectory.Advance(GetWorld3D().DirectSpaceState, _query, ref pos, ref vel);
		GlobalPosition = pos;
		_vel = vel;

		if (_vel.Length() < GrenadeTrajectory.RestSpeed && grounded) _restTimer += GrenadeTrajectory.FixedDt;
		else _restTimer = 0f;

		if (_restTimer >= GrenadeTrajectory.RestDuration || _flyTimer >= GrenadeTrajectory.MaxFlyTime)
			Deploy();
	}

	/// <summary>Called by the NetClient on incoming ProjectileState — sets the lerp target in puppet mode.</summary>
	public void ApplyRemoteState(Vector3 pos, Vector3 vel)
	{
		if (!IsPuppet) return;
		_puppetTargetPos = pos;
		_puppetTargetVel = vel;
	}

	/// <summary>Called by the NetClient on incoming ProjectileDespawn — snaps the puppet to <paramref name="finalPos"/> and spawns the smoke. No-op when already deployed.</summary>
	public void ApplyRemoteDespawn(Vector3 finalPos)
	{
		if (_deployed) return;
		GlobalPosition = finalPos;
		Deploy();
	}

	/// <summary>Spawns the voxel smoke field, marks the grenade as deployed, and handles owner/puppet replication.</summary>
	private void Deploy()
	{
		Dbg.Print($"[grenade] deployed @ ({GlobalPosition.X:F1},{GlobalPosition.Y:F1},{GlobalPosition.Z:F1}) | fly={_flyTimer:F2}s puppet={IsPuppet}");
		_field = SmokeVoxelField.Spawn(GetParent(), GlobalPosition);
		_deployed = true;

		if (OwnerNetId != 0)
		{
			var client = NetMain.Instance?.Client;
			if (!IsPuppet)
			{
				client?.SendProjectileDespawn(ProjectileId, GlobalPosition);
				client?.UnregisterOwnedProjectile(ProjectileId);
			}
			else
			{
				client?.UnregisterPuppetProjectile(OwnerNetId, ProjectileId);
			}
		}
	}
}
