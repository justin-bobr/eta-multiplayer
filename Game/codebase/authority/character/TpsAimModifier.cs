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
/// Required setup (in PlayerCore._Ready):
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
	[Export] public string AimBoneName = "spine_03";
	[Export(PropertyHint.Range, "0,1,0.05")] public float PitchScale = 0.6f;

	/// <summary>Y twist (radians). Set per frame by PuppetPlayer for upper-body rotation. 0 = no twist.</summary>
	public float SpineTwist;

	private int _boneIdx = -1;
	private int _parentBoneIdx = -1;
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
		using var _prof = (_characterBody is PlayerCore pc && pc.IsServerAgent)
			? MiniProfiler.SampleServer("TpsAimModifier._ProcessModification")
			: MiniProfiler.SampleClient("TpsAimModifier._ProcessModification");
		if (!_resolved) Resolve();
		if (_boneIdx < 0 || HeadPitch == null || _characterBody == null) return;
		var skel = GetSkeleton();
		if (skel == null) return;

		float pitch = HeadPitch.Rotation.X * PitchScale;
		float twist = SpineTwist * PitchScale;

		Quaternion bodyRot = _characterBody.GlobalTransform.Basis.GetRotationQuaternion();
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

		Quaternion newPoseRot = extraInParentLocal * _restRot;
		skel.SetBonePoseRotation(_boneIdx, newPoseRot);
	}
}
