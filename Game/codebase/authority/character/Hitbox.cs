using Godot;

/// <summary>
/// Scene-node hitbox (capsule/box/sphere). User drops these as a child of a BoneAttachment3D
/// under the TpsSkeleton in the weapon scene; position/rotation/shape are tunable in the 3D
/// editor like any normal collider. <see cref="HitboxRig"/> scans the skeleton in _Ready,
/// configures the layer (4 = hitbox) and registers the RIDs for self-exclude.
///
/// Setup example per bone:
///   TpsSkeleton
///   - BoneAttachment3D (BoneIdx = head)
///      - Hitbox (DamageMul=4.0, Label="head")
///         - CollisionShape3D (SphereShape3D Radius=0.13)
///
/// Damage = <see cref="WeaponStats.Damages"/>[Label]; the label routes the hitbox to the matching
/// part group ("head", "chest", "waist", "leg", "foot"). Damage values are configured per weapon
/// in <see cref="WeaponStats"/> (dictionary, same pattern as range), NOT here.
/// </summary>
[Tool, GlobalClass]
public partial class Hitbox : StaticBody3D
{
	/// <summary>Hitbox-Zone (Head/Chest/Arm/Leg/...). Im Editor als Dropdown. Server liest das via
	/// <see cref="HitboxRig.ReadGroup"/> + sucht den passenden Damage-Wert in <see cref="WeaponStats.Damages"/>.</summary>
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
