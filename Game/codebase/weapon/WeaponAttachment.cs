using Godot;

namespace Vantix.Weapon;

public enum AttachmentType
{
	Scope,
	SightRear,
	SightFront,
	Stock,
	Barel,
	Grip,
	Laser,
	Silencer
}

public enum AttachmentVariant
{
	Default,
	Extended,
	Short
}

[Tool, GlobalClass]
public partial class WeaponAttachment : Node3D
{
	[Export] public AttachmentType Group;
	[Export] public AttachmentVariant Variant = AttachmentVariant.Default;
}
