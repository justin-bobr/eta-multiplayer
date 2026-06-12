using Godot;

/// <summary>
/// <see cref="SkeletonModifier3D"/> for TPS body aim rotation. Läuft NACH dem AnimationMixer aber VOR
/// dem Skeleton-Render-Flush (race-frei vs direktem SetBonePoseRotation aus _Process).
///
/// WORLD-SPACE-Ansatz: rig-orientation-unabhängig. Wir rotieren die Bone-Pose so, dass im WELT-Raum
/// die zusätzliche Rotation (Pitch um Body-Right, Twist um Welt-Up) auf die Rest-Pose draufkommt.
/// Vorgänger-Version benutzte <c>Vector3.Right</c>/<c>Vector3.Up</c> als Achsen in bone-local-Raum —
/// das funktioniert nur für Mixamo-Style-Rigs wo bone-local +X = body's right ist. Bei Rigs wo die
/// Bone-Achsen anders orientiert sind (unser puppet_player.tscn: pelvis-local +Y = body's right),
/// war Pitch komplett tot und Twist nur minimal sichtbar. Der World-Space-Ansatz funktioniert für
/// jeden Rig.
///
/// Required setup (in NetworkPlayer._Ready):
///   - Add as child of Skeleton3D
///   - Set <see cref="HeadPitch"/> to the head-pitch node (provides the pitch rotation)
///   - <see cref="AimBoneName"/> matches the bone to twist (default "spine_03")
///   - <see cref="PitchScale"/> = how much of the head pitch is forwarded to the bone (0..1)
///
/// Per-frame state (set by game code):
///   - <see cref="SpineTwist"/> = view yaw minus body yaw (radians) — for puppet upper-body twist.
///     LocalPlayer in FPS mode stays at 0 (body follows mouse fully).
/// </summary>
[Tool, GlobalClass]
public partial class TpsAimModifier : SkeletonModifier3D
{
	[Export] public Node3D HeadPitch;
	/// <summary>Optional body-orientation source for the world-space pitch axis. Defaults to the owning
	/// CharacterBody3D; set it when the visible body is a separate node (NetworkPlayer's GlowVisual).</summary>
	[Export] public Node3D BodyNode;
	[Export] public string AimBoneName = "spine_03";
	/// <summary>Weapon bone (root-IK-chain, NOT under the spine) carried along with the aim bone so it stays
	/// glued to the leaning chest. Rotated by the same extra rotation about the aim-bone joint, so the gun and
	/// the spine-driven arms pivot together and the gun stays in the hands. Empty = no weapon follow.</summary>
	[Export] public StringName WeaponBoneName = "ik_hand_gun";
	[Export(PropertyHint.Range, "0,1,0.05")] public float PitchScale = 0.6f;
	/// <summary>false (default, NetworkPlayer): replace the bone with rest+aim. true (NetworkPlayer): add the
	/// aim on top of the animated pose, so idle/montage spine motion survives and it is a no-op when pitch=0.</summary>
	[Export] public bool Additive;

	/// <summary>Direct pitch (radians) used when <see cref="HeadPitch"/> is null — lets non-NetworkPlayer drivers
	/// (NetworkPlayer) feed the aim pitch straight in.</summary>
	public float Pitch;
	/// <summary>Y twist (radians). Set per frame by PuppetPlayer for upper-body rotation. 0 = no twist.</summary>
	public float SpineTwist;

	private int _boneIdx = -1;
	private int _parentBoneIdx = -1;
	private int _weaponBoneIdx = -1;
	private int _weaponParentIdx = -1;
	private Quaternion _restRot;
	private bool _resolved;
	private Node3D _characterBody;

	/// <summary>Resolves the aim bone index on ready.</summary>
	public override void _Ready() => Resolve();

	/// <summary>Lazily resolves the aim bone index, caches its rest rotation, and walks up to the owning CharacterBody3D.</summary>
	private void Resolve()
	{
		if (_resolved) return;
		var skel = GetSkeleton();
		if (skel == null || string.IsNullOrEmpty(AimBoneName)) return;
		_boneIdx = skel.FindBone(AimBoneName);
		if (_boneIdx >= 0)
		{
			_restRot = skel.GetBonePoseRotation(_boneIdx);
			_parentBoneIdx = skel.GetBoneParent(_boneIdx);
		}
		else
		{
			GD.PushWarning($"[TpsAimModifier] Bone '{AimBoneName}' not found in skeleton — pitch/twist disabled");
		}

		if (!string.IsNullOrEmpty(WeaponBoneName))
		{
			_weaponBoneIdx = skel.FindBone(WeaponBoneName);
			if (_weaponBoneIdx >= 0) _weaponParentIdx = skel.GetBoneParent(_weaponBoneIdx);
		}

		Node n = skel;
		while (n != null)
		{
			if (n is CharacterBody3D cb) { _characterBody = cb; break; }
			n = n.GetParent();
		}

		_resolved = true;
	}

	/// <summary>
	/// Modifier-Pass: setzt die Bone-Pose-Rotation so dass im Welt-Raum die zusätzliche Rotation
	/// (Pitch um Body-Right, Twist um Welt-Up) auf die Rest-Pose draufkommt. Übersteht den AnimMixer.
	/// </summary>
	public override void _ProcessModificationWithDelta(double delta)
	{
		// SV/CL je nach Owner: derselbe Modifier-Typ läuft auf ServerAgent UND Puppet im Listen-Mode.
		using var _prof = (_characterBody is NetworkPlayer pc && pc.IsServerAgent)
			? MiniProfiler.SampleServer("TpsAimModifier._ProcessModification")
			: MiniProfiler.SampleClient("TpsAimModifier._ProcessModification");
		if (!_resolved) Resolve();
		if (_boneIdx < 0) return;
		var skel = GetSkeleton();
		if (skel == null) return;
		Node3D body = BodyNode ?? _characterBody;
		if (body == null) return;

		float pitch = (HeadPitch != null ? HeadPitch.Rotation.X : Pitch) * PitchScale;
		float twist = SpineTwist * PitchScale;

		Quaternion bodyRot = body.GlobalTransform.Basis.GetRotationQuaternion();
		Vector3 bodyRightWorld = bodyRot * Vector3.Right;
		Vector3 worldUp = Vector3.Up;

		Quaternion pitchWorld = new Quaternion(bodyRightWorld, pitch);
		Quaternion twistWorld = new Quaternion(worldUp, twist);
		Quaternion extraWorld = twistWorld * pitchWorld;

		Transform3D parentSkelLocal = _parentBoneIdx >= 0
			? skel.GetBoneGlobalPose(_parentBoneIdx)
			: Transform3D.Identity;
		Quaternion skelRot = skel.GlobalTransform.Basis.GetRotationQuaternion();
		Quaternion parentGlobalWorld = skelRot * parentSkelLocal.Basis.GetRotationQuaternion();

		Quaternion extraInParentLocal = parentGlobalWorld.Inverse() * extraWorld * parentGlobalWorld;

		Quaternion basePose = Additive ? skel.GetBonePoseRotation(_boneIdx) : _restRot;
		Quaternion newPoseRot = extraInParentLocal * basePose;
		skel.SetBonePoseRotation(_boneIdx, newPoseRot);

		// Carry the weapon bone (root-IK chain, not under the spine) along with the chest lean: rigidly
		// rotate ik_hand_gun about the aim-bone joint by the same world rotation, so the gun and the
		// spine-driven arms pivot together and the gun stays in the hands. Skipped when pitch/twist ~0
		// (local player in FPS), so it's a no-op there.
		if (_weaponBoneIdx >= 0 && (Mathf.Abs(pitch) > 0.0001f || Mathf.Abs(twist) > 0.0001f))
		{
			Basis extraBasis = new Basis(extraWorld);
			Transform3D weaponParentSkel = _weaponParentIdx >= 0 ? skel.GetBoneGlobalPose(_weaponParentIdx) : Transform3D.Identity;
			// Read the gun's FRESH local pose (it carries the ADS aim-pose additive the mixer just wrote) and
			// compose it onto its root-chain parent. GetBoneGlobalPose(gun) can lag that write and would strip
			// the ADS movement back to idle — so we never read the gun's global directly.
			Transform3D gunLocal = new Transform3D(new Basis(skel.GetBonePoseRotation(_weaponBoneIdx)), skel.GetBonePosePosition(_weaponBoneIdx));
			Transform3D gunSkel = weaponParentSkel * gunLocal;
			Transform3D gunWorld = skel.GlobalTransform * gunSkel;
			Vector3 pivotWorld = skel.GlobalTransform * skel.GetBoneGlobalPose(_boneIdx).Origin;
			Vector3 newOriginWorld = pivotWorld + extraBasis * (gunWorld.Origin - pivotWorld);
			Basis newBasisWorld = extraBasis * gunWorld.Basis;
			Transform3D desiredSkel = skel.GlobalTransform.AffineInverse() * new Transform3D(newBasisWorld, newOriginWorld);
			Transform3D localPose = weaponParentSkel.AffineInverse() * desiredSkel;
			skel.SetBonePosePosition(_weaponBoneIdx, localPose.Origin);
			skel.SetBonePoseRotation(_weaponBoneIdx, localPose.Basis.GetRotationQuaternion());
		}
	}
}
