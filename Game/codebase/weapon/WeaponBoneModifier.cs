using Godot;

[Tool, GlobalClass]
public partial class WeaponBoneModifier : SkeletonModifier3D
{
	[Export] public StringName BoneName = "ik_hand_gun";

	private int _boneIdx = -1;

	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if ((string)property["name"] == "BoneName")
		{
			var skel = GetSkeleton();
			if (skel != null)
			{
				var names = new Godot.Collections.Array<string>();
				for (var i = 0; i < skel.GetBoneCount(); i++)
					names.Add(skel.GetBoneName(i));
				property["hint"] = (int)PropertyHint.Enum;
				property["hint_string"] = string.Join(",", names);
			}
		}
	}

	public override void _ProcessModificationWithDelta(double delta)
	{
		var skel = GetSkeleton();
		if (skel == null) return;
		using var _prof = MiniProfiler.SampleClient("WeaponBoneModifier._ProcessModification");

		// Apply the procedural weapon offset (ADS / crouch / canted / recoil) to the weapon bone, exactly
		// like Unreal's ModifyBone on ik_hand_gun. The grip bones ik_hand_l/r ride this bone, so the
		// TwoBoneArmIK hand targets move with the offset and both hands follow the weapon automatically.
		// Runs in the editor too so NetworkPlayer's ADS test mode can preview the aimed pose live.
		if (Transform == Transform3D.Identity) return;
		if (_boneIdx < 0) _boneIdx = skel.FindBone(BoneName);
		if (_boneIdx < 0) return;
		var pose = new Transform3D(new Basis(skel.GetBonePoseRotation(_boneIdx)), skel.GetBonePosePosition(_boneIdx));
		var result = pose * Transform;
		skel.SetBonePosePosition(_boneIdx, result.Origin);
		skel.SetBonePoseRotation(_boneIdx, result.Basis.GetRotationQuaternion());
	}
}
