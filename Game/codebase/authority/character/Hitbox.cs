using Godot;

/// <summary>
/// Scene-node hitbox (capsule/box/sphere), dropped under a BoneAttachment3D in the skeleton.
/// <see cref="HitboxRig"/> scans the skeleton in _Ready and configures the layer / self-exclude RIDs.
/// The <see cref="Group"/> routes damage via <see cref="WeaponStats.Damages"/>, configured per weapon.
/// </summary>
[Tool, GlobalClass]
public partial class Hitbox : StaticBody3D
{
	/// <summary>Hitbox zone (Head/Chest/Arm/Leg/...); keys the damage lookup in
	/// <see cref="WeaponStats.Damages"/>.</summary>
	[Export] public HitboxGroup Group = HitboxGroup.Body;

	/// <summary>Configures the collision layer/mask and ensures the "flesh" group membership.</summary>
	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		CollisionLayer = HitboxRig.Layer;
		CollisionMask = 0u;
		if (!IsInGroup("flesh")) AddToGroup("flesh");
	}
}
