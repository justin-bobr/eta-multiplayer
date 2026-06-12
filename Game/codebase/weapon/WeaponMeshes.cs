using Godot;

[GlobalClass]
public partial class WeaponMeshes : Node
{
	[ExportGroup("Meshes")]
	[Export] public string MeshHandguard = "";
	[Export] public string MeshScope = "";
	[Export] public string MeshSightFront = "";
	[Export] public string MeshSightRear = "";
	[Export] public string MeshLaser = "";
	[Export] public string MeshSilencer = "";
	[Export] public string MeshGripVertical = "";
	[Export] public string MeshGripAngled = "";

	[ExportGroup("Sockets")]
	[Export] public NodePath HandguardSocket;
	[Export] public NodePath ScopeSocket;
	[Export] public NodePath SightFrontSocket;
	[Export] public NodePath SightRearSocket;
	[Export] public NodePath LaserSocket;
	[Export] public NodePath MuzzleSocket;
	[Export] public NodePath GripSocket;

	public override void _Ready()
	{
		Attach(MeshHandguard, HandguardSocket);
		Attach(MeshScope, ScopeSocket);
		Attach(MeshSightFront, SightFrontSocket);
		Attach(MeshSightRear, SightRearSocket);
		Attach(MeshLaser, LaserSocket);
		Attach(MeshSilencer, MuzzleSocket);
		Attach(MeshGripVertical, GripSocket);
		Attach(MeshGripAngled, GripSocket);
	}

	private void Attach(string path, NodePath socket)
	{
		if (string.IsNullOrEmpty(path) || socket.IsEmpty) return;
		var target = GetNodeOrNull<Node3D>(socket);
		if (target == null) return;
		var mesh = ResourceLoader.Load<Mesh>(path);
		if (mesh == null) return;
		target.AddChild(new MeshInstance3D { Mesh = mesh });
	}
}
