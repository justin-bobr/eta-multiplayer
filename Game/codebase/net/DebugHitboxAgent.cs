using Godot;

/// <summary>Per-agent snapshot of server hitbox transforms (pos + rotation) for the debug visualiser.</summary>
public struct DebugHitboxAgent
{
	public byte NetId;
	public Transform3D[] Transforms;
}
