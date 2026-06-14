using Godot;

/// <summary>
/// Self-contained Node3D mount for the TPS Foot IK (child of the player scene, no NetworkPlayer hook).
/// Per tick, reads the parent CharacterBody3D's velocity/position/basis and feeds the <see cref="TpsFootIk"/>
/// sim (ground snap raycast + TwoBoneIK3D influence). Server scenes have no mount, so the server agent
/// pays no Foot-IK cost.
/// </summary>
public partial class TpsFootIkMount : Node3D
{
	/// <summary>Master toggle — when false, Init is skipped entirely.</summary>
	[Export] public bool EnableFootIk = true;
	/// <summary>Influence toggle for the leg IK (smooth-lerped 0..1).</summary>
	[Export] public bool EnableLegIK = true;
	/// <summary>Show visible foot target spheres.</summary>
	[Export] public bool DebugMarkers = false;
	[Export] public Skeleton3D Skeleton;
	[Export] public Node IkLeftNode;
	[Export] public Node IkRightNode;
	[Export] public Node3D TargetLeft;
	[Export] public Node3D TargetRight;
	[Export] public Node3D PoleLeft;
	[Export] public Node3D PoleRight;
	/// <summary>Which physics layers count as "ground" for the snap raycast.</summary>
	[Export] public uint GroundMask = 1;

	private TpsFootIk _ik;
	private CharacterBody3D _parent;

	/// <summary>Initializes the IK sim if a CharacterBody3D parent and skeleton are wired up.</summary>
	public override void _Ready()
	{
		_parent = GetParentOrNull<CharacterBody3D>();
		if (_parent == null)
		{
			GD.PushWarning("[TpsFootIkMount] No CharacterBody3D parent — Foot IK disabled");
			return;
		}
		if (!EnableFootIk || Skeleton == null) return;

		_ik = new TpsFootIk
		{
			GroundMask = GroundMask,
			EnableLegIK = EnableLegIK,
			EnableDebugMarkers = DebugMarkers,
			IkLeftNode = IkLeftNode,
			IkRightNode = IkRightNode,
			TargetLeft = TargetLeft,
			TargetRight = TargetRight,
			PoleLeft = PoleLeft,
			PoleRight = PoleRight,
			PlayerRid = _parent.GetRid(),
		};
		if (!_ik.Initialize(Skeleton))
			_ik = null;
	}

	/// <summary>Drives the IK sim each physics tick using the parent body's transform and velocity.</summary>
	public override void _PhysicsProcess(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("TpsFootIkMount._PhysicsProcess");
		if (_ik == null || _parent == null) return;
		_ik.EnableLegIK = EnableLegIK && _parent.IsOnFloor();
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return;
		_ik.Update((float)delta, _parent.Velocity, _parent.GlobalPosition, _parent.GlobalTransform.Basis, space);
	}
}
