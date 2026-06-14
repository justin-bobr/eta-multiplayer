using Godot;

namespace Vantix.Character;

/// <summary>WeaponBoneModifier's ADS/recoil offset on ik_hand_gun; runs as a SkeletonModifier3D after the WeaponBoneModifier.</summary>
[Tool, GlobalClass]
public partial class TwoBoneArmIK : SkeletonModifier3D
{
	[Export] public StringName UpperBone = "upperarm_l";
	[Export] public StringName LowerBone = "lowerarm_l";
	[Export] public StringName EndBone = "hand_l";
	[Export] public StringName TargetBone = "ik_hand_l";
	[Export] public bool CopyTargetRotation = true;

	private int _u = -1, _l = -1, _e = -1, _t = -1;

	private void Resolve(Skeleton3D sk)
	{
		_u = sk.FindBone(UpperBone);
		_l = sk.FindBone(LowerBone);
		_e = sk.FindBone(EndBone);
		_t = sk.FindBone(TargetBone);
	}

	public override void _ProcessModificationWithDelta(double delta)
	{
		var sk = GetSkeleton();
		if (sk == null)
			return;
		using var _prof = MiniProfiler.SampleClient("TwoBoneArmIK._ProcessModification");
		float infl = GetInfluence();
		if (infl <= 0f)
			return;
		if (_u < 0)
			Resolve(sk);
		if (_u < 0 || _l < 0 || _e < 0 || _t < 0)
			return;

		Transform3D gUpper = sk.GetBoneGlobalPose(_u);
		Vector3 s = gUpper.Origin;
		Vector3 m = sk.GetBoneGlobalPose(_l).Origin;
		Vector3 e = sk.GetBoneGlobalPose(_e).Origin;
		Transform3D gTarget = sk.GetBoneGlobalPose(_t);
		Vector3 t = gTarget.Origin;

		float lenUpper = (m - s).Length();
		float lenLower = (e - m).Length();
		float reach = lenUpper + lenLower;
		if (lenUpper < 1e-5f || lenLower < 1e-5f)
			return;

		Vector3 toTarget = t - s;
		if (toTarget.LengthSquared() < 1e-8f)
			return;
		float distST = Mathf.Clamp(toTarget.Length(), Mathf.Abs(lenUpper - lenLower) + 1e-3f, reach - 1e-3f);
		Vector3 dirST = toTarget.Normalized();

		// Keep the elbow in its current bend plane: pole = perpendicular component of the current
		// shoulder->elbow vector. Falls back to the upper bone's Y axis when the arm is straight.
		Vector3 curElbow = m - s;
		Vector3 polePerp = curElbow - dirST * curElbow.Dot(dirST);
		if (polePerp.LengthSquared() < 1e-6f)
			polePerp = gUpper.Basis.Y;
		polePerp = polePerp.Normalized();
		Vector3 bendAxis = dirST.Cross(polePerp);
		if (bendAxis.LengthSquared() < 1e-6f)
			return;
		bendAxis = bendAxis.Normalized();

		float cosSh = (lenUpper * lenUpper + distST * distST - lenLower * lenLower) / (2f * lenUpper * distST);
		float shoulderAngle = Mathf.Acos(Mathf.Clamp(cosSh, -1f, 1f));

		Vector3 dirToElbow = dirST.Rotated(bendAxis, shoulderAngle);
		Vector3 newElbow = s + dirToElbow * lenUpper;

		AlignBone(sk, _u, m - s, newElbow - s, infl);
		Vector3 m2 = sk.GetBoneGlobalPose(_l).Origin;
		Vector3 e2 = sk.GetBoneGlobalPose(_e).Origin;
		AlignBone(sk, _l, e2 - m2, t - m2, infl);

		if (CopyTargetRotation)
		{
			int parent = sk.GetBoneParent(_e);
			Basis parentGlobal = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis : Basis.Identity;
			Quaternion local = (parentGlobal.Inverse() * gTarget.Basis).GetRotationQuaternion().Normalized();
			Quaternion cur = sk.GetBonePoseRotation(_e);
			sk.SetBonePoseRotation(_e, cur.Slerp(local, infl));
		}
	}

	// Influence comes from base SkeletonModifier3D.GetInfluence(). The previous custom override called
	// Get("influence") with a string literal, allocating a StringName per arm per frame (~470KB/10s),
	// and caused a CS0108 hides-inherited-member warning.

	// Rotate a bone so its global `from` direction points along `to`, blended by influence.
	private void AlignBone(Skeleton3D sk, int bone, Vector3 from, Vector3 to, float infl)
	{
		if (from.LengthSquared() < 1e-8f || to.LengthSquared() < 1e-8f)
			return;
		Quaternion delta = new Quaternion(from.Normalized(), to.Normalized());
		if (infl < 1f)
			delta = Quaternion.Identity.Slerp(delta, infl);
		Basis newGlobal = new Basis(delta) * sk.GetBoneGlobalPose(bone).Basis;
		int parent = sk.GetBoneParent(bone);
		Basis parentGlobal = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis : Basis.Identity;
		Quaternion local = (parentGlobal.Inverse() * newGlobal).GetRotationQuaternion().Normalized();
		sk.SetBonePoseRotation(bone, local);
	}
}
