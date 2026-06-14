# NetworkPlayer

Shared player simulation: movement, hitscan, mantle, crouch, footsteps, grenades, plus puppet/server visual hooks. `LocalPlayer` derives from this and adds the local-only camera, mouse-look and aim-guide logic.

## Properties

| Name | Summary |
|------|---------|
| `ActiveSlot` | Active weapon slot (0 = weapon, 1 = grenade) — used by the HUD. |
| `AimModifier` | TpsAimModifier child under the skeleton; drives the spine twist/pitch for the TPS body aim pose (server + remote). Auto-created in `_Ready` when absent. |
| `AuthorityPosition` | Authority position for snapshots/reconciliation — always the real physics state, never the visually lerped _Process value (which would drift during the inter-tick window). |
| `FootstepLogic` | Public getter — netcode reads the footstep phase for the snapshot. |
| `GrenadeCharge` | Live grenade charge 0..1 while the throw key is held — used by the HUD. |
| `IsDead` | Death state: no movement, no collision, no shooting. Set by NetServer on the HP=0 trigger and cleared on respawn. Uses the same collision-zero logic as `IsFrozen`. |
| `IsFrozen` | Frozen state (reconnect pool): _PhysicsProcess returns immediately and the pose stays. CollisionLayer/Mask are nulled so live players do not get stuck on the ghost body. |
| `IsServerAuthority` | True when this node broadcasts server-authoritative events (shot, footstep, jump, land, hit). |
| `NeedsHitboxRig` | Whether this driver builds a hitbox rig. ServerPlayer needs it for hit-reg, the puppet for the debug overlay + casing self-exclude; LocalPlayer overrides to false. |
| `NeedsReload` | True iff the magazine is empty and not already mid-reload. Drives the bot's ReloadPressed input via `UpdateBotInputs`. |
| `PreMoveVelocityY` | Vertical velocity captured before MoveAndSlide. Used for land-impact scaling. |

## Fields

| Name | Summary |
|------|---------|
| `AdsFovDesignBase` | ADS FOV is relative to the base FOV: per-weapon AimFov is authored against a 100° base and scaled by `HipFov`, so the zoom factor stays constant at any FOV setting. |
| `BoneHistory` | Bone-pose history for lag-comp. Only initialised on the ServerAgent. |
| `IsPuppet` | True when this NetworkPlayer instance is a puppet visual — set externally by the `PuppetPlayer` wrapper before AddChild. Stays mutable because the wrapper owns the flag. |
| `LastAppliedInputTick` | Tick index of the last consumed input. Sent back to the client as ackedTick for reconciliation. |
| `MethodName.AddRecoilKick` | Cached name for the 'AddRecoilKick' method. |
| `MethodName.ApplyCrouchHeight` | Cached name for the 'ApplyCrouchHeight' method. |
| `MethodName.ApplyEditorPreview` | Cached name for the 'ApplyEditorPreview' method. |
| `MethodName.ApplyModeVisibility` | Cached name for the 'ApplyModeVisibility' method. |
| `MethodName.ApplyViewMode` | Cached name for the 'ApplyViewMode' method. |
| `MethodName.ApplyViewmodelLayer` | Cached name for the 'ApplyViewmodelLayer' method. |
| `MethodName.AssignTreeAnimations` | Cached name for the 'AssignTreeAnimations' method. |
| `MethodName.BuildAnimationEnumHint` | Cached name for the 'BuildAnimationEnumHint' method. |
| `MethodName.BuildAnimationTree` | Cached name for the 'BuildAnimationTree' method. |
| `MethodName.BuildTpsTree` | Cached name for the 'BuildTpsTree' method. |
| `MethodName.DisableExpensiveSubtreeProcessing` | Cached name for the 'DisableExpensiveSubtreeProcessing' method. |
| `MethodName.EditorRebuildTree` | Cached name for the 'EditorRebuildTree' method. |
| `MethodName.FireTpsAction` | Cached name for the 'FireTpsAction' method. |
| `MethodName.FixedTick` | Cached name for the 'FixedTick' method. |
| `MethodName.GetAnimationEnumHint` | Cached name for the 'GetAnimationEnumHint' method. |
| `MethodName.GetHitboxRig` | Cached name for the 'GetHitboxRig' method. |
| `MethodName.GetTpsAnimationEnumHint` | Cached name for the 'GetTpsAnimationEnumHint' method. |
| `MethodName.HandleFootsteps` | Cached name for the 'HandleFootsteps' method. |
| `MethodName.HandleGrenades` | Cached name for the 'HandleGrenades' method. |
| `MethodName.HandleHitscan` | Cached name for the 'HandleHitscan' method. |
| `MethodName.HandleJumpAnimation` | Cached name for the 'HandleJumpAnimation' method. |
| `MethodName.HandleLandingDetection` | Cached name for the 'HandleLandingDetection' method. |
| `MethodName.HandleWeaponAudio` | Cached name for the 'HandleWeaponAudio' method. |
| `MethodName.OnDropMagEvent` | Cached name for the 'OnDropMagEvent' method. |
| `MethodName.OnJumpEvent` | Cached name for the 'OnJumpEvent' method. |
| `MethodName.OnLandEvent` | Cached name for the 'OnLandEvent' method. |
| `MethodName.OnSimReady` | Cached name for the 'OnSimReady' method. |
| `MethodName.OnTickApplied` | Cached name for the 'OnTickApplied' method. |
| `MethodName.PreWarmAnimationOneShots` | Cached name for the 'PreWarmAnimationOneShots' method. |
| `MethodName.PushBoneHistory` | Cached name for the 'PushBoneHistory' method. |
| `MethodName.ResetInterpToCurrentPos` | Cached name for the 'ResetInterpToCurrentPos' method. |
| `MethodName.ResolveActiveSlot` | Cached name for the 'ResolveActiveSlot' method. |
| `MethodName.ResolveShot` | Cached name for the 'ResolveShot' method. |
| `MethodName.ResolveWeaponPlayers` | Cached name for the 'ResolveWeaponPlayers' method. |
| `MethodName.SetRenderLayersRecursive` | Cached name for the 'SetRenderLayersRecursive' method. |
| `MethodName.SetupCapsule` | Cached name for the 'SetupCapsule' method. |
| `MethodName.SetupHeadPitch` | Cached name for the 'SetupHeadPitch' method. |
| `MethodName.SetupSim` | Cached name for the 'SetupSim' method. |
| `MethodName.SetupTpsAimModifier` | Cached name for the 'SetupTpsAimModifier' method. |
| `MethodName.StepMantle` | Cached name for the 'StepMantle' method. |
| `MethodName.ThrowGrenade` | Cached name for the 'ThrowGrenade' method. |
| `MethodName.TryMantle` | Cached name for the 'TryMantle' method. |
| `MethodName.TryStepUp` | Cached name for the 'TryStepUp' method. |
| `MethodName.UpdateActiveWeapon` | Cached name for the 'UpdateActiveWeapon' method. |
| `MethodName.UpdateGripLayer` | Cached name for the 'UpdateGripLayer' method. |
| `MethodName.UpdateTpsAimPose` | Cached name for the 'UpdateTpsAimPose' method. |
| `MethodName.UpdateTpsBody` | Cached name for the 'UpdateTpsBody' method. |
| `MethodName.UpdateTpsBodyAim` | Cached name for the 'UpdateTpsBodyAim' method. |
| `MethodName.UpdateTpsCamera` | Cached name for the 'UpdateTpsCamera' method. |
| `MethodName.UpdateTpsMontages` | Cached name for the 'UpdateTpsMontages' method. |
| `MethodName.WarmUpAudio` | Cached name for the 'WarmUpAudio' method. |
| `MethodName._PhysicsProcess` | Cached name for the '_PhysicsProcess' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `MethodName._ValidateProperty` | Cached name for the '_ValidateProperty' method. |
| `NetInputSource` | When set, the movement sim reads from this instead of the live input singleton. ServerPlayer: filled per tick by NetServer. ServerBotPlayer: set once at spawn. |
| `Prediction` | Per-tick prediction buffer for reconciliation. Filled only for IsLocalPlayer. |
| `PropertyName.ActionRefAim` | Cached name for the 'ActionRefAim' field. |
| `PropertyName.ActionRefIdle` | Cached name for the 'ActionRefIdle' field. |
| `PropertyName.ActiveCamera` | Cached name for the 'ActiveCamera' property. |
| `PropertyName.ActiveSlot` | Cached name for the 'ActiveSlot' property. |
| `PropertyName.AdsCalibrationColor` | Cached name for the 'AdsCalibrationColor' property. |
| `PropertyName.AdsCalibrationDistance` | Cached name for the 'AdsCalibrationDistance' property. |
| `PropertyName.AdsCalibrationSize` | Cached name for the 'AdsCalibrationSize' property. |
| `PropertyName.AdsOffsetPosition` | Cached name for the 'AdsOffsetPosition' property. |
| `PropertyName.AdsOffsetRotation` | Cached name for the 'AdsOffsetRotation' property. |
| `PropertyName.AdsTestMode` | Cached name for the 'AdsTestMode' property. |
| `PropertyName.AimBlendSpeed` | Cached name for the 'AimBlendSpeed' property. |
| `PropertyName.AimFov` | Cached name for the 'AimFov' property. |
| `PropertyName.AimModifier` | Cached name for the 'AimModifier' property. |
| `PropertyName.AimPose` | Cached name for the 'AimPose' field. |
| `PropertyName.AimPoseGripAngled` | Cached name for the 'AimPoseGripAngled' field. |
| `PropertyName.AimPoseGripVertical` | Cached name for the 'AimPoseGripVertical' field. |
| `PropertyName.AimRecoilMultiplier` | Cached name for the 'AimRecoilMultiplier' property. |
| `PropertyName.AimSwayMultiplier` | Cached name for the 'AimSwayMultiplier' field. |
| `PropertyName.AuthorityPosition` | Cached name for the 'AuthorityPosition' property. |
| `PropertyName.BodyCollision` | Cached name for the 'BodyCollision' field. |
| `PropertyName.BodyYawLagEnabled` | Cached name for the 'BodyYawLagEnabled' field. |
| `PropertyName.BodyYawLagMax` | Cached name for the 'BodyYawLagMax' field. |
| `PropertyName.BodyYawLagSmoothing` | Cached name for the 'BodyYawLagSmoothing' field. |
| `PropertyName.BodyYawLagStrength` | Cached name for the 'BodyYawLagStrength' field. |
| `PropertyName.CanFire` | Cached name for the 'CanFire' field. |
| `PropertyName.CantedOffsetPosition` | Cached name for the 'CantedOffsetPosition' property. |
| `PropertyName.CantedOffsetRotation` | Cached name for the 'CantedOffsetRotation' property. |
| `PropertyName.CantedTestMode` | Cached name for the 'CantedTestMode' property. |
| `PropertyName.CapsuleRadius` | Cached name for the 'CapsuleRadius' field. |
| `PropertyName.CharacterAnimationPath` | Cached name for the 'CharacterAnimationPath' field. |
| `PropertyName.ClearJamMagSwipe` | Cached name for the 'ClearJamMagSwipe' field. |
| `PropertyName.ClearJamRack` | Cached name for the 'ClearJamRack' field. |
| `PropertyName.CrouchCameraDrop` | Cached name for the 'CrouchCameraDrop' field. |
| `PropertyName.CrouchEyeHeight` | Cached name for the 'CrouchEyeHeight' field. |
| `PropertyName.CrouchHeight` | Cached name for the 'CrouchHeight' field. |
| `PropertyName.CrouchOffsetPosition` | Cached name for the 'CrouchOffsetPosition' property. |
| `PropertyName.CrouchOffsetRotation` | Cached name for the 'CrouchOffsetRotation' property. |
| `PropertyName.CrouchTestMode` | Cached name for the 'CrouchTestMode' property. |
| `PropertyName.CurrentGameMode` | Cached name for the 'CurrentGameMode' field. |
| `PropertyName.CurrentGrip` | Cached name for the 'CurrentGrip' property. |
| `PropertyName.CurrentTick` | Cached name for the 'CurrentTick' property. |
| `PropertyName.CurrentWeaponPath` | Cached name for the 'CurrentWeaponPath' field. |
| `PropertyName.DirectionLeanDamping` | Cached name for the 'DirectionLeanDamping' field. |
| `PropertyName.DirectionLeanEnabled` | Cached name for the 'DirectionLeanEnabled' field. |
| `PropertyName.DirectionLeanStiffness` | Cached name for the 'DirectionLeanStiffness' field. |
| `PropertyName.Equip` | Cached name for the 'Equip' field. |
| `PropertyName.EquipQuick` | Cached name for the 'EquipQuick' field. |
| `PropertyName.FireAimed` | Cached name for the 'FireAimed' field. |
| `PropertyName.FireAuto` | Cached name for the 'FireAuto' field. |
| `PropertyName.FireEmpty` | Cached name for the 'FireEmpty' field. |
| `PropertyName.FireModeSwitch` | Cached name for the 'FireModeSwitch' field. |
| `PropertyName.FireSemi` | Cached name for the 'FireSemi' field. |
| `PropertyName.FloorMaxAngleDeg` | Cached name for the 'FloorMaxAngleDeg' field. |
| `PropertyName.FloorSnapDist` | Cached name for the 'FloorSnapDist' field. |
| `PropertyName.FootstepAudioPath` | Cached name for the 'FootstepAudioPath' field. |
| `PropertyName.ForwardLeanPitch` | Cached name for the 'ForwardLeanPitch' field. |
| `PropertyName.ForwardLeanPosDown` | Cached name for the 'ForwardLeanPosDown' field. |
| `PropertyName.ForwardLeanPosForward` | Cached name for the 'ForwardLeanPosForward' field. |
| `PropertyName.FpsTreePath` | Cached name for the 'FpsTreePath' field. |
| `PropertyName.GrenadeCharge` | Cached name for the 'GrenadeCharge' property. |
| `PropertyName.GrenadeThrowQuick` | Cached name for the 'GrenadeThrowQuick' field. |
| `PropertyName.GrenadeThrowSpeed` | Cached name for the 'GrenadeThrowSpeed' field. |
| `PropertyName.GripAimBlendTime` | Cached name for the 'GripAimBlendTime' field. |
| `PropertyName.GripChange` | Cached name for the 'GripChange' field. |
| `PropertyName.GripChangeNotifyTime` | Cached name for the 'GripChangeNotifyTime' field. |
| `PropertyName.GripPoseBlendSpeed` | Cached name for the 'GripPoseBlendSpeed' field. |
| `PropertyName.HeadCameraPath` | Cached name for the 'HeadCameraPath' field. |
| `PropertyName.HeadPitch` | Cached name for the 'HeadPitch' field. |
| `PropertyName.HealSyringe` | Cached name for the 'HealSyringe' field. |
| `PropertyName.HipFov` | Cached name for the 'HipFov' property. |
| `PropertyName.HitscanMask` | Cached name for the 'HitscanMask' field. |
| `PropertyName.HitscanRange` | Cached name for the 'HitscanRange' field. |
| `PropertyName.Holster` | Cached name for the 'Holster' field. |
| `PropertyName.IdleAimed` | Cached name for the 'IdleAimed' field. |
| `PropertyName.IdleCrouched` | Cached name for the 'IdleCrouched' field. |
| `PropertyName.IdlePoseGripAngled` | Cached name for the 'IdlePoseGripAngled' field. |
| `PropertyName.IdlePoseGripVertical` | Cached name for the 'IdlePoseGripVertical' field. |
| `PropertyName.IdlePoseStanding` | Cached name for the 'IdlePoseStanding' field. |
| `PropertyName.IdleStanding` | Cached name for the 'IdleStanding' field. |
| `PropertyName.IkEnabled` | Cached name for the 'IkEnabled' field. |
| `PropertyName.InertiaTiltMax` | Cached name for the 'InertiaTiltMax' field. |
| `PropertyName.InertiaTiltRecovery` | Cached name for the 'InertiaTiltRecovery' field. |
| `PropertyName.InertiaTiltStrength` | Cached name for the 'InertiaTiltStrength' field. |
| `PropertyName.Inspect` | Cached name for the 'Inspect' field. |
| `PropertyName.InspectEmpty` | Cached name for the 'InspectEmpty' field. |
| `PropertyName.InteractGrab` | Cached name for the 'InteractGrab' field. |
| `PropertyName.InteractPunch` | Cached name for the 'InteractPunch' field. |
| `PropertyName.InteractPush` | Cached name for the 'InteractPush' field. |
| `PropertyName.IsAiming` | Cached name for the 'IsAiming' property. |
| `PropertyName.IsCantedAiming` | Cached name for the 'IsCantedAiming' property. |
| `PropertyName.IsCrouching` | Cached name for the 'IsCrouching' property. |
| `PropertyName.IsDead` | Cached name for the 'IsDead' property. |
| `PropertyName.IsFrozen` | Cached name for the 'IsFrozen' property. |
| `PropertyName.IsLocalPlayer` | Cached name for the 'IsLocalPlayer' property. |
| `PropertyName.IsPuppet` | Cached name for the 'IsPuppet' field. |
| `PropertyName.IsRunning` | Cached name for the 'IsRunning' property. |
| `PropertyName.IsServerAgent` | Cached name for the 'IsServerAgent' property. |
| `PropertyName.IsServerAuthority` | Cached name for the 'IsServerAuthority' property. |
| `PropertyName.IsSprinting` | Cached name for the 'IsSprinting' property. |
| `PropertyName.JumpEnd` | Cached name for the 'JumpEnd' field. |
| `PropertyName.JumpFallingLoop` | Cached name for the 'JumpFallingLoop' field. |
| `PropertyName.JumpFull` | Cached name for the 'JumpFull' field. |
| `PropertyName.JumpStart` | Cached name for the 'JumpStart' field. |
| `PropertyName.LastAppliedInputTick` | Cached name for the 'LastAppliedInputTick' field. |
| `PropertyName.LeanReferenceSpeed` | Cached name for the 'LeanReferenceSpeed' field. |
| `PropertyName.LeftHandFabrikPath` | Cached name for the 'LeftHandFabrikPath' field. |
| `PropertyName.LocomotionSmoothing` | Cached name for the 'LocomotionSmoothing' field. |
| `PropertyName.MagCheck` | Cached name for the 'MagCheck' field. |
| `PropertyName.MagCheckAimed` | Cached name for the 'MagCheckAimed' field. |
| `PropertyName.MeleeBashForward` | Cached name for the 'MeleeBashForward' field. |
| `PropertyName.MeleeSwingLeft` | Cached name for the 'MeleeSwingLeft' field. |
| `PropertyName.MeleeSwingRight` | Cached name for the 'MeleeSwingRight' field. |
| `PropertyName.MouseInertiaEnabled` | Cached name for the 'MouseInertiaEnabled' field. |
| `PropertyName.MouseInertiaMaxPitch` | Cached name for the 'MouseInertiaMaxPitch' field. |
| `PropertyName.MouseInertiaMaxYaw` | Cached name for the 'MouseInertiaMaxYaw' field. |
| `PropertyName.MouseInertiaPitch` | Cached name for the 'MouseInertiaPitch' field. |
| `PropertyName.MouseInertiaRecovery` | Cached name for the 'MouseInertiaRecovery' field. |
| `PropertyName.MouseInertiaRollMul` | Cached name for the 'MouseInertiaRollMul' field. |
| `PropertyName.MouseInertiaSmoothingIn` | Cached name for the 'MouseInertiaSmoothingIn' field. |
| `PropertyName.MouseInertiaSmoothingOut` | Cached name for the 'MouseInertiaSmoothingOut' field. |
| `PropertyName.MouseInertiaYaw` | Cached name for the 'MouseInertiaYaw' field. |
| `PropertyName.MouseLookEnabled` | Cached name for the 'MouseLookEnabled' field. |
| `PropertyName.NeedsHitboxRig` | Cached name for the 'NeedsHitboxRig' property. |
| `PropertyName.NeedsReload` | Cached name for the 'NeedsReload' property. |
| `PropertyName.NetId` | Cached name for the 'NetId' field. |
| `PropertyName.PreMoveVelocityY` | Cached name for the 'PreMoveVelocityY' property. |
| `PropertyName.PuppetActiveSlot` | Cached name for the 'PuppetActiveSlot' field. |
| `PropertyName.PuppetIsAirborne` | Cached name for the 'PuppetIsAirborne' field. |
| `PropertyName.PuppetIsInspecting` | Cached name for the 'PuppetIsInspecting' field. |
| `PropertyName.PuppetIsReloading` | Cached name for the 'PuppetIsReloading' field. |
| `PropertyName.PuppetIsSprinting` | Cached name for the 'PuppetIsSprinting' field. |
| `PropertyName.PuppetSpineTwist` | Cached name for the 'PuppetSpineTwist' field. |
| `PropertyName.RebuildAnimationTree` | Cached name for the 'RebuildAnimationTree' property. |
| `PropertyName.RecoilDamping` | Cached name for the 'RecoilDamping' property. |
| `PropertyName.RecoilImpulseAimed` | Cached name for the 'RecoilImpulseAimed' property. |
| `PropertyName.RecoilImpulseHipfire` | Cached name for the 'RecoilImpulseHipfire' property. |
| `PropertyName.RecoilMass` | Cached name for the 'RecoilMass' property. |
| `PropertyName.RecoilMaxDegrees` | Cached name for the 'RecoilMaxDegrees' property. |
| `PropertyName.RecoilStiffness` | Cached name for the 'RecoilStiffness' property. |
| `PropertyName.Reload` | Cached name for the 'Reload' field. |
| `PropertyName.ReloadAimed` | Cached name for the 'ReloadAimed' field. |
| `PropertyName.ReloadEmpty` | Cached name for the 'ReloadEmpty' field. |
| `PropertyName.ReloadEmptyAimed` | Cached name for the 'ReloadEmptyAimed' field. |
| `PropertyName.ReloadQuick` | Cached name for the 'ReloadQuick' field. |
| `PropertyName.ReloadQuickAimed` | Cached name for the 'ReloadQuickAimed' field. |
| `PropertyName.RightHandFabrikPath` | Cached name for the 'RightHandFabrikPath' field. |
| `PropertyName.RunEnd` | Cached name for the 'RunEnd' field. |
| `PropertyName.RunForward` | Cached name for the 'RunForward' field. |
| `PropertyName.SimulatedVelocity` | Cached name for the 'SimulatedVelocity' property. |
| `PropertyName.SpeedBlendRate` | Cached name for the 'SpeedBlendRate' field. |
| `PropertyName.SprintForward` | Cached name for the 'SprintForward' field. |
| `PropertyName.StandEyeHeight` | Cached name for the 'StandEyeHeight' field. |
| `PropertyName.StandHeight` | Cached name for the 'StandHeight' field. |
| `PropertyName.StepMaxHeight` | Cached name for the 'StepMaxHeight' field. |
| `PropertyName.StrafeLeanPos` | Cached name for the 'StrafeLeanPos' field. |
| `PropertyName.StrafeLeanRoll` | Cached name for the 'StrafeLeanRoll' field. |
| `PropertyName.SwayLookFactor` | Cached name for the 'SwayLookFactor' field. |
| `PropertyName.SwayMaxDegrees` | Cached name for the 'SwayMaxDegrees' field. |
| `PropertyName.SwaySpringSpeed` | Cached name for the 'SwaySpringSpeed' field. |
| `PropertyName.TpsAimBoneName` | Cached name for the 'TpsAimBoneName' field. |
| `PropertyName.TpsAimFov` | Cached name for the 'TpsAimFov' property. |
| `PropertyName.TpsAimPitchScale` | Cached name for the 'TpsAimPitchScale' field. |
| `PropertyName.TpsAimPose` | Cached name for the 'TpsAimPose' field. |
| `PropertyName.TpsAimPoseCanted` | Cached name for the 'TpsAimPoseCanted' field. |
| `PropertyName.TpsAimPoseGripAngled` | Cached name for the 'TpsAimPoseGripAngled' field. |
| `PropertyName.TpsAimPoseGripVertical` | Cached name for the 'TpsAimPoseGripVertical' field. |
| `PropertyName.TpsAnimTree` | Cached name for the 'TpsAnimTree' field. |
| `PropertyName.TpsAnimationPath` | Cached name for the 'TpsAnimationPath' field. |
| `PropertyName.TpsBasePitchDeg` | Cached name for the 'TpsBasePitchDeg' field. |
| `PropertyName.TpsBodyTurnRate` | Cached name for the 'TpsBodyTurnRate' field. |
| `PropertyName.TpsBodyYawOffsetDeg` | Cached name for the 'TpsBodyYawOffsetDeg' field. |
| `PropertyName.TpsCamCollisionMask` | Cached name for the 'TpsCamCollisionMask' field. |
| `PropertyName.TpsCamSmoothRate` | Cached name for the 'TpsCamSmoothRate' field. |
| `PropertyName.TpsCamWallMargin` | Cached name for the 'TpsCamWallMargin' field. |
| `PropertyName.TpsCameraOffset` | Cached name for the 'TpsCameraOffset' field. |
| `PropertyName.TpsCameraPath` | Cached name for the 'TpsCameraPath' field. |
| `PropertyName.TpsClearJamMagSwipe` | Cached name for the 'TpsClearJamMagSwipe' field. |
| `PropertyName.TpsClearJamRack` | Cached name for the 'TpsClearJamRack' field. |
| `PropertyName.TpsEquip` | Cached name for the 'TpsEquip' field. |
| `PropertyName.TpsEquipQuick` | Cached name for the 'TpsEquipQuick' field. |
| `PropertyName.TpsFire` | Cached name for the 'TpsFire' field. |
| `PropertyName.TpsFireEmpty` | Cached name for the 'TpsFireEmpty' field. |
| `PropertyName.TpsFireModeSwitch` | Cached name for the 'TpsFireModeSwitch' field. |
| `PropertyName.TpsGrenadeThrowQuick` | Cached name for the 'TpsGrenadeThrowQuick' field. |
| `PropertyName.TpsHealSyringe` | Cached name for the 'TpsHealSyringe' field. |
| `PropertyName.TpsHolster` | Cached name for the 'TpsHolster' field. |
| `PropertyName.TpsIdleLoop` | Cached name for the 'TpsIdleLoop' field. |
| `PropertyName.TpsIdlePose` | Cached name for the 'TpsIdlePose' field. |
| `PropertyName.TpsIdlePoseGripAngled` | Cached name for the 'TpsIdlePoseGripAngled' field. |
| `PropertyName.TpsIdlePoseGripVertical` | Cached name for the 'TpsIdlePoseGripVertical' field. |
| `PropertyName.TpsInspect` | Cached name for the 'TpsInspect' field. |
| `PropertyName.TpsInspectEmpty` | Cached name for the 'TpsInspectEmpty' field. |
| `PropertyName.TpsInteractGrab` | Cached name for the 'TpsInteractGrab' field. |
| `PropertyName.TpsInteractPunch` | Cached name for the 'TpsInteractPunch' field. |
| `PropertyName.TpsInteractPush` | Cached name for the 'TpsInteractPush' field. |
| `PropertyName.TpsMagCheck` | Cached name for the 'TpsMagCheck' field. |
| `PropertyName.TpsMagCheckAimed` | Cached name for the 'TpsMagCheckAimed' field. |
| `PropertyName.TpsMeleeBashForward` | Cached name for the 'TpsMeleeBashForward' field. |
| `PropertyName.TpsMeleeSwingLeft` | Cached name for the 'TpsMeleeSwingLeft' field. |
| `PropertyName.TpsMeleeSwingRight` | Cached name for the 'TpsMeleeSwingRight' field. |
| `PropertyName.TpsPivotPath` | Cached name for the 'TpsPivotPath' field. |
| `PropertyName.TpsRecoilPitchScale` | Cached name for the 'TpsRecoilPitchScale' field. |
| `PropertyName.TpsReload` | Cached name for the 'TpsReload' field. |
| `PropertyName.TpsReloadAimed` | Cached name for the 'TpsReloadAimed' field. |
| `PropertyName.TpsReloadEmpty` | Cached name for the 'TpsReloadEmpty' field. |
| `PropertyName.TpsReloadEmptyAimed` | Cached name for the 'TpsReloadEmptyAimed' field. |
| `PropertyName.TpsReloadQuick` | Cached name for the 'TpsReloadQuick' field. |
| `PropertyName.TpsReloadQuickAimed` | Cached name for the 'TpsReloadQuickAimed' field. |
| `PropertyName.TpsSkeleton` | Cached name for the 'TpsSkeleton' field. |
| `PropertyName.TpsTransitionAimEnd` | Cached name for the 'TpsTransitionAimEnd' field. |
| `PropertyName.TpsTransitionAimStart` | Cached name for the 'TpsTransitionAimStart' field. |
| `PropertyName.TpsView` | Cached name for the 'TpsView' property. |
| `PropertyName.TpsVisual` | Cached name for the 'TpsVisual' field. |
| `PropertyName.TpsWeaponPath` | Cached name for the 'TpsWeaponPath' field. |
| `PropertyName.TpsZoomMax` | Cached name for the 'TpsZoomMax' field. |
| `PropertyName.TpsZoomMin` | Cached name for the 'TpsZoomMin' field. |
| `PropertyName.TpsZoomStep` | Cached name for the 'TpsZoomStep' field. |
| `PropertyName.TransitionCrouchEnd` | Cached name for the 'TransitionCrouchEnd' field. |
| `PropertyName.TransitionCrouchEndAimed` | Cached name for the 'TransitionCrouchEndAimed' field. |
| `PropertyName.TransitionCrouchStart` | Cached name for the 'TransitionCrouchStart' field. |
| `PropertyName.TransitionCrouchStartAimed` | Cached name for the 'TransitionCrouchStartAimed' field. |
| `PropertyName.TriggerDisciplineReady` | Cached name for the 'TriggerDisciplineReady' field. |
| `PropertyName.TriggerDisciplineSafePose` | Cached name for the 'TriggerDisciplineSafePose' field. |
| `PropertyName.UseAnimationTree` | Cached name for the 'UseAnimationTree' field. |
| `PropertyName.VelocityTiltEnabled` | Cached name for the 'VelocityTiltEnabled' field. |
| `PropertyName.ViewMode` | Cached name for the 'ViewMode' field. |
| `PropertyName.ViewSwayAdsLookMul` | Cached name for the 'ViewSwayAdsLookMul' field. |
| `PropertyName.ViewSwayAdsMul` | Cached name for the 'ViewSwayAdsMul' field. |
| `PropertyName.ViewSwayEnabled` | Cached name for the 'ViewSwayEnabled' field. |
| `PropertyName.ViewSwayWorldMul` | Cached name for the 'ViewSwayWorldMul' field. |
| `PropertyName.ViewmodelCameraAnchorPath` | Cached name for the 'ViewmodelCameraAnchorPath' field. |
| `PropertyName.ViewmodelCameraPath` | Cached name for the 'ViewmodelCameraPath' field. |
| `PropertyName.ViewmodelLayerPath` | Cached name for the 'ViewmodelLayerPath' field. |
| `PropertyName.ViewmodelMeshRootPath` | Cached name for the 'ViewmodelMeshRootPath' field. |
| `PropertyName.ViewmodelRenderLayer` | Cached name for the 'ViewmodelRenderLayer' field. |
| `PropertyName.WalkBackward` | Cached name for the 'WalkBackward' field. |
| `PropertyName.WalkBackwardAimed` | Cached name for the 'WalkBackwardAimed' field. |
| `PropertyName.WalkEnd` | Cached name for the 'WalkEnd' field. |
| `PropertyName.WalkForward` | Cached name for the 'WalkForward' field. |
| `PropertyName.WalkForwardAimed` | Cached name for the 'WalkForwardAimed' field. |
| `PropertyName.WalkStrafeLeft` | Cached name for the 'WalkStrafeLeft' field. |
| `PropertyName.WalkStrafeLeftAimed` | Cached name for the 'WalkStrafeLeftAimed' field. |
| `PropertyName.WalkStrafeRight` | Cached name for the 'WalkStrafeRight' field. |
| `PropertyName.WalkStrafeRightAimed` | Cached name for the 'WalkStrafeRightAimed' field. |
| `PropertyName.WeaponBoneModifierPath` | Cached name for the 'WeaponBoneModifierPath' field. |
| `PropertyName.WeaponRecoilKickback` | Cached name for the 'WeaponRecoilKickback' property. |
| `PropertyName.WeaponRecoilRotScale` | Cached name for the 'WeaponRecoilRotScale' property. |
| `PropertyName._actionAnim` | Cached name for the '_actionAnim' field. |
| `PropertyName._actionRef2Node` | Cached name for the '_actionRef2Node' field. |
| `PropertyName._actionRefNode` | Cached name for the '_actionRefNode' field. |
| `PropertyName._activeBleedRate` | Cached name for the '_activeBleedRate' field. |
| `PropertyName._activeSlot` | Cached name for the '_activeSlot' field. |
| `PropertyName._aimBlend` | Cached name for the '_aimBlend' field. |
| `PropertyName._animEnumHint` | Cached name for the '_animEnumHint' field. |
| `PropertyName._bodyNode` | Cached name for the '_bodyNode' field. |
| `PropertyName._bodyRest` | Cached name for the '_bodyRest' field. |
| `PropertyName._bodyRestCaptured` | Cached name for the '_bodyRestCaptured' field. |
| `PropertyName._bodyRestRot` | Cached name for the '_bodyRestRot' field. |
| `PropertyName._cam` | Cached name for the '_cam' field. |
| `PropertyName._cantedAim` | Cached name for the '_cantedAim' field. |
| `PropertyName._capsule` | Cached name for the '_capsule' field. |
| `PropertyName._correctionPending` | Cached name for the '_correctionPending' field. |
| `PropertyName._currentPhysicsPos` | Cached name for the '_currentPhysicsPos' field. |
| `PropertyName._currentWeapon` | Cached name for the '_currentWeapon' field. |
| `PropertyName._fireModeName` | Cached name for the '_fireModeName' field. |
| `PropertyName._fixedDt` | Cached name for the '_fixedDt' field. |
| `PropertyName._footstepAudio` | Cached name for the '_footstepAudio' field. |
| `PropertyName._fpsWeapon` | Cached name for the '_fpsWeapon' field. |
| `PropertyName._grip` | Cached name for the '_grip' field. |
| `PropertyName._gripChangeAnim` | Cached name for the '_gripChangeAnim' field. |
| `PropertyName._gripPose` | Cached name for the '_gripPose' field. |
| `PropertyName._gripPoseAim` | Cached name for the '_gripPoseAim' field. |
| `PropertyName._headBasePos` | Cached name for the '_headBasePos' field. |
| `PropertyName._hitboxRig` | Cached name for the '_hitboxRig' field. |
| `PropertyName._intervalStartBits` | Cached name for the '_intervalStartBits' field. |
| `PropertyName._intervalStartViewPitch` | Cached name for the '_intervalStartViewPitch' field. |
| `PropertyName._intervalStartViewYaw` | Cached name for the '_intervalStartViewYaw' field. |
| `PropertyName._isAiming` | Cached name for the '_isAiming' field. |
| `PropertyName._isCrouched` | Cached name for the '_isCrouched' field. |
| `PropertyName._isDead` | Cached name for the '_isDead' field. |
| `PropertyName._isFrozen` | Cached name for the '_isFrozen' field. |
| `PropertyName._isMantling` | Cached name for the '_isMantling' field. |
| `PropertyName._isReplaying` | Cached name for the '_isReplaying' field. |
| `PropertyName._lastFirePressUsec` | Cached name for the '_lastFirePressUsec' field. |
| `PropertyName._lastStepupBlockedLogMs` | Cached name for the '_lastStepupBlockedLogMs' field. |
| `PropertyName._lastStepupSuccessLogMs` | Cached name for the '_lastStepupSuccessLogMs' field. |
| `PropertyName._leftHandFabrik` | Cached name for the '_leftHandFabrik' field. |
| `PropertyName._liveBits` | Cached name for the '_liveBits' field. |
| `PropertyName._locoStopAnim` | Cached name for the '_locoStopAnim' field. |
| `PropertyName._lookPitch` | Cached name for the '_lookPitch' field. |
| `PropertyName._lookYaw` | Cached name for the '_lookYaw' field. |
| `PropertyName._magFill` | Cached name for the '_magFill' field. |
| `PropertyName._mantleReconcileBlockUntilTick` | Cached name for the '_mantleReconcileBlockUntilTick' field. |
| `PropertyName._mantleStart` | Cached name for the '_mantleStart' field. |
| `PropertyName._mantleTarget` | Cached name for the '_mantleTarget' field. |
| `PropertyName._mantleTimer` | Cached name for the '_mantleTimer' field. |
| `PropertyName._pendingThrowOrigin` | Cached name for the '_pendingThrowOrigin' field. |
| `PropertyName._pendingThrowValid` | Cached name for the '_pendingThrowValid' field. |
| `PropertyName._pendingThrowVel` | Cached name for the '_pendingThrowVel' field. |
| `PropertyName._player` | Cached name for the '_player' field. |
| `PropertyName._preMoveVelocityY` | Cached name for the '_preMoveVelocityY' field. |
| `PropertyName._prevPhysicsPos` | Cached name for the '_prevPhysicsPos' field. |
| `PropertyName._prevTickStartUsec` | Cached name for the '_prevTickStartUsec' field. |
| `PropertyName._rayQuery` | Cached name for the '_rayQuery' field. |
| `PropertyName._rayResult` | Cached name for the '_rayResult' field. |
| `PropertyName._recoilCurrent` | Cached name for the '_recoilCurrent' field. |
| `PropertyName._reconcileCountWindow` | Cached name for the '_reconcileCountWindow' field. |
| `PropertyName._reconcileWindowStartSec` | Cached name for the '_reconcileWindowStartSec' field. |
| `PropertyName._rightHandFabrik` | Cached name for the '_rightHandFabrik' field. |
| `PropertyName._runAmt` | Cached name for the '_runAmt' field. |
| `PropertyName._savedCollisionLayer` | Cached name for the '_savedCollisionLayer' field. |
| `PropertyName._savedCollisionLayerDead` | Cached name for the '_savedCollisionLayerDead' field. |
| `PropertyName._savedCollisionMask` | Cached name for the '_savedCollisionMask' field. |
| `PropertyName._savedCollisionMaskDead` | Cached name for the '_savedCollisionMaskDead' field. |
| `PropertyName._selfExclude` | Cached name for the '_selfExclude' field. |
| `PropertyName._serverBodyYawInitialized` | Cached name for the '_serverBodyYawInitialized' field. |
| `PropertyName._serverSmoothedBodyYaw` | Cached name for the '_serverSmoothedBodyYaw' field. |
| `PropertyName._simVel` | Cached name for the '_simVel' field. |
| `PropertyName._smoothedHorizSpeed` | Cached name for the '_smoothedHorizSpeed' field. |
| `PropertyName._sprintAmt` | Cached name for the '_sprintAmt' field. |
| `PropertyName._stepupLastBlockedPos` | Cached name for the '_stepupLastBlockedPos' field. |
| `PropertyName._stepupLastBlockedTick` | Cached name for the '_stepupLastBlockedTick' field. |
| `PropertyName._tickStartUsec` | Cached name for the '_tickStartUsec' field. |
| `PropertyName._ticksSinceSpawn` | Cached name for the '_ticksSinceSpawn' field. |
| `PropertyName._tpsActionAnim` | Cached name for the '_tpsActionAnim' field. |
| `PropertyName._tpsActionRef2Node` | Cached name for the '_tpsActionRef2Node' field. |
| `PropertyName._tpsActionRefNode` | Cached name for the '_tpsActionRefNode' field. |
| `PropertyName._tpsAimModifier` | Cached name for the '_tpsAimModifier' field. |
| `PropertyName._tpsAimPoseNode` | Cached name for the '_tpsAimPoseNode' field. |
| `PropertyName._tpsAnimEnumHint` | Cached name for the '_tpsAnimEnumHint' field. |
| `PropertyName._tpsCam` | Cached name for the '_tpsCam' field. |
| `PropertyName._tpsPivot` | Cached name for the '_tpsPivot' field. |
| `PropertyName._tpsPlayer` | Cached name for the '_tpsPlayer' field. |
| `PropertyName._tpsRayQuery` | Cached name for the '_tpsRayQuery' field. |
| `PropertyName._tpsRayResult` | Cached name for the '_tpsRayResult' field. |
| `PropertyName._tpsSelfExclude` | Cached name for the '_tpsSelfExclude' field. |
| `PropertyName._tpsTree` | Cached name for the '_tpsTree' field. |
| `PropertyName._tpsWasInspecting` | Cached name for the '_tpsWasInspecting' field. |
| `PropertyName._tpsWasReloading` | Cached name for the '_tpsWasReloading' field. |
| `PropertyName._tpsWeapon` | Cached name for the '_tpsWeapon' field. |
| `PropertyName._tpsZoomDist` | Cached name for the '_tpsZoomDist' field. |
| `PropertyName._tree` | Cached name for the '_tree' field. |
| `PropertyName._viewmodelCam` | Cached name for the '_viewmodelCam' field. |
| `PropertyName._viewmodelCamAnchor` | Cached name for the '_viewmodelCamAnchor' field. |
| `PropertyName._viewmodelLayer` | Cached name for the '_viewmodelLayer' field. |
| `PropertyName._visualErrorOffset` | Cached name for the '_visualErrorOffset' field. |
| `PropertyName._waitingForFadeOut` | Cached name for the '_waitingForFadeOut' field. |
| `PropertyName._wasOnFloor` | Cached name for the '_wasOnFloor' field. |
| `PropertyName._weaponBoneModifier` | Cached name for the '_weaponBoneModifier' field. |
| `PuppetActiveSlot` | 0 = weapon, 1 = grenade. Written by PuppetPlayer from Snapshot.ActiveSlot; without it the puppet's UpperBodyMix gate would stay at 0 (no FixedTick advances _activeSlot). |
| `PuppetSpineTwist` | Spine twist (view yaw minus body yaw, radians). UpdateTpsBodyAim applies it to the aim bone so the upper body follows the look direction; the body catches up past 90° delta. |
| `WeaponBoneModifierPath` | WeaponBoneModifier on the FPS skeleton applying the ADS/crouch/canted/recoil offset to ik_hand_gun. Resolved by node path, not a static singleton (which broke the editor ADS preview). |
| `_activeBleedRate` | Bleed-out rate (1/sec) applied to `_visualErrorOffset` each tick. Set by `ApplyServerCorrection` by drift magnitude; reset in `ResetInterpToCurrentPos`. |
| `_correctionPending` | Smooth-correction state: when ApplyServerCorrection detects a small drift it is faded out through this offset instead of snapping. |
| `_defaultMaterial` | Cached fallback footstep material; avoids marshalling a fresh StringName per untagged step. |
| `_intervalStartBits` | Held-input bitmask at the start of the current input-collection interval (= the previous tick's `_liveBits` snapshot). Used as `InitialBits`. |
| `_isReplaying` | True while a server reconciliation is currently replaying the last ticks. Side effects (audio, tracers, decals, net-input send) are skipped during a replay — they already ran during the original tick. |
| `_lastFirePressUsec` | Wallclock of the most recent fire-press edge, written by LocalPlayer's _Input with sub-tick precision and read by SendNetInput to compute FireSubTick. Stays 0 on server agents and puppets. |
| `_lastStepupBlockedLogMs` | Rate-limit timestamp (msec) for the "[stepup] BLOCKED — obstacle height" diagnostic log (max once/sec). |
| `_lastStepupSuccessLogMs` | Rate-limit timestamp (msec) for the "[stepup] +X.XXm" success log (throttled to limit GC pressure). |
| `_liveBits` | Held-input bitmask updated live on every input event. End-of-tick value seeds the MovementInput's legacy held flags as well as the next tick's `_intervalStartBits`. |
| `_mantleForwardOffsets` | Mantle: three forward offsets for the down-raycast scan, pre-allocated to avoid per-tick allocations. |
| `_mantleReconcileBlockUntilTick` | Tick until which reconciliation is blocked (mantle plus grace window). Mantle state isn't checkpointed, so a replay during/after a mantle would snap the player back using stale snapshots. |
| `_prevTickStartUsec` | Wallclock at the start of the previous tick — the lower bound of the interval whose events are flushed into this tick's `MovementInput`. Set in `_PhysicsProcess` immediately before `_tickStartUsec` is updated. |
| `_reconcileCountWindow` | Per-second reconcile-rate window feeding NetStats.ReconcilesPerSec. The count is bumped by LocalPlayer.ApplyServerCorrection (reconciliation lives on the local driver); the window is rolled in the shared stats pass. |
| `_stepupLastBlockedPos` | Position of the last blocked TryStepUp. Cooldown gate: skip the full TestMove sequence until the player moves >10cm, since 3×TestMove + 1×IntersectRay per tick dominated load when walking into a wall. |
| `_tickStartUsec` | Wallclock at the start of the current FixedTick; subtick-fire offsets the fire-press wallclock from this to get a fractional in-tick position. |
| `_ticksSinceSpawn` | Ticks that have passed since spawn/respawn. Reconciliation is skipped while inside the settle window (30 ticks). |
| `_visualErrorOffset` | Visual error after a replay (previously-visible minus new replay position). `_Process` adds this to GlobalPosition and fades it per tick to avoid a visible snap. |
| `_waitingForFadeOut` | True for the LocalPlayer from _Ready until the fade-out triggers once FootstepAudio preloads finish. |

## Methods

| Name | Summary |
|------|---------|
| `ApplyCrouchHeight()` | Resizes the capsule and lerps the head-pitch eye height between stand and crouch. |
| `ApplyEditorPreview(float)` | [Tool] editor preview of the FPS view. Overridden by `LocalPlayer`; base is a no-op. |
| `BuildFireInput(float)` | Builds the per-tick fire input, gating weapon actions when the grenade slot is active. The ServerAgent pulls triggers from the replicated `NetInputSource` (no Godot input on the headless server); LocalPlayer reads live input. |
| `BuildMovementInput(float)` | Base = a neutral idle input, used by `ServerPlayer`'s fallback before the first net packet. Overridden by `LocalPlayer` (live input) and `ServerPlayer` (net packet). |
| `CastGround()` | Down-raycast under the feet with the same material detection as `HandleHitscan`. |
| `ComputeThrow(float, Vector3, Vector3)` | Computes the throw origin and velocity for a given charge. Shared by `ThrowGrenade` and the aim guide so the preview matches the actual flight path. |
| `DisableExpensiveSubtreeProcessing()` | Server-only: frees TPS mesh instances and hides the visual root (skeleton bones for hitbox posing remain). Overridden by `ServerPlayer`; base is a no-op. |
| `FixedTick(float)` | Server-replayable tick step, called with constant `dt` = 1/TickRate. Only code that must also run on the server belongs here (movement, fire, stamina). |
| `GetHitboxRig()` | Read-only access to the hitbox rig (for NetServer debug broadcasts that need positions). |
| `HandleFootsteps()` | Steps the footstep cadence and emits a step event per step. Cadence (`FootstepController`) is deterministic/replayable; material probing and audio are client-side side effects. |
| `HandleGrenades(float)` | Handles slot switching (weapon vs grenade) and steps the grenade charge controller; throws on fire-release in the grenade slot. Deterministic/replayable. The ServerAgent pulls the slot from the replicated InputPacket. |
| `HandleHitscan()` | Performs the hitscan after DoFire using the movement controller's LastShotOrigin/Direction. On server authority, also does lag-compensated damage and broadcasts ShotFired. |
| `HandleJumpAnimation()` | Handles the jump-edge animation trigger, audio, and the server jump event broadcast. |
| `HandleLandingDetection()` | Detects floor transitions (touchdown and lift-off), triggers landing animation and audio, and broadcasts the land event from server authority. |
| `HandleWeaponAudio()` | Per-tick weapon audio (shoot/dry-fire/reload). Cosmetic, Local-only — overridden by `LocalPlayer`; base is a no-op. |
| `IsTunnelGround(HitInfo)` | True when the ground collider is in the "tunnel" group, used to swap to tunnel reverb. |
| `OnDropMagEvent()` | Reload-start edge event. ServerPlayer broadcasts the mag-drop. Base/local/puppet no-op (the local player drops its own mag via the FPS montage). Replay-gated by the caller. |
| `OnFootstepEvent(HitInfo, StringName)` | Step-edge event from the deterministic footstep cadence. ServerPlayer broadcasts the step; LocalPlayer plays the audio. Base/puppet no-op. The probed ground/material are passed in. |
| `OnJumpEvent()` | Jump-edge event. ServerPlayer overrides to broadcast the jump; LocalPlayer overrides to play the jump audio. Base/puppet no-op. Replay-gated by the caller. |
| `OnLandEvent(float)` | Land-edge event. ServerPlayer overrides to broadcast the land impact; LocalPlayer overrides to play the landing audio. Base/puppet no-op. Replay-gated by the caller. |
| `OnSimReady()` | Per-mode tail of `SetupSim`: sets the collision layer and any mode-specific spawn finalization. Base/puppet = client layer; ServerPlayer and LocalPlayer override. |
| `OnTickApplied()` | Per-tick hook after the deterministic step. LocalPlayer pushes prediction + acks its own tick; ServerPlayer acks the consumed net-input tick. Base/puppet: nothing (puppets don't tick). |
| `PreWarmAnimationOneShots(AnimationTree)` | Fires + aborts every AnimationNodeOneShot in the tree (found via "/request" property names) at spawn, so Godot lazy-loads the referenced animations here instead of spiking the first gameplay event. |
| `PushBoneHistory(uint)` | Called by NetServer once per tick (after BoneAttachment3D updates) — snapshots all hitbox GlobalTransforms into the ring buffer. Server agent only. |
| `RegisterGrenadeThrow(Vector3, Vector3)` | Driver hook: registers a thrown grenade with the netcode + returns its (projectileId, owner) for the spawn. `LocalPlayer` allocates a predicted id and sends it to the server; base = (0,0). |
| `ResetInterpToCurrentPos()` | Resets render-interp state after a teleport so the first frame doesn't lerp from the old position, and clears any in-flight visual reconciliation offset. |
| `ResolveActiveSlot()` | Resolves `_activeSlot` from the driver's input source (LocalPlayer: slot-key edges; ServerPlayer: the packet's SlotIsGrenade bit). Base = no-op (puppets/server-no-packet). |
| `ResolveShot(PhysicsDirectSpaceState3D)` | Per-shot resolution. Base = the local client's cosmetic pass (decal, smoke, tracer). `ServerPlayer` overrides with the authoritative lag-comp cast + broadcast. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SampleWeaponButtons()` | Samples this tick's weapon buttons from the driver's input source. Base = neutral (puppets never tick); `LocalPlayer` reads Godot input, `ServerPlayer` the packet. |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SetupCapsule()` | Builds a unique capsule resource per instance so crouch resize does not shrink every player. |
| `SetupHeadPitch()` | Configures the head-pitch pivot position to the stand-eye height. |
| `SetupSim()` | Initializes physics tuning, audio banks, hitbox rig, and the third-person aim setup. Server agents take an early-out and skip all visual-only setup. |
| `StepMantle(float)` | Per-tick smoothstep lerp during an active mantle. |
| `ThrowGrenade()` | Spawns a `SmokeGrenade` in front of the camera; throw speed lerps with held charge. |
| `TryMantle()` | Auto-mantle: when airborne with forward wish input and crouch held and an obstacle with a reachable flat top is ahead, lerps the player onto the top over `MantleDuration`. The crouch gate prevents unintended mantles on run-jumps. |
| `TryStepUp(float)` | Pre-MoveAndSlide step-up: tests for a small lip ahead and lifts the body by the actual step height; MoveAndSlide + FloorSnap then settle onto the new floor. Runs every tick (gating caused stair-stutter at sprint speed). Skips when airborne. |
| `UpdateTpsBodyAim()` | Syncs spine twist/pitch onto the `AimModifier`. The server-agent path mirrors the puppet's lagged body-yaw smoothing and rotates the skeleton root so bone poses match the puppet; lag-comp then works because BoneHistory stores the aim+twist-correct GlobalTransforms. |
| `UpdateTpsMontages()` | Fires the TPS reload/inspect one-shot on the rising edge of the reload/inspect state. Puppets read the replicated snapshot flags; the local TPS body reads its own sim. |
| `WarmUpAudio()` | Local-only: plays silent footstep/jump/land samples at a hidden position at spawn to lazy-load the banks. Overridden by `LocalPlayer`; base is a no-op. |
| `_PhysicsProcess(double)` | Per-physics-tick driver. Skips puppets (externally positioned) and frozen agents, runs the deterministic FixedTick for LocalPlayer and ServerAgent, and triggers visual updates on non-server instances. |
