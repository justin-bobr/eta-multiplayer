using Godot;
using System.Collections.Generic;

namespace Vantix.Character;

/// <summary>
/// Editor tool that generates and sizes per-bone hitbox capsules from a skeleton. Assign
/// <see cref="Skeleton"/> (optionally a <see cref="HitboxContainer"/> and per-slot bone overrides), then
/// tick <see cref="Build"/>: creates BoneAttachment3D → Hitbox → CollisionShape3D per slot, positions the
/// capsule between the bone and its first child, sizes the radius from the mesh, and parents into the
/// edited scene so it saves. Each *Bone field is a dropdown of the skeleton's bone names.
/// </summary>
[Tool, GlobalClass]
public partial class HitboxBaker : Node3D
{
	[ExportGroup("Hitbox")]
	[Export]
	public Skeleton3D Skeleton
	{
		get => _skeleton;
		set { _skeleton = value; NotifyPropertyListChanged(); }
	}
	private Skeleton3D _skeleton;

	/// <summary>Where the generated hitbox nodes are placed. Leave empty to create + use a "Hitboxes" node
	/// under the skeleton.</summary>
	[Export]
	public Node3D HitboxContainer;

	[ExportSubgroup("Bones")]
	[Export] public string HeadBone = "head";
	[Export] public string ChestBone = "spine_03";
	[Export] public string WaistBone = "pelvis";
	[Export] public string LeftUpperArmBone = "upperarm_l";
	[Export] public string RightUpperArmBone = "upperarm_r";
	[Export] public string LeftLowerArmBone = "lowerarm_l";
	[Export] public string RightLowerArmBone = "lowerarm_r";
	[Export] public string LeftHandBone = "hand_l";
	[Export] public string RightHandBone = "hand_r";
	[Export] public string LeftThighBone = "thigh_l";
	[Export] public string RightThighBone = "thigh_r";
	[Export] public string LeftCalfBone = "calf_l";
	[Export] public string RightCalfBone = "calf_r";
	[Export] public string LeftFootBone = "foot_l";
	[Export] public string RightFootBone = "foot_r";
	[Export] public string Spine01Bone = "spine_01";
	[Export] public string Spine02Bone = "spine_02";
	[Export] public string Spine04Bone = "spine_04";
	[Export] public string Spine05Bone = "spine_05";
	[Export] public string LeftClavicleBone = "clavicle_l";
	[Export] public string RightClavicleBone = "clavicle_r";
	[Export] public string LeftFootBallBone = "ball_l";
	[Export] public string RightFootBallBone = "ball_r";

	[ExportSubgroup("Bake")]
	[Export]
	public bool Build
	{
		get => false;
		set { if (value && Engine.IsEditorHint()) Bake(); }
	}

	[Export]
	public bool Baked;

	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if (_skeleton == null) return;
		string name = (string)property["name"];
		if (!name.EndsWith("Bone")) return;
		property["hint"] = (int)PropertyHint.Enum;
		property["hint_string"] = BoneEnumHint();
	}

	private string BoneEnumHint()
	{
		var names = new List<string>();
		for (int i = 0; i < _skeleton.GetBoneCount(); i++) names.Add(_skeleton.GetBoneName(i));
		return string.Join(",", names);
	}

	private void Bake()
	{
		if (_skeleton == null)
		{
			GD.PushWarning("[HitboxBaker] No Skeleton assigned — nothing to bake.");
			return;
		}
		var remap = new Dictionary<string, string>
		{
			{ "head", HeadBone }, { "spine_03", ChestBone }, { "pelvis", WaistBone },
			{ "upperarm_l", LeftUpperArmBone }, { "upperarm_r", RightUpperArmBone },
			{ "lowerarm_l", LeftLowerArmBone }, { "lowerarm_r", RightLowerArmBone },
			{ "hand_l", LeftHandBone }, { "hand_r", RightHandBone },
			{ "thigh_l", LeftThighBone }, { "thigh_r", RightThighBone },
			{ "calf_l", LeftCalfBone }, { "calf_r", RightCalfBone },
			{ "foot_l", LeftFootBone }, { "foot_r", RightFootBone },
			{ "spine_01", Spine01Bone }, { "spine_02", Spine02Bone },
			{ "spine_04", Spine04Bone }, { "spine_05", Spine05Bone },
			{ "clavicle_l", LeftClavicleBone }, { "clavicle_r", RightClavicleBone },
			{ "ball_l", LeftFootBallBone }, { "ball_r", RightFootBallBone },
		};
		var rig = new HitboxRig { Skeleton = _skeleton };
		rig.BakeDefaultHitboxes(GetTree().EditedSceneRoot, HitboxContainer, remap);
		rig.Free();
		Baked = true;
		GD.Print("[HitboxBaker] Generated + sized hitbox capsules — save the scene to persist. (Debug → Visible Collision Shapes to view.)");
	}
}
