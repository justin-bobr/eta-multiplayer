using Godot;
using System.Collections.Generic;

/// <summary>Local-player cosmetic VIEW (per-frame): viewmodel render, cameras, procedural sway,
/// locomotion tree, ADS crosshair, editor preview. Split out of NetworkPlayer — only the local
/// player runs this per-frame chain; puppets/server use the shared posing (UpdateTpsBodyAim).</summary>
public partial class LocalPlayer
{
	/// <summary>The local per-frame view chain (was NetworkPlayer._Process). Invoked by LocalPlayer._Process
	/// after the visual interpolation + preload gate, and directly in the editor for the [Tool] preview.</summary>
	private void RenderLocalView(double delta)
	{
		if (Engine.IsEditorHint())
		{ ApplyEditorPreview((float)delta); return; }
		if (IsDead)
			return;   // dead = spectating through a puppet cam; don't animate the hidden viewmodel
		using var _prof = MiniProfiler.SampleClient("LocalPlayer.RenderLocalView");

		float dt = (float)delta;
		UpdateVisualBlends(dt);
		UpdateGripBlend(dt);
		using (MiniProfiler.SampleClient("View.DriveLocomotionTree"))
			DriveLocomotionTree(dt);
		using (MiniProfiler.SampleClient("View.UpdateViewmodelMontages"))
			UpdateViewmodelMontages();
		PollMontageState();
		using (MiniProfiler.SampleClient("View.ApplyHandIk"))
			ApplyHandIk();
		using (MiniProfiler.SampleClient("View.ApplyWeaponOffset"))
			ApplyWeaponOffset();
		using (MiniProfiler.SampleClient("View.FpsTree.Advance"))
			_tree?.Advance(dt);
		using (MiniProfiler.SampleClient("View.StepViewmodelProcedural"))
			StepViewmodelProcedural(dt);
		UpdateProceduralSprings(dt);
		using (MiniProfiler.SampleClient("View.ApplyModeVisibility"))
			ApplyModeVisibility();
		ApplyViewmodelProcedural();
		if (ViewMode == ViewMode.Tps && _tpsCam != null)
			UpdateTpsCamera(dt);
		else
		{
			RenderWorldCamera(dt);
			RenderFpsCamera();
		}
		using (MiniProfiler.SampleClient("View.UpdateAdsPostFx"))
			UpdateAdsPostFx();
	}

	private void UpdateVisualBlends(float dt)
	{
		float adsTarget = Movement?.AdsBlend ?? (_isAiming ? 1f : 0f);
		_aimBlend = Mathf.MoveToward(_aimBlend, adsTarget, AimBlendSpeed * dt);
		_crouchBlend = Movement?.CrouchBlend ?? 0f;
		_cantedBlend = Mathf.MoveToward(_cantedBlend, _cantedAim && _isAiming ? 1f : 0f, AimBlendSpeed * dt);
		if (_bodyNode != null && _bodyRestCaptured)
			_bodyNode.Position = _bodyRest + Vector3.Down * (CrouchCameraDrop * _crouchBlend);
	}

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

	// Cached AnimationTree parameter paths — avoids a string→StringName allocation per Set every frame.
	private static readonly StringName _pStandWalk = "parameters/StandWalk/blend_position";
	private static readonly StringName _pAimLoco = "parameters/AimLoco/blend_position";
	private static readonly StringName _pCrouchLoco = "parameters/CrouchLoco/blend_position";
	private static readonly StringName _pStandRun = "parameters/StandRun/blend_amount";
	private static readonly StringName _pStandSprint = "parameters/StandSprint/blend_amount";
	private static readonly StringName _pAimMix = "parameters/AimMix/blend_amount";
	private static readonly StringName _pCrouchMix = "parameters/CrouchMix/blend_amount";
	private static readonly StringName _pGripAdd = "parameters/GripAdd/add_amount";
	private static readonly StringName _pGripAimBlend = "parameters/GripAimBlend/blend_amount";

	private void DriveLocomotionTree(float dt)
	{
		if (_tree == null)
			return;
		Vector3 localVel = GlobalTransform.Basis.Inverse() * Velocity;
		float refSpeed = Mathf.Max(0.1f, ConVars.Sv.WalkSpeed);
		float strafe = Mathf.Clamp(localVel.X / refSpeed, -1f, 1f);
		float fwd = Mathf.Clamp(-localVel.Z / refSpeed, -1f, 1f);
		Vector2 targetVel = new(strafe * 100f, fwd * 100f);
		_simVel = _simVel.Lerp(targetVel, Mathf.Clamp(LocomotionSmoothing * dt, 0f, 1f));
		_tree.Set(_pStandWalk, _simVel);
		_tree.Set(_pAimLoco, _simVel);
		_tree.Set(_pCrouchLoco, _simVel);

		float horizSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
		bool fwdMoving = fwd > 0.3f;
		// Normal movement (WalkSpeed) IS the run gait — shift-walk (ShiftSpeed) is the slow one. The old
		// "> WalkSpeed + 0.1" threshold could never be reached at normal speed (== WalkSpeed exactly), so
		// the Run_F pose only ever engaged while sprinting. Threshold halfway between shift and normal.
		float runThreshold = (ConVars.Sv.ShiftSpeed + ConVars.Sv.WalkSpeed) * 0.5f;
		bool running = horizSpeed > runThreshold && fwdMoving;
		bool sprinting = (Movement?.ActuallySprinting ?? false) && fwdMoving;
		_runAmt = Mathf.MoveToward(_runAmt, running || sprinting ? 1f : 0f, SpeedBlendRate * dt);
		_sprintAmt = Mathf.MoveToward(_sprintAmt, sprinting ? 1f : 0f, SpeedBlendRate * dt);
		_tree.Set(_pStandRun, _runAmt);
		_tree.Set(_pStandSprint, _sprintAmt);
		_tree.Set(_pAimMix, _aimBlend);
		_tree.Set(_pCrouchMix, _crouchBlend);
		_tree.Set(_pGripAdd, _gripBlend);
		_gripAimBlend = Mathf.MoveToward(_gripAimBlend, _aimBlend > 0.5f ? 1f : 0f, dt / Mathf.Max(GripAimBlendTime, 0.001f));
		_tree.Set(_pGripAimBlend, _gripAimBlend);

		bool isMovingNow = horizSpeed > 0.5f;
		if (_wasMoving && !isMovingNow && _simVel.LengthSquared() > 400f && IsOnFloor())
			TriggerLocoStop(running || sprinting ? RunEnd : WalkEnd);
		_wasMoving = isMovingNow;
	}

	private int _vmLastShotIndex;
	private bool _vmWasReloading, _vmWasInspecting;
	private void UpdateViewmodelMontages()
	{
		if (Movement == null)
			return;
		if (Movement.ShotIndex != _vmLastShotIndex)
		{
			bool didFire = Movement.ShotIndex > _vmLastShotIndex;
			_vmLastShotIndex = Movement.ShotIndex;
			if (didFire)
			{
				bool aimed = _aimBlend > 0.5f;
				PlayOneShot(Movement.CurrentMag <= 0 ? FireEmpty : aimed ? FireAimed : Movement.FireMode == 1 ? FireSemi : FireAuto, aimed);
				_currentWeapon?.Fire();
				AddRecoilKick(aimed ? RecoilImpulseAimed : RecoilImpulseHipfire);
			}
		}
		bool reloading = Movement.IsReloading;
		if (reloading && !_vmWasReloading)
		{
			bool aimed = _aimBlend > 0.5f;
			bool empty = Movement.CurrentMag <= 0;
			PlayOneShot(empty ? (aimed ? ReloadEmptyAimed : ReloadEmpty) : (aimed ? ReloadAimed : Reload), aimed);
			if (empty)
				_currentWeapon?.ReloadEmpty();
			else
			{ _currentWeapon?.Reload(); _currentWeapon?.DropMagazine(); }
		}
		_vmWasReloading = reloading;
		bool inspecting = Movement.IsInspecting;
		if (inspecting && !_vmWasInspecting)
		{ PlayOneShot(Inspect); _currentWeapon?.Inspect(); }
		_vmWasInspecting = inspecting;
	}

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

	private void StepViewmodelProcedural(float dt)
	{
		Vector3 vel = GlobalTransform.Basis.Inverse() * new Vector3(Velocity.X, 0f, Velocity.Z);

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

		if (!_bodyYawInit)
		{ _prevBodyYaw = _lookYaw; _prevBodyPitch = _lookPitch; _bodyYawInit = true; }
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

	private void ApplyViewmodelProcedural()
	{
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
		_viewSwayPos = movePos * adsMove;
		_viewSwayRotDeg = moveRotDeg * adsMove + lookRotDeg * adsLook;
	}

	private void UpdateBodyYaw()
	{
		if (!MouseLookEnabled)
			return;
		Node3D yawNode = _bodyNode ?? _cam?.GetParentOrNull<Node3D>();
		if (yawNode == null)
			return;
		Vector3 r = yawNode.Rotation;
		r.Y = _lookYaw;
		yawNode.Rotation = r;
	}

	private void RenderWorldCamera(float dt)
	{
		if (_cam == null)
			return;
		if (!_camRigCaptured)
		{
			_camRestLocal = _cam.Transform;
			if (_viewmodelCamAnchor != null)
				_eyeRest = _viewmodelCamAnchor.GlobalTransform;
			_cam.Fov = HipFov;
			_camRigCaptured = true;
		}
		Vector3 kick = _swayCurrent * Mathf.Lerp(1f, AimSwayMultiplier, _aimBlend)
			+ _recoilCurrent * Mathf.Lerp(1f, AimRecoilMultiplier, _aimBlend);
		Transform3D bob = _viewmodelCamAnchor != null
			? _eyeRest.AffineInverse() * _viewmodelCamAnchor.GlobalTransform
			: Transform3D.Identity;
		Vector3 swayRot = _viewSwayRotDeg * ViewSwayWorldMul;
		Vector3 swayPos = _viewSwayPos * ViewSwayWorldMul;
		Basis look = Basis.FromEuler(new Vector3(
			Mathf.DegToRad(kick.X + swayRot.X),
			Mathf.DegToRad(swayRot.Y),
			Mathf.DegToRad(kick.Z + swayRot.Z)));
		_cam.Transform = _camRestLocal * new Transform3D(look, swayPos) * bob;
		_cam.Fov = Mathf.Lerp(_cam.Fov, Mathf.Lerp(HipFov, AimFov, _aimBlend), Mathf.Clamp(dt * AimBlendSpeed, 0f, 1f));
	}

	private void RenderFpsCamera()
	{
		if (_viewmodelCam == null || _viewmodelCamAnchor == null)
			return;
		Transform3D sway = new(
			Basis.FromEuler(new Vector3(Mathf.DegToRad(_viewSwayRotDeg.X), Mathf.DegToRad(_viewSwayRotDeg.Y), Mathf.DegToRad(_viewSwayRotDeg.Z))),
			_viewSwayPos);
		_viewmodelCam.GlobalTransform = _viewmodelCamAnchor.GlobalTransform * sway;
		if (_cam != null)
			_viewmodelCam.Fov = _cam.Fov;
	}

	private PostProcessEffect _cachedPostFx;
	private bool _postFxLookupDone;
	private ShaderMaterial _viewmodelBlurMat;
	private bool _viewmodelBlurLookupDone;
	private static readonly StringName _pAdsBlendShader = "ads_blend";

	/// <summary>Fades a COD-style ADS depth-of-field in: the world camera focuses far (background softens while
	/// the mid-range target stays sharp) via CameraAttributes DOF, while the weapon is blurred by the 2D
	/// viewmodel_ads_blur shader — CameraAttributes DOF does not render in the weapon's transparent_bg
	/// SubViewport. Also feeds AdsBlend into the screen-space post-FX (world Compositor PostProcessEffect + the
	/// FSR2 PostCanvasFx path). Ported from the retired LocalAnimation node; runs Local-only via _Process.</summary>
	private void UpdateAdsPostFx()
	{
		float adsBlend = _aimBlend;
		float dof = Settings.AdsDepthOfField ? adsBlend : 0f;
		ApplyViewmodelAdsBlur(dof);
		ApplyWorldAdsDof(Mathf.Lerp(0f, 0.04f, dof));

		if (!_postFxLookupDone)
		{
			_postFxLookupDone = true;
			foreach (Node n in GetTree().Root.FindChildren("*", "WorldEnvironment", true, false))
			{
				if (n is WorldEnvironment we && !ViewmodelMotionBlur.IsViewmodelEnvironment(we) && we.Compositor is Compositor c)
					foreach (CompositorEffect e in c.CompositorEffects)
						if (e is PostProcessEffect ppe)
						{ _cachedPostFx = ppe; break; }
				if (_cachedPostFx != null)
					break;
			}
		}
		if (_cachedPostFx != null)
			_cachedPostFx.AdsBlend = adsBlend;
		if (PostCanvasFx.Instance != null)
			PostCanvasFx.Instance.AdsBlend = adsBlend;
		// Feed the same ADS vignette boost into the per-viewmodel post-FX so the weapon edges
		// darken on aim consistently with the world.
		if (ViewmodelMotionBlur.Effect != null)
			ViewmodelMotionBlur.Effect.AdsBlend = adsBlend;
	}

	/// <summary>Drives the viewmodel_ads_blur shader on the weapon SubViewportContainer — a 2D pseudo-DOF that
	/// keeps the iron-sight focus zone sharp and blurs the rest of the weapon. This is the only way to blur the
	/// weapon: CameraAttributes DOF does not render in its transparent_bg SubViewport.</summary>
	private void ApplyViewmodelAdsBlur(float blend)
	{
		if (!_viewmodelBlurLookupDone)
		{
			_viewmodelBlurLookupDone = true;
			if (_viewmodelLayer != null)
				foreach (Node n in _viewmodelLayer.FindChildren("viewmodel_container", "SubViewportContainer", true, false))
					if (n is SubViewportContainer svc && svc.Material is ShaderMaterial sm)
					{ _viewmodelBlurMat = sm; break; }
		}
		_viewmodelBlurMat?.SetShaderParameter(_pAdsBlendShader, blend);
	}

	/// <summary>COD-style world DOF for ADS: the mid-range target stays sharp while the far background softens.
	/// Far DOF stays permanently enabled; only the amount fades with ADS so there is no per-frame toggle.</summary>
	private void ApplyWorldAdsDof(float amount)
	{
		if (_cam?.Attributes is not CameraAttributesPractical a)
			return;
		a.DofBlurNearEnabled = false;
		a.DofBlurFarEnabled = true;
		a.DofBlurFarDistance = 35.0f;
		a.DofBlurFarTransition = 30.0f;
		a.DofBlurAmount = amount;
	}

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


	private void PlayOneShot(string anim, bool aimed = false)
	{
		if (string.IsNullOrEmpty(anim) || _tree == null || _actionAnim == null || !_player.HasAnimation(anim))
			return;
		string actionRef = aimed ? ActionRefAim : ActionRefIdle;
		if (_actionRefNode != null)
			_actionRefNode.Animation = actionRef;
		if (_actionRef2Node != null)
			_actionRef2Node.Animation = actionRef;
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

	protected override void ApplyEditorPreview(float dt = 0f)
	{
		var player = GetNodeOrNull<AnimationPlayer>(CharacterAnimationPath);
		var tree = GetNodeOrNull<AnimationTree>(FpsTreePath);
		if (player == null || tree == null)
			return;

		_leftHandFabrik ??= GetNodeOrNull<Node3D>(LeftHandFabrikPath);
		_rightHandFabrik ??= GetNodeOrNull<Node3D>(RightHandFabrikPath);
		_currentWeapon ??= GetNodeOrNull<WeaponAnimation>(CurrentWeaponPath);   // editor: read ADS/recoil from the weapon

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

		tree.Set(_pAimMix, _aimBlend);
		tree.Set(_pStandSprint, _sprintAmt);
		tree.Set(_pStandRun, Mathf.Max(_runAmt, _sprintAmt));
		tree.Set(_pCrouchMix, _crouchBlend);
		tree.Set(_pStandWalk, _simVel);
		tree.Set(_pAimLoco, _simVel);
		tree.Set(_pCrouchLoco, _simVel);
		var editorFastMovement = _sprintAmt > 0.05f || _runAmt > 0.5f;
		float gripAmt = _grip != GripType.Standard && !editorFastMovement ? 1f : 0f;
		tree.Set(_pGripAdd, gripAmt);
		tree.Set(_pGripAimBlend, _aimBlend);
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

	private void UpdateAdsCrosshair()
	{
		if (_adsTestMode && !_adsTestPrev)
			SpawnAdsCrosshair();
		else if (!_adsTestMode && _adsTestPrev)
			DespawnAdsCrosshair();
		_adsTestPrev = _adsTestMode;
		if (_adsTestMode)
			PoseAdsCrosshair();
	}

	private void SpawnAdsCrosshair()
	{
		Camera3D cam = _viewmodelCam ?? _cam;
		if (cam == null || _adsMarker != null)
			return;
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
		if (_adsMarker != null)
		{ _adsMarker.Position = pos; if (_adsMarker.Mesh is SphereMesh s) { s.Radius = t; s.Height = t * 2f; } SetCrosshairColor(_adsMarker); }
		if (_adsLineH != null)
		{ _adsLineH.Position = pos; if (_adsLineH.Mesh is BoxMesh b) b.Size = new Vector3(100f, t, t); SetCrosshairColor(_adsLineH); }
		if (_adsLineV != null)
		{ _adsLineV.Position = pos; if (_adsLineV.Mesh is BoxMesh b) b.Size = new Vector3(t, 100f, t); SetCrosshairColor(_adsLineV); }
	}

	private void SetCrosshairColor(MeshInstance3D mi) { if (mi.MaterialOverride is StandardMaterial3D m) m.AlbedoColor = AdsCalibrationColor; }

	private void DespawnAdsCrosshair()
	{
		_adsMarker?.QueueFree();
		_adsLineH?.QueueFree();
		_adsLineV?.QueueFree();
		_adsMarker = _adsLineH = _adsLineV = null;
	}

	// ── Local-only view state (relocated from NetworkPlayer; referenced only by this partial) ──
	private Vector2 _lookDelta;
	private float _crouchBlend;
	private float _cantedBlend;
	private Vector3 _swayCurrent;
	private Vector3 _recoilVel;
	private Transform3D _camRestLocal;
	private Transform3D _eyeRest;
	private bool _camRigCaptured;
	private bool _adsTestPrev;
	private MeshInstance3D _adsMarker, _adsLineH, _adsLineV;
	private float _gripAimBlend;
	private bool _montageActive;
	private GripType _pendingGrip;
	private float _gripSwitchDelay = -1f;
	private bool _gripChangeActive;
	private float _gripBlend;
	private bool _editorTreeReady;
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
}
