using Godot;

/// <summary>
/// Abstract foundation for all character variants (LocalPlayer, PuppetPlayer, ServerPlayer,
/// ServerBotPlayer). Holds shared state and setup that ALL variants need:
///   - CharacterBody3D inheritance + collision capsule
///   - MovementController (always — even Puppet reads AdsBlend/CrouchBlend etc. for anim state)
///   - Skeleton3D + HitboxRig (hitboxes exist client-side for visualization, server-side for lag-comp)
///   - NetId / Hp / Team / TickRate
///   - StandHeight / CrouchHeight / capsule resize
///
/// Subclasses extend with their specific paths:
///   - LocalPlayer: camera + FPS arms + viewmodel + input + prediction + reconciliation
///   - PuppetPlayer: snapshot-interp + AnimTree manual advance + spectator camera
///   - ServerPlayer: real-peer input + frozen state for reconnect pool
///   - ServerBotPlayer: zero/AI input + PendingRemoval
///
/// Pattern: template method. Base calls <c>Setup()</c> and <c>Tick()</c> at strategic points,
/// subclasses override with their logic. Common helpers like ApplyCrouchHeight live here.
/// </summary>
public abstract partial class BaseCharacter : CharacterBody3D
{
	public const int TickRate = 128;
	protected float _fixedDt;
	protected uint _currentTick;
	public uint CurrentTick => _currentTick;

	[Export(PropertyHint.Range, "0.5,2.5,0.05")] public float StandHeight = 1.8f;
	[Export(PropertyHint.Range, "0.5,2.0,0.05")] public float CrouchHeight = 1.2f;
	[Export(PropertyHint.Range, "0.1,1.0,0.01")] public float CapsuleRadius = 0.4f;
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float FloorSnapDist = 0.6f;
	[Export(PropertyHint.Range, "0,90,1")] public float FloorMaxAngleDeg = 50f;
	[Export(PropertyHint.Range, "0.0,1.0,0.01")] public float StepMaxHeight = 0.45f;

	[Export(PropertyHint.Range, "1.0,2.5,0.05")] public float StandEyeHeight = 1.7f;
	[Export(PropertyHint.Range, "0.5,2.0,0.05")] public float CrouchEyeHeight = 1.1f;

	[Export] public CollisionShape3D BodyCollision;
	protected CapsuleShape3D _capsule;
	protected Vector3 _headBasePos;

	public byte NetId;

	[Export] public Skeleton3D TpsSkeleton;
	protected HitboxRig _hitboxRig;
	/// <summary>Read-only access to the hitbox rig (for NetServer debug broadcasts that need positions).</summary>
	public HitboxRig GetHitboxRig() => _hitboxRig;
	/// <summary>Bone-Pose-History für Lag-Comp. Nur auf ServerAgent initialisiert (LocalPlayer/Puppet
	/// brauchen keine Server-Authoritative-Cast-Rewinds). Wird in <see cref="PlayerCore._Ready"/>
	/// nach <see cref="_hitboxRig"/>.Build() erzeugt.</summary>
	public BonePoseRewindBuffer BoneHistory;

	protected readonly MovementController _movement = new();
	public MovementController Movement => _movement;

	/// <summary>Standard lifecycle: sets physics tick rate, runs common setup, and dispatches to the subclass hook.</summary>
	public override void _Ready()
	{
		Engine.PhysicsTicksPerSecond = TickRate;
		_fixedDt = 1f / TickRate;

		SetupCapsule();
		SetupHitboxRig();
		ConfigureCollisionLayers();

		OnReady();
	}

	/// <summary>Subclass hook called after common setup. Wire up [Export] fields, cameras, etc. here.</summary>
	protected virtual void OnReady() { }

	/// <summary>Default collision layers: layer 2 (Characters) for LocalPlayer + Puppet.
	/// ServerBaseCharacter overrides with layer 5. PlayerCore overrides flag-based (multi-role during migration).
	/// Virtual instead of abstract so subclasses can optionally inherit.</summary>
	protected virtual void ConfigureCollisionLayers()
	{
		CollisionLayer = 1u << 1;
		CollisionMask = 1u | (1u << 1);
	}

	/// <summary>Builds a unique capsule resource per instance (duplicated from the scene-shared .tres) so that
	/// crouch resize does not shrink every player at once. Also configures floor behavior.</summary>
	protected void SetupCapsule()
	{
		if (BodyCollision == null) return;
		if (BodyCollision.Shape is not CapsuleShape3D cap)
		{
			GD.PushWarning("[BaseCharacter] BodyCollision.Shape is not a CapsuleShape3D — crouch resize will not work");
			return;
		}
		_capsule = (CapsuleShape3D)cap.Duplicate();
		_capsule.Height = StandHeight;
		_capsule.Radius = CapsuleRadius;
		BodyCollision.Shape = _capsule;
		BodyCollision.Position = new Vector3(0f, StandHeight / 2f, 0f);

		FloorMaxAngle = Mathf.DegToRad(FloorMaxAngleDeg);
		FloorSnapLength = FloorSnapDist;
		FloorBlockOnWall = true;
		FloorStopOnSlope = false;
		// MaxSlides reduziert von default 4 auf 2 — halbiert die per-MoveAndSlide-Cost beim Wall-
		// Slide (Profiler zeigte 0.4ms avg). 2 Iterations reichen für flat-wall + step-up; Corner-
		// stuck-Risiko minimal weil unsere Map keine engen Wedge-Corners hat. Falls bei spitzen
		// Geometrien Player stuck: zurück auf 3.
		MaxSlides = 2;
	}

	/// <summary>Spawns per-bone hitboxes if a skeleton is present. Required for all variants:
	/// server for hitscan damage resolve, client for (debug) visualization. ServerPlayer/Bot need
	/// the hitboxes for lag-comp even if no mesh is rendered.</summary>
	protected void SetupHitboxRig()
	{
		if (TpsSkeleton == null) return;
		_hitboxRig = new HitboxRig { Skeleton = TpsSkeleton, Name = "HitboxRig" };
		AddChild(_hitboxRig);
		_hitboxRig.Build();
	}

	/// <summary>Live resize of the capsule based on CrouchBlend. Virtual so LocalPlayer (PlayerCore)
	/// can additionally adjust the head pitch height (eye position). Skips the assignment when the
	/// height delta is below 0.1mm — capsule resize is expensive (re-cooks the shape) and the player
	/// can't see sub-mm changes anyway.</summary>
	protected virtual void ApplyCrouchHeight()
	{
		float blend = _movement.CrouchBlend;
		if (_capsule == null || BodyCollision == null) return;
		float h = Mathf.Lerp(StandHeight, CrouchHeight, blend);
		if (Mathf.Abs(_capsule.Height - h) < 0.0001f) return;
		_capsule.Height = h;
		var pos = BodyCollision.Position;
		pos.Y = h * 0.5f;
		BodyCollision.Position = pos;
	}
}
