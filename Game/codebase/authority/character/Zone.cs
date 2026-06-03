using Godot;

/// <summary>
/// Named, non-blocking 3D region for two roles:
///   1. **UI**: shows the player which area they're currently in. <see cref="HudCs2"/> /
///      <see cref="Map.ZoneAt"/> read the player position each frame, find the innermost Zone
///      containing them, and write <see cref="ZoneName"/> into the HUD's zone label.
///   2. **Bot navigation**: every Zone's centre is a long-range target candidate for
///      <see cref="BotController"/> — bots pick a random Zone and walk there via
///      <see cref="NavigationServer3D.MapGetPath"/>. The Zone box doesn't drive any pathfinding
///      geometry; the NavMesh is baked from the world's collision shapes, and Zones just provide
///      semantic destination points.
///
/// Single self-contained node: no child <see cref="CollisionShape3D"/>, no child Label3D — the
/// box shape is attached directly via the <see cref="CollisionObject3D.CreateShapeOwner"/> API
/// so the scene tree shows just the Zone itself. The wireframe outline in the editor is drawn
/// by <see cref="ZoneGizmoPlugin"/> via Godot's <see cref="EditorNode3DGizmoPlugin"/> system —
/// which means the standard 3D View → Gizmos toggle hides / shows it.
///
/// CollisionLayer is forced to 0 (Zone never blocks anyone), <see cref="Area3D.Monitoring"/> is
/// on, <see cref="Area3D.Monitorable"/> is off. Default <see cref="Area3D.CollisionMask"/> = 2
/// (the LocalPlayer / Puppet body layer); override on per-zone basis if a map uses a different
/// layer convention.
/// </summary>
[Tool, GlobalClass]
public partial class Zone : Area3D
{
	/// <summary>Display name for this region. Read by the HUD for the "you are in: …" line.
	/// Free-form string, kept short ("Long", "B-Tunnels", "Pit") since it's drawn under the
	/// compass.</summary>
	[Export] public string ZoneName { get; set; } = "Zone";

	/// <summary>Box extents of the zone in meters. Drives the internal <see cref="BoxShape3D"/>
	/// sizing; reapplied whenever the property changes so the editor reflects edits live.</summary>
	[Export]
	public Vector3 Size
	{
		get => _size;
		set { _size = value; UpdateBoxShape(); UpdateGizmos(); }
	}
	private Vector3 _size = new(4f, 2f, 4f);

	/// <summary>Group name every Zone joins on enter-tree. <see cref="Map.Scan"/> and any code
	/// that needs to enumerate zones by group can use this constant.</summary>
	public const string GroupName = "zone";

	private uint _shapeOwnerId;
	private BoxShape3D _boxShape;
	private bool _shapeOwnerReady;

	public override void _Ready()
	{
		EnsureShape();
		UpdateBoxShape();

		// Zone is detect-only. Layer 0 = nobody collides with this body; Monitoring keeps the
		// body_entered / body_exited signals firing for the configured mask (default 2 =
		// LocalPlayer + PuppetPlayer body layer). Monitorable off so other Areas don't pick
		// this Zone up either (it's not "physics geometry", just a label region).
		CollisionLayer = 0;
		if (CollisionMask == 0) CollisionMask = 2;
		Monitoring = true;
		Monitorable = false;

		if (!IsInGroup(GroupName)) AddToGroup(GroupName);
		UpdateGizmos();
	}

	/// <summary>Registers a shape owner on this Area3D and attaches a fresh <see cref="BoxShape3D"/>
	/// directly to it. ShapeOwners are runtime state (not serialised in the .tscn) so this runs
	/// every time the node enters the tree; the size comes from the <see cref="Size"/> export
	/// which IS serialised, so the box ends up at the right dimensions after every load.</summary>
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
