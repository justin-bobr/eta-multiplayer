using Godot;

namespace Vantix.Character;

/// <summary>Result of a single ray / hitscan query — what was hit, where, the surface normal and material.</summary>
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
