# LocalPlayer

`Vantix.Character.LocalPlayer`

The local player's character (the one this client controls). Drives its own sim, prediction, input and viewmodel. Spawned from `local_player.tscn`.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.AddJumpKick` | Cached name for the 'AddJumpKick' method. |
| `MethodName.AddLandKick` | Cached name for the 'AddLandKick' method. |
| `MethodName.ApplyEditorPreview` | Cached name for the 'ApplyEditorPreview' method. |
| `MethodName.ApplyHandIk` | Cached name for the 'ApplyHandIk' method. |
| `MethodName.ApplyServerCorrection` | Cached name for the 'ApplyServerCorrection' method. |
| `MethodName.ApplyViewmodelAdsBlur` | Cached name for the 'ApplyViewmodelAdsBlur' method. |
| `MethodName.ApplyViewmodelProcedural` | Cached name for the 'ApplyViewmodelProcedural' method. |
| `MethodName.ApplyWeaponOffset` | Cached name for the 'ApplyWeaponOffset' method. |
| `MethodName.ApplyWorldAdsDof` | Cached name for the 'ApplyWorldAdsDof' method. |
| `MethodName.ComputeFireSubTick` | Cached name for the 'ComputeFireSubTick' method. |
| `MethodName.DespawnAdsCrosshair` | Cached name for the 'DespawnAdsCrosshair' method. |
| `MethodName.DriveLocomotionTree` | Cached name for the 'DriveLocomotionTree' method. |
| `MethodName.HandleKeyToggles` | Cached name for the 'HandleKeyToggles' method. |
| `MethodName.HandleMouseLook` | Cached name for the 'HandleMouseLook' method. |
| `MethodName.HandleWeaponAudio` | Cached name for the 'HandleWeaponAudio' method. |
| `MethodName.MakeCrosshairMesh` | Cached name for the 'MakeCrosshairMesh' method. |
| `MethodName.MakeOffset` | Cached name for the 'MakeOffset' method. |
| `MethodName.OnJumpEvent` | Cached name for the 'OnJumpEvent' method. |
| `MethodName.OnLandEvent` | Cached name for the 'OnLandEvent' method. |
| `MethodName.OnSimReady` | Cached name for the 'OnSimReady' method. |
| `MethodName.OnTickApplied` | Cached name for the 'OnTickApplied' method. |
| `MethodName.PlayOneShot` | Cached name for the 'PlayOneShot' method. |
| `MethodName.PollMontageState` | Cached name for the 'PollMontageState' method. |
| `MethodName.PoseAdsCrosshair` | Cached name for the 'PoseAdsCrosshair' method. |
| `MethodName.ReadInputBitsFromGodot` | Cached name for the 'ReadInputBitsFromGodot' method. |
| `MethodName.RecordSubtickInputEvent` | Cached name for the 'RecordSubtickInputEvent' method. |
| `MethodName.RenderFpsCamera` | Cached name for the 'RenderFpsCamera' method. |
| `MethodName.RenderLocalView` | Cached name for the 'RenderLocalView' method. |
| `MethodName.RenderWorldCamera` | Cached name for the 'RenderWorldCamera' method. |
| `MethodName.ResolveActiveSlot` | Cached name for the 'ResolveActiveSlot' method. |
| `MethodName.SendNetInput` | Cached name for the 'SendNetInput' method. |
| `MethodName.SetCrosshairColor` | Cached name for the 'SetCrosshairColor' method. |
| `MethodName.SetupSprintBlur` | Cached name for the 'SetupSprintBlur' method. |
| `MethodName.SpawnAdsCrosshair` | Cached name for the 'SpawnAdsCrosshair' method. |
| `MethodName.StepViewmodelProcedural` | Cached name for the 'StepViewmodelProcedural' method. |
| `MethodName.TriggerLocoStop` | Cached name for the 'TriggerLocoStop' method. |
| `MethodName.UpdateAdsCrosshair` | Cached name for the 'UpdateAdsCrosshair' method. |
| `MethodName.UpdateAdsPostFx` | Cached name for the 'UpdateAdsPostFx' method. |
| `MethodName.UpdateAimGuide` | Cached name for the 'UpdateAimGuide' method. |
| `MethodName.UpdateGripBlend` | Cached name for the 'UpdateGripBlend' method. |
| `MethodName.UpdateJumpLayer` | Cached name for the 'UpdateJumpLayer' method. |
| `MethodName.UpdateProceduralSprings` | Cached name for the 'UpdateProceduralSprings' method. |
| `MethodName.UpdateSprintBlur` | Cached name for the 'UpdateSprintBlur' method. |
| `MethodName.UpdateViewmodelMontages` | Cached name for the 'UpdateViewmodelMontages' method. |
| `MethodName.UpdateVisualBlends` | Cached name for the 'UpdateVisualBlends' method. |
| `MethodName.WarmUpAudio` | Cached name for the 'WarmUpAudio' method. |
| `MethodName._Input` | Cached name for the '_Input' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `PropertyName.NeedsHitboxRig` | Cached name for the 'NeedsHitboxRig' property. |
| `PropertyName._adsLineH` | Cached name for the '_adsLineH' field. |
| `PropertyName._adsLineV` | Cached name for the '_adsLineV' field. |
| `PropertyName._adsMarker` | Cached name for the '_adsMarker' field. |
| `PropertyName._adsTestPrev` | Cached name for the '_adsTestPrev' field. |
| `PropertyName._aimDbg` | Cached name for the '_aimDbg' field. |
| `PropertyName._aimGuide` | Cached name for the '_aimGuide' field. |
| `PropertyName._airMaxFallDist` | Cached name for the '_airMaxFallDist' field. |
| `PropertyName._airTime` | Cached name for the '_airTime' field. |
| `PropertyName._bobScale` | Cached name for the '_bobScale' field. |
| `PropertyName._bodyPitchLag` | Cached name for the '_bodyPitchLag' field. |
| `PropertyName._bodyYawInit` | Cached name for the '_bodyYawInit' field. |
| `PropertyName._bodyYawLag` | Cached name for the '_bodyYawLag' field. |
| `PropertyName._cachedPostFx` | Cached name for the '_cachedPostFx' field. |
| `PropertyName._camRestLocal` | Cached name for the '_camRestLocal' field. |
| `PropertyName._camRigCaptured` | Cached name for the '_camRigCaptured' field. |
| `PropertyName._cantedBlend` | Cached name for the '_cantedBlend' field. |
| `PropertyName._crouchBlend` | Cached name for the '_crouchBlend' field. |
| `PropertyName._dirLeanSpringVel` | Cached name for the '_dirLeanSpringVel' field. |
| `PropertyName._editorTreeReady` | Cached name for the '_editorTreeReady' field. |
| `PropertyName._eyeRest` | Cached name for the '_eyeRest' field. |
| `PropertyName._fallStartY` | Cached name for the '_fallStartY' field. |
| `PropertyName._gripAimBlend` | Cached name for the '_gripAimBlend' field. |
| `PropertyName._gripBlend` | Cached name for the '_gripBlend' field. |
| `PropertyName._gripSwitchDelay` | Cached name for the '_gripSwitchDelay' field. |
| `PropertyName._inertiaTilt` | Cached name for the '_inertiaTilt' field. |
| `PropertyName._jumpInitiated` | Cached name for the '_jumpInitiated' field. |
| `PropertyName._jumpKickPitch` | Cached name for the '_jumpKickPitch' field. |
| `PropertyName._jumpKickPitchVel` | Cached name for the '_jumpKickPitchVel' field. |
| `PropertyName._jumpKickPos` | Cached name for the '_jumpKickPos' field. |
| `PropertyName._jumpKickVel` | Cached name for the '_jumpKickVel' field. |
| `PropertyName._jumpLoopBlend` | Cached name for the '_jumpLoopBlend' field. |
| `PropertyName._lookDelta` | Cached name for the '_lookDelta' field. |
| `PropertyName._montageActive` | Cached name for the '_montageActive' field. |
| `PropertyName._mouseInertia` | Cached name for the '_mouseInertia' field. |
| `PropertyName._mouseInertiaSmoothed` | Cached name for the '_mouseInertiaSmoothed' field. |
| `PropertyName._pendingGrip` | Cached name for the '_pendingGrip' field. |
| `PropertyName._postFxLookupDone` | Cached name for the '_postFxLookupDone' field. |
| `PropertyName._prevBodyPitch` | Cached name for the '_prevBodyPitch' field. |
| `PropertyName._prevBodyYaw` | Cached name for the '_prevBodyYaw' field. |
| `PropertyName._prevProcVelocity` | Cached name for the '_prevProcVelocity' field. |
| `PropertyName._recoilVel` | Cached name for the '_recoilVel' field. |
| `PropertyName._reloadAudioWasActive` | Cached name for the '_reloadAudioWasActive' field. |
| `PropertyName._smoothBodyY` | Cached name for the '_smoothBodyY' field. |
| `PropertyName._smoothedDirRatio` | Cached name for the '_smoothedDirRatio' field. |
| `PropertyName._sprintBlurBlend` | Cached name for the '_sprintBlurBlend' field. |
| `PropertyName._sprintBlurLayer` | Cached name for the '_sprintBlurLayer' field. |
| `PropertyName._sprintBlurMat` | Cached name for the '_sprintBlurMat' field. |
| `PropertyName._sprintBlurRect` | Cached name for the '_sprintBlurRect' field. |
| `PropertyName._sprintFovBlend` | Cached name for the '_sprintFovBlend' field. |
| `PropertyName._sprintStopArmed` | Cached name for the '_sprintStopArmed' field. |
| `PropertyName._stepSmoothOffset` | Cached name for the '_stepSmoothOffset' field. |
| `PropertyName._stepYInit` | Cached name for the '_stepYInit' field. |
| `PropertyName._swayCurrent` | Cached name for the '_swayCurrent' field. |
| `PropertyName._viewSwayPos` | Cached name for the '_viewSwayPos' field. |
| `PropertyName._viewSwayRotDeg` | Cached name for the '_viewSwayRotDeg' field. |
| `PropertyName._viewmodelBlurLookupDone` | Cached name for the '_viewmodelBlurLookupDone' field. |
| `PropertyName._viewmodelBlurMat` | Cached name for the '_viewmodelBlurMat' field. |
| `PropertyName._vmLastShotIndex` | Cached name for the '_vmLastShotIndex' field. |
| `PropertyName._vmWasAirborne` | Cached name for the '_vmWasAirborne' field. |
| `PropertyName._vmWasInspecting` | Cached name for the '_vmWasInspecting' field. |
| `PropertyName._vmWasReloading` | Cached name for the '_vmWasReloading' field. |
| `PropertyName._walkStopArmed` | Cached name for the '_walkStopArmed' field. |
| `PropertyName._wasAirborneRaw` | Cached name for the '_wasAirborneRaw' field. |
| `PropertyName._wasNearStop` | Cached name for the '_wasNearStop' field. |

## Methods

| Name | Summary |
|------|---------|
| `AddLandKick(float)` | Fires the landing clip + impact kick when the air cycle was a real jump/fall (jump key or a fall past JumpMinFallHeight). Returns whether it counted, so the caller gates the landing sound alike. |
| `ApplyServerCorrection(uint, Vector3, Vector3)` | Called by `NetClient` after each received snapshot. Compares the server position at the acked tick with the locally stored prediction. Small drifts are bled out smoothly, large drifts trigger a full replay with a visual smoothing offset. |
| `ApplyViewmodelAdsBlur(float)` | Drives the viewmodel_ads_blur shader on the weapon SubViewportContainer — a 2D pseudo-DOF keeping the iron-sight zone sharp. The only way to blur the weapon, since CameraAttributes DOF doesn't render in its transparent_bg SubViewport. |
| `ApplyWorldAdsDof(float)` | World far-DOF for ADS. Far DOF stays enabled; only the amount fades with ADS (no per-frame toggle). |
| `HandleWeaponAudio()` | Per-tick weapon audio: shoot, dry-fire and reload on the movement controller's fire-state edges. Replay-gated so reconciliation doesn't re-trigger sounds. |
| `ProbeReverbEnv(Vantix.Character.HitInfo)` | Classifies the gunshot reverb environment via an upward ceiling raycast. Tunnel-tagged ground returns Tunnel, a ceiling hit returns Indoor, otherwise Outdoor. |
| `RenderLocalView(double)` | The local per-frame view chain. Invoked by LocalPlayer._Process after visual interpolation, and directly in the editor for the [Tool] preview. |
| `ReplayOneTick(Vantix.Character.MovementInput)` | Re-simulates one tick with the saved input; physics state is re-derived from current position. Audio, FX and net-send are skipped via `_isReplaying`. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SetupSprintBlur()` | Lazily builds the peripheral sprint-blur overlay: a full-screen ColorRect running sprint_blur.gdshader on a CanvasLayer behind the viewmodel (layer -1, so the weapon/HUD stay crisp). |
| `UpdateAdsPostFx()` | Fades in ADS depth-of-field: world camera focuses far via CameraAttributes DOF, weapon blurred by the 2D viewmodel_ads_blur shader (CameraAttributes DOF doesn't render in the weapon's transparent_bg SubViewport). Also feeds AdsBlend into the screen-space post-FX. |
| `UpdateAimGuide()` | Renders the grenade trajectory preview while the grenade slot is active and fire is held. Uses `Predict` with the same pending-throw origin/velocity the sim uses on release, so the preview matches the real flight. Built lazily on first show. |
| `UpdateSprintBlur(float)` | Shows/hides the sprint-blur overlay and feeds it the eased sprint blend. Gated by the Motion Blur graphics setting. |
