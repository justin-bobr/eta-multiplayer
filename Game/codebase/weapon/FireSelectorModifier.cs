using Godot;

namespace Vantix.Weapon;

// Applies the fire-selector bone pose after the AnimationMixer writes the skeleton (like WeaponBoneModifier/TwoBoneArmIK).
[GlobalClass]
public partial class FireSelectorModifier : SkeletonModifier3D
{
	public WeaponAnimation Weapon;

	public override void _ProcessModificationWithDelta(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("FireSelectorModifier._ProcessModification");
		Weapon?.ApplyFireSelectorPose();
	}
}
