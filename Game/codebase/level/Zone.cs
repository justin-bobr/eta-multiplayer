using Godot;

/// <summary>
/// Named, non-blocking 3D region: drives the HUD "you are in" label (innermost match via
/// <see cref="Level.ZoneAt"/>) and serves as a bot navigation target. Self-contained — the box shape
/// is attached via <see cref="CollisionObject3D.CreateShapeOwner"/>; the editor outline is drawn by
/// <see cref="ZoneGizmoPlugin"/>.
/// CollisionLayer is forced to 0 (never blocks), Monitoring on, Monitorable off, default Mask = 2
/// (player body layer).
/// </summary>
[Tool, GlobalClass]
public partial class Zone : Area3D
{
	/// <summary>Display name shown in the HUD; keep it short ("Long", "B-Tunnels", "Pit").</summary>
	[Export] public string ZoneName { get; set; } = "Zone";

	/// <summary>Box extents in meters; drives the internal <see cref="BoxShape3D"/>, reapplied on change.</summary>
	[Export]
	public Vector3 Size
	{
		get => _size;
		set { _size = value; UpdateBoxShape(); UpdateGizmos(); }
	}
	private Vector3 _size = new(4f, 2f, 4f);

	private uint _shapeOwnerId;
	private BoxShape3D _boxShape;
	private bool _shapeOwnerReady;

	public override void _Ready()
	{
		EnsureShape();
		UpdateBoxShape();

		CollisionLayer = 0;
		if (CollisionMask == 0) CollisionMask = 2;
		Monitoring = true;
		Monitorable = false;

		UpdateGizmos();
	}

	/// <summary>Attaches a fresh <see cref="BoxShape3D"/> shape owner; runs each tree entry since ShapeOwners aren't serialised.</summary>
	private void EnsureShape()
	{
		if (_shapeOwnerReady) return;
		_shapeOwnerId = CreateShapeOwner(this);
		_boxShape = new BoxShape3D();
		ShapeOwnerAddShape(_shapeOwnerId, _boxShape);
		_shapeOwnerReady = true;
	}

	private void UpdateBoxShape()
	{
		if (_boxShape != null) _boxShape.Size = _size;
	}
}
