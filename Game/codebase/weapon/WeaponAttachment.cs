using Godot;

namespace Vantix.Weapon;

/// <summary>Marks a child node as a weapon attachment of a given type and variant.</summary>
[Tool, GlobalClass]
public partial class WeaponAttachment : Node3D
{
	[Export] public AttachmentType Group;
	[Export] public AttachmentVariant Variant = AttachmentVariant.Default;
}
