using Godot;

namespace Vantix.Net;

/// <summary>One player's hitbox transforms, sent to clients for the debug hitbox overlay.</summary>
public struct DebugHitboxAgent
{
	public byte NetId;
	public Transform3D[] Transforms;
}
