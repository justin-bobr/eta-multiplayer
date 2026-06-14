using Godot;

namespace Vantix.Character;

public struct HitInfo
{
	public bool Hit;
	public Vector3 Origin;
	public Vector3 Direction;
	public Vector3 Position;
	public Vector3 Normal;
	public Node3D Collider;
	public float Distance;
	public StringName Material;
}
