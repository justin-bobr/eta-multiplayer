using Godot;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class AnimatedCharacter : CharacterBody3D
{
	[ExportGroup("Mode")]
	// GameMode: Local = the player on this client (own viewmodel + aim guide), Remote = another player's puppet,
	// Server = headless (all cameras off, no footsteps, no weapon event animations). ViewMode picks the camera
	// perspective for Local/Remote; Server ignores it.
	[Export] public GameMode CurrentGameMode = GameMode.Local;
	[Export] public ViewMode CurrentViewMode = ViewMode.Fps;

	[ExportGroup("FPS")]
	[ExportSubgroup("Camera")]
	[Export] public NodePath HeadCameraPath;
	[Export] public NodePath ViewmodelCameraPath;
	// The viewmodel CanvasLayer (FPS arms overlay). Shown only in FPS view; hidden in TPS and on the server.
	[Export] public NodePath ViewmodelLayerPath;
	// The weapon's eye socket inside the viewport (SOCKET_CameraFP). The viewmodel camera rides its full pose
	// each frame so the gun renders correctly and follows the head animation.
	[Export] public NodePath ViewmodelCameraAnchorPath;
	// All meshes under this node (the arms armature) are forced onto the viewmodel render layer at startup,
	// so the world cameras (cull_mask = layer 1) never see them and the gun doesn't self-reflect.
	[Export] public NodePath ViewmodelMeshRootPath;
	[Export(PropertyHint.Layers3DRender)] public uint ViewmodelRenderLayer = 2;
	[Export] public bool MouseLookEnabled = false;
	[Export(PropertyHint.Range, "10,89,1")] public float LookPitchLimitDeg = 85f;

	[ExportGroup("TPS")]
	[ExportSubgroup("Camera")]
	[Export] public NodePath TpsCameraPath;
	[Export] public NodePath TpsPivotPath;
	// Base pivot pitch (the look pitch is added on top while in TPS). Kept gentle so the camera looks roughly at
	// the character's upper back rather than steeply down at the ground.
	[Export(PropertyHint.Range, "-45,45,0.5")] public float TpsBasePitchDeg = -8f;
	// Camera rest offset behind the pivot (X = shoulder side, Y = height above the capsule-centre pivot, Z = arm
	// length back). Driven by code, so tune it here instead of moving the node — a raycast pulls it in on hits.
	[Export] public Vector3 TpsCameraOffset = new(0.4f, 0.0f, 1.5f);
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float TpsCamWallMargin = 0.2f;
	[Export(PropertyHint.Layers3DPhysics)] public uint TpsCamCollisionMask = 1;
	[Export(PropertyHint.Range, "1,40,0.5")] public float TpsCamSmoothRate = 14f;
	// Mouse-wheel zoom range for the arm length (Z), and the per-notch step.
	[Export(PropertyHint.Range, "0.3,3,0.1")] public float TpsZoomMin = 0.8f;
	[Export(PropertyHint.Range, "1,8,0.1")] public float TpsZoomMax = 4.0f;
	[Export(PropertyHint.Range, "0.05,1,0.05")] public float TpsZoomStep = 0.3f;

	[ExportSubgroup("Body")]
	// Third-person body mesh (glow shader). Shown only in TPS mode (Local or Remote, never Server / FPS).
	[Export] public NodePath GlowVisualPath;
	// The body smoothly yaws to face the look direction; the offset corrects the mesh's import facing.
	[Export(PropertyHint.Range, "-180,180,1")] public float TpsBodyYawOffsetDeg = 0f;
	[Export(PropertyHint.Range, "1,30,0.5")] public float TpsBodyTurnRate = 10f;
	// Aim pitch: the upper-body spine bone leans up/down toward the look (additive over the anim, Remote only).
	[Export] public string TpsAimBoneName = "spine_03";
	[Export(PropertyHint.Range, "0,1,0.05")] public float TpsAimPitchScale = 0.6f;
	// Procedural recoil kick on the spine when firing (additive, any mode). Tune sign/strength to taste.
	[Export(PropertyHint.Range, "-2,2,0.05")] public float TpsRecoilPitchScale = 0.5f;

	[ExportGroup("Body")]
	[Export] public NodePath BodyNodePath;
	[Export(PropertyHint.Range, "0,1,0.01")] public float CrouchCameraDrop = 0.32f;
	[ExportSubgroup("Collision")]
	[Export] public NodePath BodyCollisionPath;
	[Export(PropertyHint.Range, "0.5,2.5,0.05")] public float StandHeight = 1.8f;
	[Export(PropertyHint.Range, "0.5,2.0,0.05")] public float CrouchHeight = 1.2f;
	[Export(PropertyHint.Range, "0.1,1.0,0.01")] public float CapsuleRadius = 0.4f;

	[ExportGroup("Animations")]
	[ExportSubgroup("FPS")]
	[Export] public NodePath CharacterAnimationPath;
	[Export] public NodePath FpsTreePath = new("AnimationTree");
	// AnimationTree: locomotion blend-space layer + a OneShot slot so montages (fire/reload/inspect) blend OVER
	// locomotion and back, instead of hard-interrupting it. Disable only to debug with the tree off (no anim).
	[Export] public bool UseAnimationTree = true;
	// How fast the simulated velocity catches up to input — lower = smoother/floatier locomotion blending.
	[Export(PropertyHint.Range, "1,20,0.5")] public float LocomotionSmoothing = 3f;
	[Export(PropertyHint.Range, "1,15,0.5")] public float SpeedBlendRate = 5f;
	// Inspector button: assigns the clips to the FPS AnimationTree. Press, then Ctrl+S to persist.
	[Export] public bool RebuildAnimationTree { get => false; set { if (value) EditorRebuildTree(); } }

	[ExportSubgroup("FPS/Locomotion")]
	[Export] public string IdleStanding = "locomotion/A_TFA_FP_AR_Idle_Loop_Standing";
	[Export] public string IdleCrouched = "locomotion/A_TFA_FP_AR_Idle_Loop_Crouched";
	[Export] public string IdleAimed = "locomotion/A_TFA_FP_AR_Idle_Loop_Aimed";
	[Export] public string WalkForward = "locomotion/A_TFA_FP_AR_Walk_F_Loop_Standing";
	[Export] public string WalkBackward = "locomotion/A_TFA_FP_AR_Walk_B_Loop_Standing";
	[Export] public string WalkStrafeLeft = "locomotion/A_TFA_FP_AR_Walk_Strafe_L_Loop_Standing";
	[Export] public string WalkStrafeRight = "locomotion/A_TFA_FP_AR_Walk_Strafe_R_Loop_Standing";
	[Export] public string WalkForwardAimed = "locomotion/A_TFA_FP_AR_Walk_F_Loop_Aimed";
	[Export] public string WalkBackwardAimed = "locomotion/A_TFA_FP_AR_Walk_B_Loop_Aimed";
	[Export] public string WalkStrafeLeftAimed = "locomotion/A_TFA_FP_AR_Walk_Strafe_L_Loop_Aimed";
	[Export] public string WalkStrafeRightAimed = "locomotion/A_TFA_FP_AR_Walk_Strafe_R_Loop_Aimed";
	[Export] public string RunForward = "locomotion/A_TFA_FP_AR_Run_F_Loop";
	[Export] public string SprintForward = "locomotion/A_TFA_FP_AR_Sprint_F_Loop";
	[Export] public string JumpStart = "locomotion/A_TFA_FP_AR_Jump_Start";
	[Export] public string JumpFallingLoop = "locomotion/A_TFA_FP_AR_Jump_Falling_Loop";
	[Export] public string JumpEnd = "locomotion/A_TFA_FP_AR_Jump_End";
	[Export] public string JumpFull = "locomotion/A_TFA_FP_AR_Jump_Full";

	[ExportSubgroup("FPS/Combat")]
	[Export] public string FireSemi = "combat/A_TFA_FP_AR_Fire_Semi";
	[Export] public string FireAuto = "combat/A_TFA_FP_AR_Fire_Auto";
	[Export] public string FireAimed = "combat/A_TFA_FP_AR_Fire_Aimed";
	[Export] public string FireEmpty = "combat/A_TFA_FP_AR_Fire_Empty";
	[Export] public string Reload = "combat/A_TFA_FP_AR_Reload";
	[Export] public string ReloadEmpty = "combat/A_TFA_FP_AR_Reload_Empty";
	[Export] public string ReloadAimed = "combat/A_TFA_FP_AR_Reload_Aimed";
	[Export] public string ReloadEmptyAimed = "combat/A_TFA_FP_AR_Reload_Empty_Aimed";
	[Export] public string ReloadQuick = "combat/A_TFA_FP_AR_Reload_Quick";
	[Export] public string ReloadQuickAimed = "combat/A_TFA_FP_AR_Reload_Quick_Aimed";
	[Export] public string MagCheck = "combat/A_TFA_FP_AR_MagCheck";
	[Export] public string MagCheckAimed = "combat/A_TFA_FP_AR_MagCheck_Aimed";
	[Export] public string FireModeSwitch = "combat/A_TFA_FP_AR_FireModeSwitch";
	[Export] public string MeleeBashForward = "combat/A_TFA_FP_AR_Melee_Bash_F";
	[Export] public string MeleeSwingLeft = "combat/A_TFA_FP_AR_Melee_Swing_L";
	[Export] public string MeleeSwingRight = "combat/A_TFA_FP_AR_Melee_Swing_R";
	[Export] public string ClearJamMagSwipe = "combat/A_TFA_FP_AR_ClearJam_MagSwipe";
	[Export] public string ClearJamRack = "combat/A_TFA_FP_AR_ClearJam_Rack";
	[Export] public string GrenadeThrowQuick = "combat/A_TFA_FP_AR_Grenade_Throw_Quick";

	[ExportSubgroup("FPS/Interactions")]
	[Export] public string Inspect = "interactions/A_TFA_FP_AR_Inspect";
	[Export] public string InspectEmpty = "interactions/A_TFA_FP_AR_Inspect_Empty";
	[Export] public string HealSyringe = "interactions/A_TFA_FP_AR_Heal_Syringe";
	[Export] public string GripChange = "interactions/A_TFA_FP_AR_GripChange";
	[Export] public string InteractGrab = "interactions/A_TFA_FP_AR_Interact_Grab";
	[Export] public string InteractPush = "interactions/A_TFA_FP_AR_Interact_Push";
	[Export] public string InteractPunch = "interactions/A_TFA_FP_AR_Interact_Punch";

	[ExportSubgroup("FPS/Transitions")]
	[Export] public string Equip = "transitions/A_TFA_FP_AR_Equip";
	[Export] public string EquipQuick = "transitions/A_TFA_FP_AR_Equip_Quick";
	[Export] public string Holster = "transitions/A_TFA_FP_AR_Holster";
	[Export] public string TransitionCrouchStart = "transitions/A_TFA_FP_AR_Transition_Crouch_Start";
	[Export] public string TransitionCrouchEnd = "transitions/A_TFA_FP_AR_Transition_Crouch_End";
	[Export] public string TransitionCrouchStartAimed = "transitions/A_TFA_FP_AR_Transition_Crouch_Start_Aimed";
	[Export] public string TransitionCrouchEndAimed = "transitions/A_TFA_FP_AR_Transition_Crouch_End_Aimed";
	[Export] public string WalkEnd = "transitions/A_TFA_FP_AR_Transition_Walk_End";
	[Export] public string RunEnd = "transitions/A_TFA_FP_AR_Transition_Run_End";
	[Export] public string TriggerDisciplineReady = "transitions/A_TFA_FP_AR_TriggerDiscipline_Ready";

	[ExportSubgroup("FPS/Poses")]
	[Export] public string IdlePoseStanding = "poses/A_TFA_FP_AR_Idle_Pose_Standing";
	[Export] public string AimPose = "poses/A_TFA_FP_AR_Aim_Pose";
	[Export] public string AimPoseGripAngled = "poses/A_TFA_FP_AR_Aim_Pose_Grip_Angled";
	[Export] public string ActionRefAim = "poses/A_TFA_FP_AR_Aim_Pose";
	[Export] public string ActionRefIdle = "poses/A_TFA_FP_AR_Idle_Pose_Standing";
	[Export] public string AimPoseGripVertical = "poses/A_TFA_FP_AR_Aim_Pose_Grip_Vertical";
	[Export] public string IdlePoseGripAngled = "poses/A_TFA_FP_AR_Idle_Pose_Grip_Angled";
	[Export] public string IdlePoseGripVertical = "poses/A_TFA_FP_AR_Idle_Pose_Grip_Vertical";
	[Export] public string TriggerDisciplineSafePose = "poses/A_TFA_FP_AR_TriggerDiscipline_Safe_Pose";

	[ExportSubgroup("TPS")]
	// Third-person AnimationPlayer + tree on the GlowVisual body. Empty until the TPS anim tree is built.
	[Export] public NodePath TpsAnimationPath;
	[Export] public NodePath TpsTreePath;
	// Inspector button: assigns the TPS clips to the TPS AnimationTree (once it exists). Press, then Ctrl+S.
	[Export] public bool RebuildTpsAnimationTree { get => false; set { if (value) EditorRebuildTpsTree(); } }

	// TPS clip keys live in the third-person AnimationPlayer (TpsAnimationPath); the UE export suffix is kept.
	[ExportSubgroup("TPS/Locomotion")]
	[Export] public string TpsIdleLoop = "locomotion/A_TFA_TP_AR_Idle_Loop_Unreal Take";

	[ExportSubgroup("TPS/Combat")]
	[Export] public string TpsFire = "combat/A_TFA_TP_AR_Fire_Unreal Take";
	[Export] public string TpsFireEmpty = "combat/A_TFA_TP_AR_Fire_Empty_Unreal Take";
	[Export] public string TpsReload = "combat/A_TFA_TP_AR_Reload_Unreal Take";
	[Export] public string TpsReloadEmpty = "combat/A_TFA_TP_AR_Reload_Empty_Unreal Take";
	[Export] public string TpsReloadAimed = "combat/A_TFA_TP_AR_Reload_Aimed_Unreal Take";
	[Export] public string TpsReloadEmptyAimed = "combat/A_TFA_TP_AR_Reload_Empty_Aimed_Unreal Take";
	[Export] public string TpsReloadQuick = "combat/A_TFA_TP_AR_Reload_Quick_Unreal Take";
	[Export] public string TpsReloadQuickAimed = "combat/A_TFA_TP_AR_Reload_Quick_Aimed_Unreal Take";
	[Export] public string TpsMagCheck = "combat/A_TFA_TP_AR_MagCheck_Unreal Take";
	[Export] public string TpsMagCheckAimed = "combat/A_TFA_TP_AR_MagCheck_Aimed_Unreal Take";
	[Export] public string TpsFireModeSwitch = "combat/A_TFA_TP_AR_FireModeSwitch_Unreal Take";
	[Export] public string TpsMeleeBashForward = "combat/A_TFA_TP_AR_Melee_Bash_F_Unreal Take";
	[Export] public string TpsMeleeSwingLeft = "combat/A_TFA_TP_AR_Melee_Swing_L_Unreal Take";
	[Export] public string TpsMeleeSwingRight = "combat/A_TFA_TP_AR_Melee_Swing_R_Unreal Take";
	[Export] public string TpsClearJamMagSwipe = "combat/A_TFA_TP_AR_ClearJam_MagSwipe_Unreal Take";
	[Export] public string TpsClearJamRack = "combat/A_TFA_TP_AR_ClearJam_Rack_Unreal Take";
	[Export] public string TpsGrenadeThrowQuick = "combat/A_TFA_TP_AR_Grenade_Throw_Quick_Unreal Take";

	[ExportSubgroup("TPS/Interactions")]
	[Export] public string TpsInspect = "interactions/A_TFA_TP_AR_Inspect_Unreal Take";
	[Export] public string TpsInspectEmpty = "interactions/A_TFA_TP_AR_Inspect_Empty_Unreal Take";
	[Export] public string TpsHealSyringe = "interactions/A_TFA_TP_AR_Heal_Syringe_Unreal Take";
	[Export] public string TpsInteractGrab = "interactions/A_TFA_TP_AR_Interact_Grab_Unreal Take";
	[Export] public string TpsInteractPush = "interactions/A_TFA_TP_AR_Interact_Push_Unreal Take";
	[Export] public string TpsInteractPunch = "interactions/A_TFA_TP_AR_Interact_Punch_Unreal Take";

	[ExportSubgroup("TPS/Transitions")]
	[Export] public string TpsEquip = "transitions/A_TFA_TP_AR_Equip_Unreal Take";
	[Export] public string TpsEquipQuick = "transitions/A_TFA_TP_AR_Equip_Quick_Unreal Take";
	[Export] public string TpsHolster = "transitions/A_TFA_TP_AR_Holster_Unreal Take";
	[Export] public string TpsTransitionAimStart = "transitions/A_TFA_TP_AR_Transition_Aim_Start_Unreal Take";
	[Export] public string TpsTransitionAimEnd = "transitions/A_TFA_TP_AR_Transition_Aim_End_Unreal Take";

	[ExportSubgroup("TPS/Poses")]
	[Export] public string TpsAimPose = "poses/A_TFA_TP_AR_Aim_Pose_Unreal Take";
	[Export] public string TpsAimPoseCanted = "poses/A_TFA_TP_AR_Aim_Pose_Canted_Unreal Take";
	[Export] public string TpsAimPoseGripAngled = "poses/A_TFA_TP_AR_Aim_Pose_Grip_Angled_Unreal Take";
	[Export] public string TpsAimPoseGripVertical = "poses/A_TFA_TP_AR_Aim_Pose_Grip_Vertical_Unreal Take";
	[Export] public string TpsIdlePose = "poses/A_TFA_TP_AR_Idle_Pose_Unreal Take";
	[Export] public string TpsIdlePoseGripAngled = "poses/A_TFA_TP_AR_Idle_Pose_Grip_Angled_Unreal Take";
	[Export] public string TpsIdlePoseGripVertical = "poses/A_TFA_TP_AR_Idle_Pose_Grip_Vertical_Unreal Take";

	[ExportGroup("ADS")]
	[Export(PropertyHint.Range, "30,120,0.5")] public float HipFov = 100f;
	[Export(PropertyHint.Range, "30,120,0.5")] public float AimFov = 78f;
	[Export(PropertyHint.Range, "1,30,0.1")] public float AimBlendSpeed = 12f;
	[Export(PropertyHint.Range, "1,60,0.5")] public float GripPoseBlendSpeed = 15f;
	[Export(PropertyHint.Range, "0.05,0.5,0.005")] public float GripAimBlendTime = 0.18f;
	[Export(PropertyHint.Range, "0.05,0.5,0.005")] public float GripChangeNotifyTime = 0.133f;
	// Fine step (0.0001 m / 0.01°) + or_less,or_greater so the inspector keeps 4 decimals and doesn't round/clamp.
	[Export(PropertyHint.Range, "-1,1,0.0001,or_less,or_greater")] public Vector3 AdsOffsetPosition = new(-0.02f, 0.06f, 0.0205f);
	[Export(PropertyHint.Range, "-180,180,0.01,or_less,or_greater")] public Vector3 AdsOffsetRotation = new(0f, -8.4f, 0f);
	[Export(PropertyHint.Range, "-1,1,0.0001,or_less,or_greater")] public Vector3 CrouchOffsetPosition = new(0.015f, 0.02f, -0.015f);
	[Export(PropertyHint.Range, "-180,180,0.01,or_less,or_greater")] public Vector3 CrouchOffsetRotation = new(0f, 4.3f, 0f);
	[Export(PropertyHint.Range, "-1,1,0.0001,or_less,or_greater")] public Vector3 CantedOffsetPosition = new(-0.05f, -0.015f, -0.01f);
	[Export(PropertyHint.Range, "-180,180,0.01,or_less,or_greater")] public Vector3 CantedOffsetRotation = new(0f, 35.0f, 0f);

	[ExportSubgroup("Test (Editor)")]
	// Editor-only live tuning: forces the full ADS pose (AdsOffsetPosition/Rotation applied) + AimFov, and
	// draws an H/V crosshair at the sight distance in the viewmodel viewport to centre the iron sights.
	// Tune AdsOffsetPosition/Rotation until the sights sit on the crosshair, then turn this OFF and save.
	[Export] public bool AdsTestMode { get => _adsTestMode; set { _adsTestMode = value; if (Engine.IsEditorHint()) ApplyEditorPreview(); } }
	[Export(PropertyHint.Range, "0.1,5,0.05")] public float AdsCalibrationDistance = 1.0f;
	[Export(PropertyHint.Range, "0.001,0.05,0.0005")] public float AdsCalibrationSize = 0.004f;
	[Export] public Color AdsCalibrationColor = new(1f, 0f, 0f, 1f);

	[ExportGroup("Sway")]
	[Export(PropertyHint.Range, "0,1,0.001")] public float SwayLookFactor = 0.04f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float SwayMaxDegrees = 6f;
	[Export(PropertyHint.Range, "1,40,0.5")] public float SwaySpringSpeed = 12f;
	// Sway scale while aiming (ADS): 1.0 = full sway, 0.5 = half, 0.3 = calmer ADS sway. Lerped by aim blend.
	[Export(PropertyHint.Range, "0,1,0.05")] public float AimSwayMultiplier = 0.3f;

	// Additive first-person view-sway layers ported from LocalAnimation. They offset the head_pitch node
	// (camera pivot) on top of crouch-drop + look-yaw, driven by move input / look yaw / mouse delta. Each
	// layer has an on/off toggle; the master ViewSwayEnabled gates all of them.
	// COD-style preset: ADS very stable, a pronounced-but-smooth weapon swing on look (mouse inertia is the
	// signature feel), and restrained movement tilt. All tunable live; toggles per layer + master gate.
	[ExportGroup("View Sway")]
	[Export] public bool ViewSwayEnabled = true;
	[Export(PropertyHint.Range, "1,40,0.5")] public float LeanReferenceSpeed = 5.0f;
	// ADS multiplier for the MOVEMENT layers (direction lean + velocity tilt): kept low so strafing does not
	// drag the sights off target. (1 = full during ADS.)
	[Export(PropertyHint.Range, "0,1,0.05")] public float ViewSwayAdsMul = 0.15f;
	// ADS multiplier for the LOOK layers (look-lag + mouse inertia): kept higher so the weapon still drifts on
	// turn / look up-down while aiming, like COD ADS sway. (1 = full during ADS.)
	[Export(PropertyHint.Range, "0,1,0.05")] public float ViewSwayAdsLookMul = 0.5f;
	// How much of the sway the world camera (head_pitch) gets; the weapon overlay always gets the full amount.
	// COD-style: keep the world calm and let the weapon do the swaying. 0 = world dead-still, weapon-only sway.
	[Export(PropertyHint.Range, "0,1,0.05")] public float ViewSwayWorldMul = 0.35f;

	[ExportSubgroup("Direction Lean")]
	[Export] public bool DirectionLeanEnabled = true;
	[Export(PropertyHint.Range, "10,400,1")] public float DirectionLeanStiffness = 90f;
	[Export(PropertyHint.Range, "1,60,0.5")] public float DirectionLeanDamping = 14f;
	[Export(PropertyHint.Range, "0,0.1,0.0005")] public float StrafeLeanPos = 0.011f;
	[Export(PropertyHint.Range, "0,10,0.1")] public float StrafeLeanRoll = 1.4f;
	[Export(PropertyHint.Range, "0,5,0.05")] public float ForwardLeanPitch = 0.35f;
	[Export(PropertyHint.Range, "0,0.05,0.0005")] public float ForwardLeanPosDown = 0.004f;
	[Export(PropertyHint.Range, "0,0.05,0.0005")] public float ForwardLeanPosForward = 0.009f;

	[ExportSubgroup("Velocity Tilt")]
	[Export] public bool VelocityTiltEnabled = true;
	[Export(PropertyHint.Range, "0,1,0.005")] public float InertiaTiltStrength = 0.05f;
	[Export(PropertyHint.Range, "0,10,0.1")] public float InertiaTiltMax = 1.2f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float InertiaTiltRecovery = 7.0f;

	// Look-lag: the weapon trails fast look rotation, on BOTH axes (yaw on turn, pitch on look up/down).
	[ExportSubgroup("Body Yaw Lag")]
	[Export] public bool BodyYawLagEnabled = true;
	[Export(PropertyHint.Range, "0,0.1,0.0005")] public float BodyYawLagStrength = 0.01f;
	[Export(PropertyHint.Range, "0,30,0.5")] public float BodyYawLagMax = 2.5f;
	[Export(PropertyHint.Range, "1,40,0.5")] public float BodyYawLagSmoothing = 10.0f;

	[ExportSubgroup("Mouse Inertia")]
	[Export] public bool MouseInertiaEnabled = true;
	[Export(PropertyHint.Range, "0,0.1,0.0005")] public float MouseInertiaYaw = 0.01f;
	[Export(PropertyHint.Range, "0,0.1,0.0005")] public float MouseInertiaPitch = 0.012f;
	[Export(PropertyHint.Range, "0,5,0.05")] public float MouseInertiaMaxYaw = 0.7f;
	[Export(PropertyHint.Range, "0,5,0.05")] public float MouseInertiaMaxPitch = 0.7f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float MouseInertiaRecovery = 7.0f;
	[Export(PropertyHint.Range, "1,40,0.5")] public float MouseInertiaSmoothingIn = 10.0f;
	[Export(PropertyHint.Range, "1,40,0.5")] public float MouseInertiaSmoothingOut = 7.0f;
	[Export(PropertyHint.Range, "0,2,0.05")] public float MouseInertiaRollMul = 0.18f;

	[ExportGroup("Recoil")]
	[Export] public Vector3 RecoilImpulseHipfire = new(-1.2f, 0.4f, 0f);
	[Export] public Vector3 RecoilImpulseAimed = new(-0.6f, 0.2f, 0f);
	// Infima recoil = a damped spring (VectorSpringInterp): a kick displaces it, the spring pulls it back
	// to rest with a little overshoot. Damping 1 = critical (no overshoot), <1 = springier.
	[Export(PropertyHint.Range, "10,600,5")] public float RecoilStiffness = 200f;
	[Export(PropertyHint.Range, "0.1,1.5,0.05")] public float RecoilDamping = 0.6f;
	[Export(PropertyHint.Range, "0.2,4,0.1")] public float RecoilMass = 1f;
	[Export(PropertyHint.Range, "1,45,0.5")] public float RecoilMaxDegrees = 10f;
	// Recoil view-kick scale while aiming (ADS): 1.0 = full, 0.5 = half. Lerped by aim blend.
	[Export(PropertyHint.Range, "0,1,0.05")] public float AimRecoilMultiplier = 0.5f;
	// Procedural weapon-bone recoil on top of the camera kick: the gun rotates + kicks back while firing (UE FP
	// additive recoil). Rotation scales the recoil spring (deg), kickback pushes the gun toward the player (m/deg).
	[Export(PropertyHint.Range, "0,1,0.02")] public float WeaponRecoilRotScale = 0.35f;
	[Export(PropertyHint.Range, "0,0.05,0.001")] public float WeaponRecoilKickback = 0.012f;

	[ExportGroup("IK")]
	[ExportSubgroup("Left Hand (Foregrip)")]
	[Export] public bool IkEnabled = false;
	[Export] public NodePath LeftHandFabrikPath;

	[ExportSubgroup("Right Hand (Pistol Grip)")]
	[Export] public NodePath RightHandFabrikPath;

	[ExportGroup("Weapon")]
	[Export] public NodePath CurrentWeaponPath;
	[Export] public NodePath TpsWeaponPath;

	[ExportSubgroup("Firing")]
	[Export] public bool CanFire = true;
	[Export(PropertyHint.Range, "1,1000,1")] public float HitscanRange = 200f;
	[Export(PropertyHint.Layers3DPhysics)] public uint HitscanMask = 1;
	[Export(PropertyHint.Range, "2,40,0.5")] public float GrenadeThrowSpeed = 14f;

	[ExportGroup("Footsteps")]
	[Export] public NodePath FootstepAudioPath;

	[ExportGroup("State")]
	[ExportSubgroup("Movement")]
	[Export] public bool IsRunning { get => _runAmt > 0.5f; set => _runAmt = value ? 1f : 0f; }
	[Export] public bool IsSprinting { get => _sprintAmt > 0.5f; set { _sprintAmt = value ? 1f : 0f; if (value) _runAmt = 1f; } }
	[Export] public bool IsCrouching { get => _isCrouched; set => _isCrouched = value; }
	[Export] public Vector2 SimulatedVelocity { get => _simVel; set => _simVel = value; }
	[ExportSubgroup("Actions")]
	[Export] public bool IsAiming { get => _isAiming; set => _isAiming = value; }
	[Export] public bool IsCantedAiming { get => _cantedAim; set => _cantedAim = value; }
	[Export] public GripType CurrentGrip { get => _grip; set => _grip = value; }

	// Local = this client's own player, Remote = another player's puppet, Server = headless agent. Nested so it
	// doesn't collide with the global match-type GameMode (Competitive/Deathmatch).
	public enum GameMode { Local, Remote, Server }
	public enum LocomotionState { Idle, Walk, Run, Sprint, Jump, Falling, Land }
	public enum FireMode { Semi, Auto }
	public enum GripType { Standard, Angled, Vertical }
	public enum InteractKind { Grab, Push, Punch }
	public enum MeleeDirection { Bash, Left, Right }
	public enum SpeedMode { Walk, Run, Sprint }

	private AnimationPlayer _player;
	private LocomotionState _locomotion = LocomotionState.Idle;
	private SpeedMode _speed = SpeedMode.Walk;
	private bool _isAiming;
	private bool _isCrouched;
	private Vector2 _moveInput;
	private Vector2 _lookDelta;
	private float _lookYaw;
	private float _lookPitch;
	private float _aimBlend;
	private float _crouchBlend;
	private bool _cantedAim;
	private float _cantedBlend;
	private Vector3 _swayCurrent;
	private Vector3 _recoilCurrent;
	private Vector3 _recoilVel;
	private Camera3D _cam;
	private Camera3D _viewmodelCam;
	private Camera3D _tpsCam;
	private Node3D _tpsPivot;
	private float _tpsZoomDist = -1f;
	private PhysicsRayQueryParameters3D _tpsRayQuery;
	private readonly PhysicsRayQueryResult3D _tpsRayResult = new();
	private Godot.Collections.Array<Rid> _tpsSelfExclude;
	private Node3D _viewmodelCamAnchor;
	private CanvasLayer _viewmodelLayer;
	private Node3D _glowVisual;
	private AnimationPlayer _tpsPlayer;
	private AnimationTree _tpsTree;
	private AnimationNodeAnimation _tpsActionAnim;
	private AnimationNodeAnimation _tpsAimPoseNode;
	private TpsAimModifier _tpsAimModifier;
	private Transform3D _camRestLocal;
	private Transform3D _eyeRest;
	private bool _camRigCaptured;
	private bool _adsTestMode;
	private bool _adsTestPrev;
	private MeshInstance3D _adsMarker, _adsLineH, _adsLineV;

	private WeaponAnimation _currentWeapon;
	private WeaponAnimation _fpsWeapon;
	private WeaponAnimation _tpsWeapon;
	private float _magFill = 1f;
	private string _fireModeName = "Semi";
	private GrenadeAimGuide _aimGuide;
	private readonly List<Vector3> _aimPath = new();
	private bool _grenadeAiming;
	private readonly FootstepController _footstepLogic = new();
	private FootstepAudio _footstepAudio;
	private AnimationTree _tree;
	private AnimationNodeAnimation _actionAnim;
	private AnimationNodeAnimation _actionRefNode;
	private AnimationNodeAnimation _actionRef2Node;
	private AnimationNodeAnimation _tpsActionRefNode;
	private AnimationNodeAnimation _tpsActionRef2Node;
	private AnimationNodeAnimation _gripPose;
	private AnimationNodeAnimation _gripPoseAim;
	private float _gripAimBlend;
	private AnimationNodeAnimation _gripChangeAnim;
	private bool _montageActive;
	private GripType _grip = GripType.Standard;
	private GripType _pendingGrip;
	private float _gripSwitchDelay = -1f;
	private bool _gripChangeActive;
	private float _gripBlend;
	private Vector2 _simVel;
	private float _runAmt, _sprintAmt;
	private bool _editorTreeReady;
	private Node3D _leftHandFabrik;
	private Node3D _rightHandFabrik;
	private Node3D _bodyNode;
	private Vector3 _bodyRest;
	private Vector3 _bodyRestRot;
	private bool _bodyRestCaptured;
	private bool _wasMoving;

	private Vector3 _smoothedDirRatio;
	private Vector3 _dirLeanSpringVel;
	private Vector3 _prevProcVelocity;
	private Vector3 _inertiaTilt;
	private float _prevBodyYaw;
	private float _prevBodyPitch;
	private bool _bodyYawInit;
	private float _bodyYawLag;
	private float _bodyPitchLag;
	private Vector3 _mouseInertia;
	private Vector3 _mouseInertiaSmoothed;
	private Vector3 _viewSwayPos;
	private Vector3 _viewSwayRotDeg;
	private AnimationNodeAnimation _locoStopAnim;
	private CollisionShape3D _bodyCollision;
	private CapsuleShape3D _capsule;
	private string _animEnumHint;
	private string _tpsAnimEnumHint;

	// ---- Setup -------------------------------------------------------------------------------------------

	private void ResolveWeaponPlayers()
	{
		bool server = CurrentGameMode == GameMode.Server;
		_fpsWeapon = server ? null : GetNodeOrNull<WeaponAnimation>(CurrentWeaponPath);
		_tpsWeapon = server ? null : GetNodeOrNull<WeaponAnimation>(TpsWeaponPath);
		if (_fpsWeapon != null) { _fpsWeapon.Mode = WeaponMode.FPS; _fpsWeapon.OwnerBody = this; }
		if (_tpsWeapon != null) { _tpsWeapon.Mode = WeaponMode.TPS; _tpsWeapon.OwnerBody = this; }
		_leftHandFabrik = GetNodeOrNull<Node3D>(LeftHandFabrikPath);
		_rightHandFabrik = GetNodeOrNull<Node3D>(RightHandFabrikPath);
		_bodyNode = GetNodeOrNull<Node3D>(BodyNodePath);
		if (_bodyNode != null && !_bodyRestCaptured)
		{ _bodyRest = _bodyNode.Position; _bodyRestRot = _bodyNode.Rotation; _bodyRestCaptured = true; }
		_cam = GetNodeOrNull<Camera3D>(HeadCameraPath);
		_viewmodelCam = GetNodeOrNull<Camera3D>(ViewmodelCameraPath);
		_viewmodelCamAnchor = GetNodeOrNull<Node3D>(ViewmodelCameraAnchorPath);
		_viewmodelLayer = GetNodeOrNull<CanvasLayer>(ViewmodelLayerPath);
		_tpsCam = GetNodeOrNull<Camera3D>(TpsCameraPath);
		_tpsPivot = GetNodeOrNull<Node3D>(TpsPivotPath);
		_glowVisual = GetNodeOrNull<Node3D>(GlowVisualPath);
		_tpsPlayer = GetNodeOrNull<AnimationPlayer>(TpsAnimationPath);
		_footstepAudio = GetNodeOrNull<FootstepAudio>(FootstepAudioPath);
		if (_footstepAudio != null)
			_footstepAudio.IsLocalPlayer = CurrentGameMode == GameMode.Local;
		UpdateActiveWeapon();
	}

	private bool TpsView => CurrentGameMode != GameMode.Server && CurrentViewMode == ViewMode.Tps && _tpsCam != null;

	// Picks the weapon instance driven by the current view: the FPS arms weapon in FPS, the third-person body
	// weapon in TPS (Server drives neither). On a switch the newly-active instance inherits the live state —
	// aiming, fire mode, magazine fill — so its selector + follower stay correct across the view change.
	private void UpdateActiveWeapon()
	{
		WeaponAnimation target = CurrentGameMode == GameMode.Server ? null
			: TpsView && _tpsWeapon != null ? _tpsWeapon
			: _fpsWeapon;
		if (target == _currentWeapon)
			return;
		_currentWeapon = target;
		if (_currentWeapon == null)
			return;
		_currentWeapon.Aiming = _isAiming;
		_currentWeapon.OwnerBody = this;
		_currentWeapon.SetFireMode(_fireModeName);
		_currentWeapon.SetMagazineFill(_magFill);
	}

	// Third-person camera rig (UE CameraPivotPoint + SpringArm). The pivot lives at the capsule centre under the
	// root (NOT the head), and takes the look yaw + base/look pitch here. The camera then sits at TpsCameraOffset
	// behind it, pulled in toward the pivot by a raycast when a wall or the ground is closer than the arm length.
	private void UpdateTpsCamera(float dt)
	{
		if (_tpsPivot == null || _tpsCam == null)
			return;
		_tpsPivot.Rotation = new Vector3(
			Mathf.DegToRad(TpsBasePitchDeg) + (MouseLookEnabled ? _lookPitch : 0f),
			MouseLookEnabled ? _lookYaw : _tpsPivot.Rotation.Y,
			0f);

		if (_tpsZoomDist < 0f)
			_tpsZoomDist = TpsCameraOffset.Z;
		Vector3 offset = new(TpsCameraOffset.X, TpsCameraOffset.Y, _tpsZoomDist);
		Vector3 pivot = _tpsPivot.GlobalPosition;
		Vector3 desiredWorld = _tpsPivot.GlobalTransform * offset;
		Vector3 targetLocal = offset;
		var space = GetWorld3D()?.DirectSpaceState;
		if (space != null)
		{
			_tpsRayQuery ??= new PhysicsRayQueryParameters3D { CollideWithAreas = false, CollideWithBodies = true };
			_tpsSelfExclude ??= new Godot.Collections.Array<Rid> { GetRid() };
			_tpsRayQuery.From = pivot;
			_tpsRayQuery.To = desiredWorld;
			_tpsRayQuery.CollisionMask = TpsCamCollisionMask;
			_tpsRayQuery.Exclude = _tpsSelfExclude;
			if (space.IntersectRayInto(_tpsRayQuery, _tpsRayResult))
			{
				Vector3 dir = desiredWorld - pivot;
				float desiredDist = dir.Length();
				if (desiredDist > 0.001f)
				{
					float hitDist = (_tpsRayResult.GetPosition() - pivot).Length();
					float safeDist = Mathf.Max(0.2f, hitDist - TpsCamWallMargin);
					targetLocal = _tpsPivot.GlobalTransform.AffineInverse() * (pivot + dir / desiredDist * safeDist);
				}
			}
		}
		float t = TpsCamSmoothRate > 0f ? 1f - Mathf.Exp(-TpsCamSmoothRate * dt) : 1f;
		_tpsCam.Position = _tpsCam.Position.Lerp(targetLocal, t);
	}

	// Third-person body facing. Only Remote puppets yaw to their (replicated) look direction here; the local
	// player's body keeps its own facing while the camera orbits instead. Animation comes from the TPS tree.
	private void UpdateTpsBody(float dt)
	{
		bool remote = CurrentGameMode == GameMode.Remote;
		UpdateTpsAimPose();
		if (_tpsAimModifier != null)
			_tpsAimModifier.Pitch = (remote ? _lookPitch : 0f) + Mathf.DegToRad(_recoilCurrent.X) * TpsRecoilPitchScale;
		if (!remote || _glowVisual == null)
			return;
		float targetYaw = _lookYaw + Mathf.DegToRad(TpsBodyYawOffsetDeg);
		Vector3 r = _glowVisual.Rotation;
		r.Y = Mathf.LerpAngle(r.Y, targetYaw, Mathf.Clamp(TpsBodyTurnRate * dt, 0f, 1f));
		_glowVisual.Rotation = r;
	}

	// Selects the additive aim-pose variant for the body by canted / grip state (canted -> vertical -> angled ->
	// standard). The additive AimAdd layer in the TPS tree then shifts the body toward whichever stance is active.
	private void UpdateTpsAimPose()
	{
		if (_tpsAimPoseNode == null || _tpsPlayer == null)
			return;
		string pose = _cantedAim ? TpsAimPoseCanted
			: _grip == GripType.Vertical ? TpsAimPoseGripVertical
			: _grip == GripType.Angled ? TpsAimPoseGripAngled
			: TpsAimPose;
		if (!string.IsNullOrEmpty(pose) && _tpsAimPoseNode.Animation != pose && _tpsPlayer.HasAnimation(pose))
			_tpsAimPoseNode.Animation = pose;
	}

	// Camera + body visibility per mode: Server kills all cameras; Local/Remote pick the FPS or TPS camera by
	// ViewMode and show the third-person GlowVisual only in TPS. Falls back to the FPS camera if no TPS cam.
	private void ApplyModeVisibility()
	{
		bool server = CurrentGameMode == GameMode.Server;
		bool tps = TpsView;
		bool fps = !server && !tps;
		if (_cam != null) _cam.Current = fps;
		if (_tpsCam != null) _tpsCam.Current = tps;
		if (_viewmodelCam != null) _viewmodelCam.Current = !server;
		if (_viewmodelLayer != null) _viewmodelLayer.Visible = fps;
		if (_glowVisual != null) _glowVisual.Visible = tps;
		UpdateActiveWeapon();
	}

	// Builds a per-instance capsule (duplicated from the scene shape) sized to StandHeight/CapsuleRadius, so the
	// live crouch resize never mutates a shared resource. No-op when the body shape isn't a CapsuleShape3D.
	private void SetupCapsule()
	{
		_bodyCollision = GetNodeOrNull<CollisionShape3D>(BodyCollisionPath);
		if (_bodyCollision?.Shape is not CapsuleShape3D cap)
			return;
		_capsule = (CapsuleShape3D)cap.Duplicate();
		_capsule.Height = StandHeight;
		_capsule.Radius = CapsuleRadius;
		_bodyCollision.Shape = _capsule;
		Vector3 pos = _bodyCollision.Position;
		pos.Y = StandHeight * 0.5f;
		_bodyCollision.Position = pos;
	}

	private void ApplyViewmodelLayer()
	{
		Node root = GetNodeOrNull<Node>(ViewmodelMeshRootPath);
		if (root != null) SetRenderLayersRecursive(root, ViewmodelRenderLayer);
	}

	private static void SetRenderLayersRecursive(Node n, uint layer)
	{
		if (n is VisualInstance3D vi) vi.Layers = layer;
		foreach (Node c in n.GetChildren())
			SetRenderLayersRecursive(c, layer);
	}

	// ---- AnimationTree build -----------------------------------------------------------------------------

	// 5-point 2D blend spaces (Idle centre + Walk F/B/Strafe L/R), axes -100..100, exactly like Infima's
	// BS_TFA_FP_AR_Locomotion_*. Run/Sprint are NOT samples here — they are separate loops (Standing only).
	private (string key, Vector2 pos)[] StandPoints() => new[] {
		(IdleStanding, new Vector2(0, 0)), (WalkForward, new Vector2(0, 100)), (WalkBackward, new Vector2(0, -100)),
		(WalkStrafeLeft, new Vector2(-100, 0)), (WalkStrafeRight, new Vector2(100, 0)),
	};
	private (string key, Vector2 pos)[] AimPoints() => new[] {
		(IdleAimed, new Vector2(0, 0)), (WalkForwardAimed, new Vector2(0, 100)), (WalkBackwardAimed, new Vector2(0, -100)),
		(WalkStrafeLeftAimed, new Vector2(-100, 0)), (WalkStrafeRightAimed, new Vector2(100, 0)),
	};
	private (string key, Vector2 pos)[] CrouchPoints() => new[] {
		(IdleCrouched, new Vector2(0, 0)), (WalkForward, new Vector2(0, 100)), (WalkBackward, new Vector2(0, -100)),
		(WalkStrafeLeft, new Vector2(-100, 0)), (WalkStrafeRight, new Vector2(100, 0)),
	};

	private static void PopulateBlendSpace(AnimationNodeBlendSpace2D bs, AnimationPlayer player, (string key, Vector2 pos)[] points)
	{
		for (int i = bs.GetBlendPointCount() - 1; i >= 0; i--)
			bs.RemoveBlendPoint(i);
		foreach (var (key, pos) in points)
			if (!string.IsNullOrEmpty(key) && player.HasAnimation(key))
				bs.AddBlendPoint(new AnimationNodeAnimation { Animation = key }, pos);
	}

	private void AssignTreeAnimations(AnimationNodeBlendTree blend, AnimationPlayer player)
	{
		if (blend.GetNode("StandWalk") is AnimationNodeBlendSpace2D s)
			PopulateBlendSpace(s, player, StandPoints());
		if (blend.GetNode("AimLoco") is AnimationNodeBlendSpace2D a)
			PopulateBlendSpace(a, player, AimPoints());
		if (blend.GetNode("CrouchLoco") is AnimationNodeBlendSpace2D c)
			PopulateBlendSpace(c, player, CrouchPoints());
		if (blend.GetNode("RunLoop") is AnimationNodeAnimation r)
			r.Animation = RunForward;
		if (blend.GetNode("SprintLoop") is AnimationNodeAnimation sp)
			sp.Animation = SprintForward;
	}

	private void UpdateGripLayer()
	{
		if (_player == null)
			return;
		string nonAim = _grip switch
		{
			GripType.Angled => IdlePoseGripAngled,
			GripType.Vertical => IdlePoseGripVertical,
			_ => null,
		};
		string aim = _grip switch
		{
			GripType.Angled => AimPoseGripAngled,
			GripType.Vertical => AimPoseGripVertical,
			_ => null,
		};
		if (_gripPose != null && nonAim != null && _player.HasAnimation(nonAim))
			_gripPose.Animation = nonAim;
		if (_gripPoseAim != null && aim != null && _player.HasAnimation(aim))
			_gripPoseAim.Animation = aim;
	}

	private void BuildAnimationTree()
	{
		if (!UseAnimationTree || _player == null)
		{
			var saved = GetNodeOrNull<AnimationTree>(FpsTreePath);
			if (saved != null)
				saved.Active = false;
			return;
		}

		_tree = GetNodeOrNull<AnimationTree>(FpsTreePath);
		var bt = _tree?.TreeRoot as AnimationNodeBlendTree;
		if (_tree == null || bt == null || !bt.HasNode("StandWalk"))
		{
			GD.PushWarning("[AnimatedCharacter] No AnimationTree with StandWalk found in scene.");
			return;
		}
		AssignTreeAnimations(bt, _player);
		_tree.AnimPlayer = _tree.GetPathTo(_player);
		_actionAnim = bt.GetNode("ActionAnim") as AnimationNodeAnimation;
		_actionRefNode = bt.HasNode("ActionRef") ? bt.GetNode("ActionRef") as AnimationNodeAnimation : null;
		_actionRef2Node = bt.HasNode("ActionRef2") ? bt.GetNode("ActionRef2") as AnimationNodeAnimation : null;
		if (_actionRefNode != null) _actionRefNode.Animation = ActionRefIdle;
		if (_actionRef2Node != null) _actionRef2Node.Animation = ActionRefIdle;
		_gripPose = bt.GetNode("GripPose") as AnimationNodeAnimation;
		_gripPoseAim = bt.GetNode("GripPoseAim") as AnimationNodeAnimation;
		_locoStopAnim = bt.GetNode("LocoStopAnim") as AnimationNodeAnimation;
		_gripChangeAnim = bt.GetNode("GripChangeAnim") as AnimationNodeAnimation;
		UpdateGripLayer();
		_tree.Active = true;
		_tree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		_tree.Set("parameters/ActionSub/sub_amount", 1f);
		_tree.Set("parameters/ActionAdd/add_amount", 1f);
	}

	// Wires the third-person AnimationTree (the TpsAnimationTree scene node + tps_animation_root_tree.tres): a
	// looping Idle with an Action OneShot layered over it so montages (fire/reload/…) blend in and out. Points it
	// at the TPS player and resolves tracks against the player's root so the body's skeleton is posed correctly.
	private void BuildTpsTree()
	{
		if (CurrentGameMode == GameMode.Server || _tpsPlayer == null)
			return;
		_tpsTree = GetNodeOrNull<AnimationTree>(TpsTreePath);
		var bt = _tpsTree?.TreeRoot as AnimationNodeBlendTree;
		if (_tpsTree == null || bt == null || !bt.HasNode("Action"))
		{
			GD.PushWarning("[AnimatedCharacter] No TpsAnimationTree with an Action node found in scene.");
			return;
		}
		_tpsTree.AnimPlayer = _tpsTree.GetPathTo(_tpsPlayer);
		Node playerRoot = _tpsPlayer.GetNodeOrNull(_tpsPlayer.RootNode);
		if (playerRoot != null)
			_tpsTree.RootNode = _tpsTree.GetPathTo(playerRoot);
		if (bt.GetNode("Idle") is AnimationNodeAnimation idle && !string.IsNullOrEmpty(TpsIdleLoop))
			idle.Animation = TpsIdleLoop;
		_tpsAimPoseNode = bt.GetNode("AimPose") as AnimationNodeAnimation;
		if (_tpsAimPoseNode != null && !string.IsNullOrEmpty(TpsAimPose))
			_tpsAimPoseNode.Animation = TpsAimPose;
		if (bt.GetNode("AimRef") is AnimationNodeAnimation aimRef && !string.IsNullOrEmpty(TpsIdleLoop))
			aimRef.Animation = TpsIdleLoop;
		_tpsActionAnim = bt.GetNode("ActionAnim") as AnimationNodeAnimation;
		_tpsActionRefNode = bt.HasNode("ActionRef") ? bt.GetNode("ActionRef") as AnimationNodeAnimation : null;
		_tpsActionRef2Node = bt.HasNode("ActionRef2") ? bt.GetNode("ActionRef2") as AnimationNodeAnimation : null;
		if (_tpsActionRefNode != null) _tpsActionRefNode.Animation = TpsIdlePose;
		if (_tpsActionRef2Node != null) _tpsActionRef2Node.Animation = TpsIdlePose;
		_tpsTree.Active = true;
		// AimSub must fully subtract Idle so AimAdd gets the pure (AimPose - Idle) delta, not the whole AimPose.
		_tpsTree.Set("parameters/AimSub/sub_amount", 1f);
		_tpsTree.Set("parameters/ActionSub/sub_amount", 1f);
		_tpsTree.Set("parameters/ActionAdd/add_amount", 1f);
	}

	// Plays a third-person montage on the body through the TPS tree's OneShot slot (mirrors PlayOneShot for FPS).
	private void PlayTpsOneShot(string clip, bool aimed = false)
	{
		if (_tpsTree == null || _tpsActionAnim == null || _tpsPlayer == null
			|| string.IsNullOrEmpty(clip) || !_tpsPlayer.HasAnimation(clip))
			return;
		string tpsRef = aimed ? TpsAimPose : TpsIdlePose;
		if (_tpsActionRefNode != null) _tpsActionRefNode.Animation = tpsRef;
		if (_tpsActionRef2Node != null) _tpsActionRef2Node.Animation = tpsRef;
		_tpsActionAnim.Animation = clip;
		_tpsTree.Set("parameters/Action/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);
	}

	// Adds the additive aim-pitch modifier to the TPS body skeleton (UE ABP aim offset). It leans the spine
	// toward the look pitch on top of the tree's pose; driven per frame in UpdateTpsBody (Remote only).
	private void SetupTpsAimModifier()
	{
		if (CurrentGameMode == GameMode.Server || _glowVisual == null)
			return;
		var skel = _glowVisual.GetNodeOrNull<Skeleton3D>("Armature/Skeleton3D");
		if (skel == null)
			return;
		_tpsAimModifier = new TpsAimModifier
		{
			Name = "TpsAimModifier",
			Additive = true,
			BodyNode = _glowVisual,
			AimBoneName = TpsAimBoneName,
			PitchScale = TpsAimPitchScale,
		};
		skel.AddChild(_tpsAimModifier);
	}

	private void EditorRebuildTree()
	{
		if (!Engine.IsEditorHint())
			return;
		_animEnumHint = null;
		_tpsAnimEnumHint = null;
		NotifyPropertyListChanged();
		var player = GetNodeOrNull<AnimationPlayer>(CharacterAnimationPath);
		if (player == null)
		{ GD.PushWarning("[AnimatedCharacter] CharacterAnimationPath unresolved"); return; }

		var tree = GetNodeOrNull<AnimationTree>(FpsTreePath);
		var blend = tree?.TreeRoot as AnimationNodeBlendTree;
		if (tree == null || blend == null || !blend.HasNode("StandWalk"))
		{
			GD.PushWarning("[AnimatedCharacter] No AnimationTree with StandWalk in scene — add one in the editor.");
			return;
		}
		AssignTreeAnimations(blend, player);
		tree.AnimPlayer = tree.GetPathTo(player);
		GD.Print("[AnimatedCharacter] Animations assigned — Ctrl+S to save.");
	}

	// Placeholder until the third-person AnimationTree exists. Once TpsTreePath points to a TPS tree we will wire
	// the Tps* clips into it the same way EditorRebuildTree does for first person.
	private void EditorRebuildTpsTree()
	{
		if (!Engine.IsEditorHint())
			return;
		_tpsAnimEnumHint = null;
		NotifyPropertyListChanged();
		if (GetNodeOrNull<AnimationTree>(TpsTreePath) == null)
			GD.PushWarning("[AnimatedCharacter] No TPS AnimationTree (TpsTreePath) yet — build it first, then wire the Tps* clips.");
		else
			GD.Print("[AnimatedCharacter] TPS tree found; clip wiring is not implemented yet.");
	}

	// Turns every animation-path string export into a DROPDOWN of the real clips in its player (FPS clips from
	// CharacterAnimationPath, Tps* clips from TpsAnimationPath), so a missing/renamed clip shows up empty instead
	// of a silently-wrong free-text string. Cached after the first non-empty build (retries while the player is
	// not yet resolvable).
	private string GetAnimationEnumHint()
	{
		if (string.IsNullOrEmpty(_animEnumHint))
			_animEnumHint = BuildAnimationEnumHint(CharacterAnimationPath);
		return _animEnumHint;
	}

	private string GetTpsAnimationEnumHint()
	{
		if (string.IsNullOrEmpty(_tpsAnimEnumHint))
			_tpsAnimEnumHint = BuildAnimationEnumHint(TpsAnimationPath);
		return _tpsAnimEnumHint;
	}

	private string BuildAnimationEnumHint(NodePath playerPath)
	{
		var player = GetNodeOrNull<AnimationPlayer>(playerPath);
		if (player == null)
			return "";
		var list = new List<string>();
		foreach (StringName lib in player.GetAnimationLibraryList())
		{
			var library = player.GetAnimationLibrary(lib);
			if (library == null)
				continue;
			foreach (StringName a in library.GetAnimationList())
				list.Add(lib.ToString().Length > 0 ? $"{lib}/{a}" : a.ToString());
		}
		list.Sort();
		return string.Join(",", list);
	}

	// ---- Per-frame update --------------------------------------------------------------------------------

	// ADS / crouch / canted cross-fades. Crouch also lowers the FP body (camera + arms) and resizes the capsule,
	// matching UE's eye-height drop (64 -> 32 cm).
	private void UpdatePostureBlends(float dt)
	{
		_aimBlend = Mathf.MoveToward(_aimBlend, _isAiming ? 1f : 0f, AimBlendSpeed * dt);
		_crouchBlend = Mathf.MoveToward(_crouchBlend, _isCrouched ? 1f : 0f, AimBlendSpeed * dt);
		_cantedBlend = Mathf.MoveToward(_cantedBlend, _cantedAim && _isAiming ? 1f : 0f, AimBlendSpeed * dt);
		if (_bodyNode != null && _bodyRestCaptured)
			_bodyNode.Position = _bodyRest + Vector3.Down * (CrouchCameraDrop * _crouchBlend);
		ApplyCrouchHeight();
	}

	// Live capsule resize from the crouch blend (Stand -> Crouch height), keeping the capsule bottom on the floor.
	// Skipped below a 0.1 mm delta — resizing re-cooks the shape and is wasted on changes the player can't see.
	private void ApplyCrouchHeight()
	{
		if (_capsule == null || _bodyCollision == null)
			return;
		float h = Mathf.Lerp(StandHeight, CrouchHeight, _crouchBlend);
		if (Mathf.Abs(_capsule.Height - h) < 0.0001f)
			return;
		_capsule.Height = h;
		Vector3 pos = _bodyCollision.Position;
		pos.Y = h * 0.5f;
		_bodyCollision.Position = pos;
	}

	// Grip-pose timing: the switch is delayed by GripChangeNotifyTime (hand reaches the grip first), then the
	// additive grip pose fades in — but relaxes back out while sprinting/running.
	private void UpdateGripBlend(float dt)
	{
		if (_gripSwitchDelay >= 0f)
		{
			_gripSwitchDelay -= dt;
			if (_gripSwitchDelay < 0f)
			{ _grip = _pendingGrip; UpdateGripLayer(); }
		}
		bool fastMovement = _sprintAmt > 0.05f || _runAmt > 0.5f;
		_gripBlend = Mathf.MoveToward(_gripBlend, _grip != GripType.Standard && !fastMovement ? 1f : 0f, GripPoseBlendSpeed * dt);
	}

	// Feed the AnimationTree from SimulatedVelocity (Infima): interpolate the move vector and drive the -100..100
	// blend spaces with it, never raw input — diagonals are clamped to the diamond |x|+|y| <= 1. Run/Sprint are
	// separate forward loops blended over the walk space; a stop animation fires when input is released at speed.
	private void DriveLocomotionTree(float dt)
	{
		if (_tree == null)
			return;
		float strafe = _moveInput.X;
		float fwd = -_moveInput.Y;
		float sum = Mathf.Abs(strafe) + Mathf.Abs(fwd);
		if (sum > 1f)
		{ strafe /= sum; fwd /= sum; }
		Vector2 targetVel = new(strafe * 100f, fwd * 100f);
		_simVel = _simVel.Lerp(targetVel, Mathf.Clamp(LocomotionSmoothing * dt, 0f, 1f));
		_tree.Set("parameters/StandWalk/blend_position", _simVel);
		_tree.Set("parameters/AimLoco/blend_position", _simVel);
		_tree.Set("parameters/CrouchLoco/blend_position", _simVel);

		bool fwdMoving = fwd > 0.3f;
		_runAmt = Mathf.MoveToward(_runAmt, (_speed == SpeedMode.Run || _speed == SpeedMode.Sprint) && fwdMoving ? 1f : 0f, SpeedBlendRate * dt);
		_sprintAmt = Mathf.MoveToward(_sprintAmt, _speed == SpeedMode.Sprint && fwdMoving ? 1f : 0f, SpeedBlendRate * dt);
		_tree.Set("parameters/StandRun/blend_amount", _runAmt);
		_tree.Set("parameters/StandSprint/blend_amount", _sprintAmt);
		_tree.Set("parameters/AimMix/blend_amount", _aimBlend);
		_tree.Set("parameters/CrouchMix/blend_amount", _crouchBlend);
		_tree.Set("parameters/GripAdd/add_amount", _gripBlend);
		_gripAimBlend = Mathf.MoveToward(_gripAimBlend, _isAiming ? 1f : 0f, dt / Mathf.Max(GripAimBlendTime, 0.001f));
		_tree.Set("parameters/GripAimBlend/blend_amount", _gripAimBlend);

		bool isMovingNow = _moveInput.LengthSquared() > 0.01f;
		bool canStop = _locomotion != LocomotionState.Jump && _locomotion != LocomotionState.Falling && _locomotion != LocomotionState.Land;
		if (_wasMoving && !isMovingNow && _simVel.LengthSquared() > 400f && canStop)
			TriggerLocoStop(_speed == SpeedMode.Run || _speed == SpeedMode.Sprint ? RunEnd : WalkEnd);
		_wasMoving = isMovingNow;
	}

	// The tree drives the player, so AnimationFinished never fires — poll the OneShot slots and clear our
	// montage / grip-change flags when they finish.
	private void PollMontageState()
	{
		if (_tree == null)
			return;
		if (_montageActive && !_tree.Get("parameters/Action/active").AsBool())
			_montageActive = false;
		if (_gripChangeActive && !_tree.Get("parameters/GripChangeSlot/active").AsBool())
			_gripChangeActive = false;
	}

	private void ApplyHandIk()
	{
		float ikInfluence = IkEnabled ? 1f : 0f;
		_leftHandFabrik?.Set("influence", ikInfluence);
		_rightHandFabrik?.Set("influence", ikInfluence);
	}

	// Procedural view-kick springs. Sway follows the look delta (clamped to SwayMaxDegrees); recoil is a damped
	// spring back to rest (Infima VectorSpringInterp): a fired kick displaces it, the spring pulls it back with
	// a little overshoot. RenderWorldCamera consumes both as the camera kick.
	private void UpdateProceduralSprings(float dt)
	{
		Vector3 swayTarget = new(
			Mathf.Clamp(-_lookDelta.Y * SwayLookFactor, -SwayMaxDegrees, SwayMaxDegrees),
			Mathf.Clamp(-_lookDelta.X * SwayLookFactor, -SwayMaxDegrees, SwayMaxDegrees),
			0f);
		_swayCurrent = _swayCurrent.Lerp(swayTarget, Mathf.Clamp(dt * SwaySpringSpeed, 0f, 1f));
		float rk = RecoilStiffness, rm = Mathf.Max(0.05f, RecoilMass);
		float rc = RecoilDamping * 2f * Mathf.Sqrt(rk * rm);
		_recoilVel += (-_recoilCurrent * rk - _recoilVel * rc) / rm * dt;
		_recoilCurrent += _recoilVel * dt;
		_lookDelta = Vector2.Zero;
	}

	// Advances the view-sway springs from a virtual velocity (move input * speed mode, since the body does not
	// translate yet), the look yaw and the per-frame look delta. Must run before UpdateProceduralSprings, which
	// clears _lookDelta. The result is consumed by ApplyViewmodelProcedural.
	private void StepViewmodelProcedural(float dt)
	{
		Vector3 vel = new Vector3(_moveInput.X, 0f, _moveInput.Y) * SpeedForMode();

		float speed = vel.Length();
		Vector3 dir = speed > 0.01f ? vel / speed : Vector3.Zero;
		Vector3 dirRatio = dir * Mathf.Min(speed / Mathf.Max(0.01f, LeanReferenceSpeed), 1.2f);
		Vector3 leanAccel = (dirRatio - _smoothedDirRatio) * DirectionLeanStiffness - _dirLeanSpringVel * DirectionLeanDamping;
		_dirLeanSpringVel += leanAccel * dt;
		_smoothedDirRatio += _dirLeanSpringVel * dt;

		Vector3 accel = dt > 0.0001f ? (vel - _prevProcVelocity) / dt : Vector3.Zero;
		_prevProcVelocity = vel;
		_inertiaTilt += new Vector3(-accel.Z, 0f, accel.X) * InertiaTiltStrength * dt;
		_inertiaTilt.X = Mathf.Clamp(_inertiaTilt.X, -InertiaTiltMax, InertiaTiltMax);
		_inertiaTilt.Z = Mathf.Clamp(_inertiaTilt.Z, -InertiaTiltMax, InertiaTiltMax);
		_inertiaTilt = _inertiaTilt.Lerp(Vector3.Zero, Mathf.Min(1f, InertiaTiltRecovery * dt));

		if (!_bodyYawInit) { _prevBodyYaw = _lookYaw; _prevBodyPitch = _lookPitch; _bodyYawInit = true; }
		float yawDelta = Mathf.AngleDifference(_prevBodyYaw, _lookYaw);
		_prevBodyYaw = _lookYaw;
		float yawRateDeg = Mathf.RadToDeg(yawDelta / Mathf.Max(0.0001f, dt));
		float targetLag = Mathf.Clamp(-yawRateDeg * BodyYawLagStrength, -BodyYawLagMax, BodyYawLagMax);
		_bodyYawLag = Mathf.Lerp(_bodyYawLag, targetLag, Mathf.Min(1f, BodyYawLagSmoothing * dt));

		float pitchDelta = _lookPitch - _prevBodyPitch;
		_prevBodyPitch = _lookPitch;
		float pitchRateDeg = Mathf.RadToDeg(pitchDelta / Mathf.Max(0.0001f, dt));
		float targetPitchLag = Mathf.Clamp(-pitchRateDeg * BodyYawLagStrength, -BodyYawLagMax, BodyYawLagMax);
		_bodyPitchLag = Mathf.Lerp(_bodyPitchLag, targetPitchLag, Mathf.Min(1f, BodyYawLagSmoothing * dt));

		_mouseInertia.Y += _lookDelta.X * MouseInertiaYaw;
		_mouseInertia.X += _lookDelta.Y * MouseInertiaPitch;
		_mouseInertia.X = Mathf.Clamp(_mouseInertia.X, -MouseInertiaMaxPitch, MouseInertiaMaxPitch);
		_mouseInertia.Y = Mathf.Clamp(_mouseInertia.Y, -MouseInertiaMaxYaw, MouseInertiaMaxYaw);
		_mouseInertia = _mouseInertia.Lerp(Vector3.Zero, Mathf.Min(1f, MouseInertiaRecovery * dt));
		bool building = _mouseInertia.LengthSquared() > _mouseInertiaSmoothed.LengthSquared();
		float smoothRate = building ? MouseInertiaSmoothingIn : MouseInertiaSmoothingOut;
		_mouseInertiaSmoothed = _mouseInertiaSmoothed.Lerp(_mouseInertia, Mathf.Min(1f, smoothRate * dt));
	}

	// Final authoritative writer of the head_pitch (camera-pivot) transform at runtime: recomposes the base
	// (rest + crouch-drop, rest-rot + look-yaw) and adds the view-sway offset, so toggling layers or disabling
	// ViewSwayEnabled cleanly returns to the base pose without accumulation. Runs after UpdateBodyYaw.
	private void ApplyViewmodelProcedural()
	{
		if (_bodyNode == null || !_bodyRestCaptured)
			return;

		// Movement-driven ambient (lean + velocity tilt) — strongly suppressed while aiming so the sights stay
		// on target when strafing. Look-driven sway (look-lag + mouse inertia) — kept more present so the weapon
		// still drifts on turns/look during ADS, like COD. Each group gets its own ADS multiplier.
		Vector3 movePos = Vector3.Zero;
		Vector3 moveRotDeg = Vector3.Zero;
		Vector3 lookRotDeg = Vector3.Zero;
		if (ViewSwayEnabled)
		{
			if (DirectionLeanEnabled)
			{
				float strafe = _smoothedDirRatio.X;
				float forward = -_smoothedDirRatio.Z;
				movePos += new Vector3(
					strafe * StrafeLeanPos,
					-Mathf.Max(0f, forward) * ForwardLeanPosDown + Mathf.Max(0f, -forward) * ForwardLeanPosDown * 0.6f,
					-forward * ForwardLeanPosForward);
				moveRotDeg += new Vector3(-forward * ForwardLeanPitch, 0f, -strafe * StrafeLeanRoll);
			}
			if (VelocityTiltEnabled)
				moveRotDeg += new Vector3(_inertiaTilt.X, 0f, _inertiaTilt.Z);
			if (BodyYawLagEnabled)
			{
				lookRotDeg.Y += _bodyYawLag;
				lookRotDeg.X += _bodyPitchLag;
			}
			if (MouseInertiaEnabled)
				lookRotDeg += new Vector3(_mouseInertiaSmoothed.X, _mouseInertiaSmoothed.Y, -_mouseInertiaSmoothed.Y * MouseInertiaRollMul);
		}

		float adsMove = Mathf.Lerp(1f, ViewSwayAdsMul, _aimBlend);
		float adsLook = Mathf.Lerp(1f, ViewSwayAdsLookMul, _aimBlend);
		Vector3 pos = movePos * adsMove;
		Vector3 rotDeg = moveRotDeg * adsMove + lookRotDeg * adsLook;
		_viewSwayPos = pos;
		_viewSwayRotDeg = rotDeg;

		Vector3 worldPos = pos * ViewSwayWorldMul;
		Vector3 worldRotDeg = rotDeg * ViewSwayWorldMul;
		Vector3 basePos = _bodyRest + Vector3.Down * (CrouchCameraDrop * _crouchBlend);
		float baseYaw = MouseLookEnabled ? _lookYaw : _bodyRestRot.Y;
		_bodyNode.Position = basePos + worldPos;
		_bodyNode.Rotation = new Vector3(
			_bodyRestRot.X + Mathf.DegToRad(worldRotDeg.X),
			baseYaw + Mathf.DegToRad(worldRotDeg.Y),
			_bodyRestRot.Z + Mathf.DegToRad(worldRotDeg.Z));
	}

	// Yaw the body pivot (head_pitch) that carries the cameras + arms. Pitch is handled per-mode elsewhere: the
	// FPS camera pitches in RenderWorldCamera, the TPS pivot in UpdateTpsCamera — so this node only takes yaw.
	private void UpdateBodyYaw()
	{
		if (!MouseLookEnabled) return;
		Node3D yawNode = _bodyNode ?? _cam?.GetParentOrNull<Node3D>();
		if (yawNode == null) return;
		Vector3 r = yawNode.Rotation;
		r.Y = _lookYaw;
		yawNode.Rotation = r;
	}

	// The world camera renders the main world (its own SubViewport keeps the gun unclipped + lit) but still
	// bobs with the head animation: we read the eye socket's animated delta from rest and layer it on top of
	// the input look (yaw on the body, pitch here) + the sway/recoil view-kick. FOV lerps for ADS.
	private void RenderWorldCamera(float dt)
	{
		if (_cam == null) return;
		if (!_camRigCaptured)
		{
			_camRestLocal = _cam.Transform;
			if (_viewmodelCamAnchor != null) _eyeRest = _viewmodelCamAnchor.GlobalTransform;
			_cam.Fov = HipFov;
			_camRigCaptured = true;
		}
		Vector3 kick = _swayCurrent * Mathf.Lerp(1f, AimSwayMultiplier, _aimBlend)
			+ _recoilCurrent * Mathf.Lerp(1f, AimRecoilMultiplier, _aimBlend);
		Transform3D bob = _viewmodelCamAnchor != null
			? _eyeRest.AffineInverse() * _viewmodelCamAnchor.GlobalTransform
			: Transform3D.Identity;
		Basis look = Basis.FromEuler(new Vector3(
			(MouseLookEnabled ? _lookPitch : 0f) + Mathf.DegToRad(kick.X),
			0f,
			Mathf.DegToRad(kick.Z)));
		_cam.Transform = _camRestLocal * new Transform3D(look, Vector3.Zero) * bob;
		_cam.Fov = Mathf.Lerp(_cam.Fov, Mathf.Lerp(HipFov, AimFov, _aimBlend), Mathf.Clamp(dt * AimBlendSpeed, 0f, 1f));
	}

	// The viewmodel camera rides the weapon's eye socket inside the own-world SubViewport, taking its FULL
	// pose (position + the head bone's animated rotation) so the gun renders correctly and follows the head
	// animation. FOV mirrors the world camera. Runs in the editor too for a live preview.
	private void RenderFpsCamera()
	{
		if (_viewmodelCam == null || _viewmodelCamAnchor == null) return;
		Transform3D sway = new(
			Basis.FromEuler(new Vector3(Mathf.DegToRad(_viewSwayRotDeg.X), Mathf.DegToRad(_viewSwayRotDeg.Y), Mathf.DegToRad(_viewSwayRotDeg.Z))),
			_viewSwayPos);
		_viewmodelCam.GlobalTransform = _viewmodelCamAnchor.GlobalTransform * sway;
		if (_cam != null) _viewmodelCam.Fov = _cam.Fov;
	}

	// ---- Weapon offset & montages ------------------------------------------------------------------------

	private static Transform3D MakeOffset(Vector3 posMetres, Vector3 rotDegrees) =>
		new(Basis.FromEuler(new Vector3(Mathf.DegToRad(rotDegrees.X), Mathf.DegToRad(rotDegrees.Y), Mathf.DegToRad(rotDegrees.Z))), posMetres);

	private void ApplyWeaponOffset()
	{
		var wbm = WeaponBoneModifier.Instance;
		if (wbm == null)
			return;
		Transform3D ads = Transform3D.Identity.InterpolateWith(MakeOffset(AdsOffsetPosition, AdsOffsetRotation), _aimBlend);
		Transform3D crouch = Transform3D.Identity.InterpolateWith(MakeOffset(CrouchOffsetPosition, CrouchOffsetRotation), _crouchBlend);
		Transform3D canted = Transform3D.Identity.InterpolateWith(MakeOffset(CantedOffsetPosition, CantedOffsetRotation), _cantedBlend);
		Transform3D recoil = MakeOffset(
			new Vector3(0f, 0f, Mathf.Abs(_recoilCurrent.X) * WeaponRecoilKickback),
			_recoilCurrent * WeaponRecoilRotScale);
		wbm.Transform = ads * crouch * canted * recoil;
	}

	// Hitscan from the active camera on fire (Hitscan.Cast, excluding our own body), spawning a cosmetic impact
	// via BulletImpactManager. Skipped on Server and when CanFire is off; the full game would resolve damage here.
	private void FireHitscan()
	{
		if (!CanFire || CurrentGameMode == GameMode.Server)
			return;
		Camera3D cam = CurrentViewMode == ViewMode.Tps && _tpsCam != null ? _tpsCam : _cam;
		if (cam == null)
			return;
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
			return;
		HitInfo hit = Hitscan.Cast(space, cam.GlobalPosition, -cam.GlobalTransform.Basis.Z, HitscanRange, exclude: GetRid(), mask: HitscanMask);
		if (hit.Hit)
			BulletImpactManager.Instance?.Spawn(hit.Position, hit.Normal, hit.Material);
	}

	// Grenade throw trajectory preview (GrenadeAimGuide). Shown while aiming a grenade in FPS/TPS (never Server);
	// origin + velocity come from the active camera so the ribbon matches where the grenade would actually fly.
	private void UpdateAimGuide()
	{
		if (_aimGuide == null)
			return;
		Camera3D cam = CurrentViewMode == ViewMode.Tps && _tpsCam != null ? _tpsCam : _cam;
		bool show = _grenadeAiming && cam != null;
		_aimGuide.SetGuideVisible(show);
		if (!show)
			return;
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
			return;
		Vector3 forward = -cam.GlobalTransform.Basis.Z;
		GrenadeTrajectory.Predict(space, cam.GlobalPosition + forward * 0.4f, forward * GrenadeThrowSpeed, GetRid(), _aimPath, out Vector3 landing, out Vector3 landingNormal);
		_aimGuide.UpdatePath(_aimPath, landing, landingNormal);
	}

	private bool FootstepsActive => _footstepAudio != null && CurrentGameMode != GameMode.Server;

	// Distance-based footstep cadence (FootstepController), driven by a virtual move speed since the demo body
	// doesn't physically translate. Each step casts the ground for its material and plays through FootstepAudio.
	private void UpdateFootsteps(float dt)
	{
		if (!FootstepsActive)
			return;
		float speed = Mathf.Min(_moveInput.Length(), 1f) * SpeedForMode();
		_footstepLogic.Step(new FootstepInput
		{
			Dt = dt,
			HorizontalSpeed = speed,
			OnFloor = true,
			ShiftHeld = false,
			CrouchHeld = _isCrouched,
			IsSprinting = _speed == SpeedMode.Sprint,
			IsSliding = false,
		});
		if (!_footstepLogic.DidStepThisFrame)
			return;
		HitInfo ground = CastGround();
		_footstepAudio.PlayStep(GlobalPosition, GroundMaterial(ground), _footstepLogic.StepLoudness, InTunnel(ground), _speed == SpeedMode.Sprint);
	}

	private void PlayFootstepJump()
	{
		if (!FootstepsActive)
			return;
		HitInfo g = CastGround();
		_footstepAudio.PlayJump(GlobalPosition, GroundMaterial(g), _speed == SpeedMode.Sprint ? 1f : 0.75f, InTunnel(g));
	}

	private void PlayFootstepLand()
	{
		if (!FootstepsActive)
			return;
		HitInfo g = CastGround();
		_footstepAudio.PlayLand(GlobalPosition, GroundMaterial(g), 1f, InTunnel(g));
	}

	private float SpeedForMode() => _speed switch
	{
		SpeedMode.Sprint => ConVars.Sv.SprintSpeed,
		SpeedMode.Run => Mathf.Lerp(ConVars.Sv.WalkSpeed, ConVars.Sv.SprintSpeed, 0.6f),
		_ => ConVars.Sv.WalkSpeed,
	};

	private HitInfo CastGround()
	{
		var space = GetWorld3D()?.DirectSpaceState;
		return space == null ? default : Hitscan.Cast(space, GlobalPosition + Vector3.Up * 0.4f, Vector3.Down, 1.5f, exclude: GetRid(), mask: 1u);
	}

	private static StringName GroundMaterial(HitInfo g) => g.Hit ? g.Material : (StringName)"default";
	private static bool InTunnel(HitInfo g) => g.Hit && g.Collider != null && g.Collider.IsInGroup("tunnel");

	// Play a montage (fire / reload / inspect / …) through the tree's OneShot slot so it blends OVER locomotion
	// instead of hard-interrupting it. The slot's fade time is configured on the AnimationTree in the scene.
	private void PlayOneShot(string anim, bool aimed = false)
	{
		if (string.IsNullOrEmpty(anim) || _tree == null || _actionAnim == null || !_player.HasAnimation(anim))
			return;
		string actionRef = aimed ? ActionRefAim : ActionRefIdle;
		if (_actionRefNode != null) _actionRefNode.Animation = actionRef;
		if (_actionRef2Node != null) _actionRef2Node.Animation = actionRef;
		_actionAnim.Animation = anim;
		_tree.Set("parameters/Action/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);
		_montageActive = true;
	}

	private void PlayGripChange()
	{
		if (string.IsNullOrEmpty(GripChange))
			return;
		if (_gripChangeAnim != null && _tree != null && _player.HasAnimation(GripChange))
		{
			_gripChangeAnim.Animation = GripChange;
			_tree.Set("parameters/GripChangeSlot/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);
			_gripChangeActive = true;
			return;
		}
		PlayOneShot(GripChange);
	}

	private void TriggerLocoStop(string anim)
	{
		if (_locoStopAnim == null || _tree == null || string.IsNullOrEmpty(anim) || !_player.HasAnimation(anim))
			return;
		_locoStopAnim.Animation = anim;
		_tree.Set("parameters/LocoStop/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);
	}

	// ---- Editor preview & ADS crosshair ------------------------------------------------------------------

	// Editor-only mirror of _Process: drives the tree, ADS pose and cameras for a live preview. With AdsTestMode
	// on it forces the aimed pose + AimFov and freezes the tree so the bob doesn't shift the sights while tuning.
	private void ApplyEditorPreview(float dt = 0f)
	{
		var player = GetNodeOrNull<AnimationPlayer>(CharacterAnimationPath);
		var tree = GetNodeOrNull<AnimationTree>(FpsTreePath);
		if (player == null || tree == null)
			return;

		_leftHandFabrik ??= GetNodeOrNull<Node3D>(LeftHandFabrikPath);
		_rightHandFabrik ??= GetNodeOrNull<Node3D>(RightHandFabrikPath);

		if (!_editorTreeReady)
		{
			if (tree.TreeRoot is AnimationNodeBlendTree setupBt)
				AssignTreeAnimations(setupBt, player);
			tree.Active = true;
			tree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
			ApplyViewmodelLayer();
			_editorTreeReady = true;
		}

		_aimBlend = (_isAiming || _adsTestMode) ? 1f : 0f;
		_crouchBlend = _isCrouched ? 1f : 0f;
		_cantedBlend = (_cantedAim && _isAiming) ? 1f : 0f;

		_bodyCollision ??= GetNodeOrNull<CollisionShape3D>(BodyCollisionPath);
		if (_capsule == null && _bodyCollision?.Shape is CapsuleShape3D editorCapsule)
			_capsule = editorCapsule;
		ApplyCrouchHeight();

		tree.Set("parameters/AimMix/blend_amount", _aimBlend);
		tree.Set("parameters/StandSprint/blend_amount", _sprintAmt);
		tree.Set("parameters/StandRun/blend_amount", Mathf.Max(_runAmt, _sprintAmt));
		tree.Set("parameters/CrouchMix/blend_amount", _crouchBlend);
		tree.Set("parameters/StandWalk/blend_position", _simVel);
		tree.Set("parameters/AimLoco/blend_position", _simVel);
		tree.Set("parameters/CrouchLoco/blend_position", _simVel);
		var editorFastMovement = _sprintAmt > 0.05f || _runAmt > 0.5f;
		float gripAmt = _grip != GripType.Standard && !editorFastMovement ? 1f : 0f;
		tree.Set("parameters/GripAdd/add_amount", gripAmt);
		tree.Set("parameters/GripAimBlend/blend_amount", _aimBlend);
		if (_grip != GripType.Standard && tree.TreeRoot is AnimationNodeBlendTree bt)
		{
			string nonAim = _grip == GripType.Angled ? IdlePoseGripAngled : IdlePoseGripVertical;
			string aim = _grip == GripType.Angled ? AimPoseGripAngled : AimPoseGripVertical;
			if (bt.HasNode("GripPose") && bt.GetNode("GripPose") is AnimationNodeAnimation gp)
				gp.Animation = nonAim;
			if (bt.HasNode("GripPoseAim") && bt.GetNode("GripPoseAim") is AnimationNodeAnimation gpa)
				gpa.Animation = aim;
		}
		_cam ??= GetNodeOrNull<Camera3D>(HeadCameraPath);
		_viewmodelCam ??= GetNodeOrNull<Camera3D>(ViewmodelCameraPath);
		_viewmodelCamAnchor ??= GetNodeOrNull<Node3D>(ViewmodelCameraAnchorPath);
		_tpsCam ??= GetNodeOrNull<Camera3D>(TpsCameraPath);
		_glowVisual ??= GetNodeOrNull<Node3D>(GlowVisualPath);
		_viewmodelLayer ??= GetNodeOrNull<CanvasLayer>(ViewmodelLayerPath);
		ApplyModeVisibility();
		if (_cam != null)
			_cam.Fov = (_isAiming || _adsTestMode) ? AimFov : HipFov;

		var ikInfluence = IkEnabled ? 1f : 0f;
		_leftHandFabrik?.Set("influence", ikInfluence);
		_rightHandFabrik?.Set("influence", ikInfluence);

		ApplyWeaponOffset();
		tree.Advance(_adsTestMode ? 0.0 : dt);
		RenderFpsCamera();
		UpdateAdsCrosshair();
	}

	// ADS calibration crosshair: a red sphere + H/V lines drawn at AdsCalibrationDistance in front of the
	// viewmodel camera (so they live in the same SubViewport world as the gun). Tune AdsOffsetPosition/
	// Rotation until the iron sights sit on the crosshair centre, then turn AdsTestMode off.
	private void UpdateAdsCrosshair()
	{
		if (_adsTestMode && !_adsTestPrev) SpawnAdsCrosshair();
		else if (!_adsTestMode && _adsTestPrev) DespawnAdsCrosshair();
		_adsTestPrev = _adsTestMode;
		if (_adsTestMode) PoseAdsCrosshair();
	}

	private void SpawnAdsCrosshair()
	{
		Camera3D cam = _viewmodelCam ?? _cam;
		if (cam == null || _adsMarker != null) return;
		uint layer = cam.CullMask != 0 ? cam.CullMask : 1u;
		_adsMarker = MakeCrosshairMesh("_AdsMarker", new SphereMesh { Radius = AdsCalibrationSize, Height = AdsCalibrationSize * 2f }, layer, cam);
		_adsLineH = MakeCrosshairMesh("_AdsLineH", new BoxMesh { Size = new Vector3(100f, AdsCalibrationSize, AdsCalibrationSize) }, layer, cam);
		_adsLineV = MakeCrosshairMesh("_AdsLineV", new BoxMesh { Size = new Vector3(AdsCalibrationSize, 100f, AdsCalibrationSize) }, layer, cam);
		PoseAdsCrosshair();
	}

	private MeshInstance3D MakeCrosshairMesh(string name, Mesh mesh, uint layer, Camera3D parent)
	{
		var mi = new MeshInstance3D
		{
			Name = name,
			Mesh = mesh,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = AdsCalibrationColor, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true },
			Layers = layer,
		};
		parent.AddChild(mi);
		mi.Owner = null;
		return mi;
	}

	private void PoseAdsCrosshair()
	{
		Vector3 pos = new(0f, 0f, -AdsCalibrationDistance);
		float t = AdsCalibrationSize;
		if (_adsMarker != null) { _adsMarker.Position = pos; if (_adsMarker.Mesh is SphereMesh s) { s.Radius = t; s.Height = t * 2f; } SetCrosshairColor(_adsMarker); }
		if (_adsLineH != null) { _adsLineH.Position = pos; if (_adsLineH.Mesh is BoxMesh b) b.Size = new Vector3(100f, t, t); SetCrosshairColor(_adsLineH); }
		if (_adsLineV != null) { _adsLineV.Position = pos; if (_adsLineV.Mesh is BoxMesh b) b.Size = new Vector3(t, 100f, t); SetCrosshairColor(_adsLineV); }
	}

	private void SetCrosshairColor(MeshInstance3D mi) { if (mi.MaterialOverride is StandardMaterial3D m) m.AlbedoColor = AdsCalibrationColor; }

	private void DespawnAdsCrosshair()
	{
		_adsMarker?.QueueFree(); _adsLineH?.QueueFree(); _adsLineV?.QueueFree();
		_adsMarker = _adsLineH = _adsLineV = null;
	}

	// ---- Godot lifecycle ---------------------------------------------------------------------------------

	// Runs last (ProcessPriority 100) so the AnimationTree poses the skeleton before our IK writes bone poses on
	// top — otherwise the tree overwrites the IK and the hands snap back to the anim pose. Idle needs no explicit
	// play: the locomotion blend space rests at its centre (idle) by default.
	public override void _Ready()
	{
		SetProcess(true);
		ProcessPriority = 100;
		if (Engine.IsEditorHint())
			return;
		_player = GetNodeOrNull<AnimationPlayer>(CharacterAnimationPath);
		if (_player == null)
		{
			GD.PushWarning("[AnimatedCharacter] CharacterAnimationPath unresolved");
			return;
		}
		ResolveWeaponPlayers();
		SetupCapsule();
		BuildAnimationTree();
		BuildTpsTree();
		SetupTpsAimModifier();
		ApplyViewmodelLayer();
		if (CurrentGameMode != GameMode.Server)
		{
			_aimGuide = new GrenadeAimGuide();
			GetParent()?.CallDeferred(Node.MethodName.AddChild, _aimGuide);
		}
	}

	// Runtime frame: posture/grip blends, feed the tree, poll montage state, IK, weapon offset, advance the tree,
	// procedural springs, then the cameras. ApplyEditorPreview mirrors this order for the editor.
	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
		{ ApplyEditorPreview((float)delta); return; }

		float dt = (float)delta;
		UpdatePostureBlends(dt);
		UpdateGripBlend(dt);
		DriveLocomotionTree(dt);
		PollMontageState();
		ApplyHandIk();
		ApplyWeaponOffset();
		_tree?.Advance(dt);
		StepViewmodelProcedural(dt);
		UpdateProceduralSprings(dt);
		ApplyModeVisibility();
		if (CurrentGameMode != GameMode.Server)
		{
			_tpsTree?.Set("parameters/AimAdd/add_amount", _aimBlend);
			UpdateFootsteps(dt);
			UpdateBodyYaw();
			ApplyViewmodelProcedural();
			UpdateTpsBody(dt);
			if (CurrentViewMode == ViewMode.Tps && _tpsCam != null)
				UpdateTpsCamera(dt);
			else
			{
				RenderWorldCamera(dt);
				RenderFpsCamera();
			}
			UpdateAimGuide();
		}
	}

	// Every string export here is an animation key (lib/clip), so present them as an enum of the clips that
	// actually exist in the player — a missing/renamed clip then shows up empty instead of silently wrong.
	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		var type = (Variant.Type)property["type"].AsInt64();
		var usage = (PropertyUsageFlags)property["usage"].AsInt64();
		if (type != Variant.Type.String || !usage.HasFlag(PropertyUsageFlags.ScriptVariable))
			return;
		string name = (string)property["name"];
		if (name.EndsWith("BoneName"))   // bone names are not animation clips
			return;
		string hint = name.StartsWith("Tps") ? GetTpsAnimationEnumHint() : GetAnimationEnumHint();
		if (!string.IsNullOrEmpty(hint))
		{
			property["hint"] = (int)PropertyHint.Enum;
			property["hint_string"] = hint;
		}
	}

	// ---- Public state setters ----------------------------------------------------------------------------

	public void SetMoveInput(Vector2 moveLocal) => _moveInput = moveLocal;
	public void SetAiming(bool aiming) { if (_isAiming == aiming) return; _isAiming = aiming; if (_currentWeapon != null) _currentWeapon.Aiming = aiming; UpdateGripLayer(); }
	public void SetCanted(bool canted) => _cantedAim = canted;
	public void SetCrouched(bool crouched) { if (_isCrouched == crouched) return; _isCrouched = crouched; }
	public void SetSpeedMode(SpeedMode mode) { if (_speed == mode) return; _speed = mode; }
	public void SetMagazineFill(float fill01) { _magFill = fill01; _currentWeapon?.SetMagazineFill(fill01); }
	public void SetViewMode(ViewMode mode) => CurrentViewMode = mode;
	public void ToggleViewMode() => CurrentViewMode = CurrentViewMode == ViewMode.Tps ? ViewMode.Fps : ViewMode.Tps;
	// Mouse-wheel zoom for the TPS arm length: dir > 0 zooms in (closer), dir < 0 zooms out. Clamped to the range.
	public void AdjustTpsZoom(float dir)
	{
		if (_tpsZoomDist < 0f)
			_tpsZoomDist = TpsCameraOffset.Z;
		_tpsZoomDist = Mathf.Clamp(_tpsZoomDist - dir * TpsZoomStep, TpsZoomMin, TpsZoomMax);
	}
	public void SetGrenadeAiming(bool aiming) => _grenadeAiming = aiming;

	public void SetLookDelta(Vector2 deltaDegrees)
	{
		_lookDelta = deltaDegrees;
		if (MouseLookEnabled)
		{
			_lookYaw -= Mathf.DegToRad(deltaDegrees.X);
			float lim = Mathf.DegToRad(LookPitchLimitDeg);
			_lookPitch = Mathf.Clamp(_lookPitch - Mathf.DegToRad(deltaDegrees.Y), -lim, lim);
		}
	}

	public void AddRecoilKick(Vector3 degreesXYZ)
	{
		_recoilCurrent += degreesXYZ;
		float mag = _recoilCurrent.Length();
		if (mag > RecoilMaxDegrees)
			_recoilCurrent = _recoilCurrent * (RecoilMaxDegrees / mag);
	}

	// ---- Public action triggers --------------------------------------------------------------------------

	public void JumpStarted() { _locomotion = LocomotionState.Jump; PlayOneShot(JumpStart); PlayFootstepJump(); }
	public void Falling() => _locomotion = LocomotionState.Falling;
	public void Landed() { _locomotion = LocomotionState.Land; PlayOneShot(JumpEnd); PlayFootstepLand(); }

	public void TriggerFire(FireMode mode, bool empty)
	{
		string anim = empty ? FireEmpty : (_isAiming ? FireAimed : (mode == FireMode.Auto ? FireAuto : FireSemi));
		bool aimed = _isAiming && !empty;
		PlayOneShot(anim, aimed);
		PlayTpsOneShot(empty ? TpsFireEmpty : TpsFire, aimed);
		if (!empty)
		{
			_currentWeapon?.Fire();
			FireHitscan();
		}
		AddRecoilKick(_isAiming ? RecoilImpulseAimed : RecoilImpulseHipfire);
	}

	public void TriggerReload(bool empty, bool quick)
	{
		string anim;
		if (quick) anim = _isAiming ? ReloadQuickAimed : ReloadQuick;
		else if (empty) anim = _isAiming ? ReloadEmptyAimed : ReloadEmpty;
		else anim = _isAiming ? ReloadAimed : Reload;
		PlayOneShot(anim, _isAiming);
		PlayTpsOneShot(quick ? (_isAiming ? TpsReloadQuickAimed : TpsReloadQuick)
			: empty ? (_isAiming ? TpsReloadEmptyAimed : TpsReloadEmpty)
			: (_isAiming ? TpsReloadAimed : TpsReload), _isAiming);
		if (quick) _currentWeapon?.ReloadQuick();
		else if (empty) _currentWeapon?.ReloadEmpty();
		else _currentWeapon?.Reload();
	}

	public void TriggerMagCheck() { PlayOneShot(_isAiming ? MagCheckAimed : MagCheck, _isAiming); PlayTpsOneShot(_isAiming ? TpsMagCheckAimed : TpsMagCheck, _isAiming); _currentWeapon?.MagCheck(); }
	public void TriggerFireModeSwitch(FireMode mode) { _fireModeName = mode.ToString(); PlayOneShot(FireModeSwitch); PlayTpsOneShot(TpsFireModeSwitch); _currentWeapon?.SetFireMode(_fireModeName); }
	public void TriggerInspect(bool empty) { PlayOneShot(empty ? InspectEmpty : Inspect); PlayTpsOneShot(empty ? TpsInspectEmpty : TpsInspect); _currentWeapon?.Inspect(); }
	public void TriggerClearJam(bool rack) { PlayOneShot(rack ? ClearJamRack : ClearJamMagSwipe); PlayTpsOneShot(rack ? TpsClearJamRack : TpsClearJamMagSwipe); if (rack) _currentWeapon?.ClearJamRack(); else _currentWeapon?.ClearJamMagSwipe(); }
	public void TriggerGrenadeThrow() { PlayOneShot(GrenadeThrowQuick); PlayTpsOneShot(TpsGrenadeThrowQuick); }
	public void TriggerHealSyringe() { PlayOneShot(HealSyringe); PlayTpsOneShot(TpsHealSyringe); }

	public void TriggerGripChange()
	{
		_pendingGrip = (GripType)(((int)_grip + 1) % 3);
		_gripSwitchDelay = GripChangeNotifyTime;
		PlayGripChange();
	}

	public void TriggerInteract(InteractKind kind)
	{
		PlayOneShot(kind switch { InteractKind.Push => InteractPush, InteractKind.Punch => InteractPunch, _ => InteractGrab });
		PlayTpsOneShot(kind switch { InteractKind.Push => TpsInteractPush, InteractKind.Punch => TpsInteractPunch, _ => TpsInteractGrab });
	}

	public void TriggerMelee(MeleeDirection direction)
	{
		PlayOneShot(direction switch { MeleeDirection.Left => MeleeSwingLeft, MeleeDirection.Right => MeleeSwingRight, _ => MeleeBashForward });
		PlayTpsOneShot(direction switch { MeleeDirection.Left => TpsMeleeSwingLeft, MeleeDirection.Right => TpsMeleeSwingRight, _ => TpsMeleeBashForward });
	}

	public void Equipping(bool quick) { PlayOneShot(quick ? EquipQuick : Equip); PlayTpsOneShot(quick ? TpsEquipQuick : TpsEquip); _currentWeapon?.Equip(); }
	public void Holstering() { PlayOneShot(Holster); PlayTpsOneShot(TpsHolster); }
}
