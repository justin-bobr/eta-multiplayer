using Godot;

// Applies the fire-selector bone pose AFTER the AnimationMixer has written the skeleton — same pattern
// as WeaponBoneModifier/TwoBoneArmIK. Replaces the old ordering hack where WeaponAnimation manually
// advanced its tree in _Process just so the override could run after the mixer.
[GlobalClass]
public partial class FireSelectorModifier : SkeletonModifier3D
{
	public WeaponAnimation Weapon;

	public override void _ProcessModificationWithDelta(double delta) => Weapon?.ApplyFireSelectorPose();
}
