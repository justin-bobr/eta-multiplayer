# Infima Tactical-FPS — Complete 1:1 Port Spec (Unreal → Godot 4 / C#)

Definitive reference for rebuilding the Infima "Tactical FPS Animations" AR demo (FP controller +
weapon) in Godot. Extracted from the cooked `.uasset` blueprints at
`C:/Users/stefa/Documents/Unreal Projects/extract/Content/InfimaGames/TacticalFPSAnimations`
plus the user's UE editor screenshots. Tags: **[X]** extracted from binary, **[S]** from screenshot,
**[U]** user-confirmed value, **[I]** inferred, **[?]** needs an exact value read in UE.

---

## 0. The core lesson (why earlier attempts failed)
Unreal does ADS by **moving the weapon bone, not the camera**, and keeps **BOTH hands on the moved
weapon with FABRIK**. The camera only changes FOV. So a faithful port REQUIRES working dual-hand IK —
there is no shortcut. Our custom two-bone solver is unstable (flings arms off-screen), so until a real
**FABRIK** is in, the weapon must NOT be moved procedurally and IK must stay OFF — the animation alone
already grips the gun with both hands (confirmed: the editor preview shows correct grips with no IK).

---

## 1. Scene mapping (Godot ↔ UE) [X]
```
AnimatedCharacter (Node3D, root)         ← BP_TFA_BaseCharacter
 ├─ Camera3D                              ← CameraFP (head bone, FOV only)
 ├─ fps_arms/Armature/Skeleton3D          ← FP mesh (UE Manny FP skeleton)
 │   ├─ ik_hand_gun (BoneAttachment3D)    ← the WEAPON BONE (all ModifyBone targets)
 │   │   ├─ camera (Node3D)               ← SOCKET_GunCamera
 │   │   ├─ Weapon (M4)                   ← weapon attached to ik_hand_gun
 │   │   ├─ LeftHandIKTarget / Pole       ← FABRIK-Left effector (foregrip)
 │   └─ head (BoneAttachment3D)           ← CameraFP anchor
 ├─ RightHandIKTarget / Pole              ← FABRIK-Right effector (should sit on the rear grip)
```
Skeleton bones present (UE Manny FP): `root, spine_*, clavicle_l/r, upperarm_l/r, lowerarm_l/r,
hand_l/r, head, ik_hand_root → ik_hand_gun → ik_hand_l/r`. The weapon + both IK targets ride
`ik_hand_gun`; the deforming arms (`*_l/r`) are a separate chain that the FP anims pose onto the gun.

---

## 2. AnimGraph node stack (ABP_TFA_FP_BaseCharacter, bottom → top) [X]
1. **BlendSpacePlayer** — per-stance locomotion blend space (the ±100 2D blend space).
2. **BlendListByBool / BlendListByEnum** — stance/aim selection (Standing ↔ Aimed ↔ Crouched),
   driven by `bIsAiming` and `CurrentStance` (E_TFA_Stance), with a blend time.
3. **LayeredBoneBlend ×3** + **ApplyMeshSpaceAdditive ×3** (`Root Space Additive`, Alpha=1.0) — the
   additive layers: **left-hand grip pose overlay**, **recoil**, and the **ADS/crouch additive** pose.
4. **ModifyBone ×3** on the weapon bone `ik_hand_gun` (see §3) — labeled in UE:
   - **"Aim Down Sights"** → applies `CurrentAimDownSightsOffset`.
   - **"Procedural Crouch Pose"** → applies `CurrentCrouchOffset` (`CrouchTransform`).
   - **Recoil** → applies `RecoilTransform`.
5. **FABRIK ×2** — **"Left"** (tip `hand_l`) and **"Right"** (tip `hand_r`), each pinning the hand to
   the (now-moved) weapon. `bIsLeftHandOnWeapon` gates the left one.
6. **DefaultSlot / AdditiveSlot** montage slots — fire / reload / inspect play over the result.
7. **LocalToComponentSpace / ComponentToLocalSpace** wrap the component-space skeletal controls;
   **SaveCachedPose / UseCachedPose** cache the per-stance locomotion (`CachedLocomotion*`).

Godot mapping: 1–2 = `AnimationTree` blend spaces + Blend2 (DONE). 3 = `AnimationNodeAdd2` layers /
bone-pose additive. 4 = post-tree bone writes on `ik_hand_gun` (or its child nodes). 5 = a real FABRIK
SkeletonModifier on each arm. 6 = `AnimationNodeOneShot` (DONE).

---

## 2b. ABP_TFA_TP_BaseCharacter — differences vs FP [S confirmed]
TP AnimGraph differs from FP in these sections:
- **Blend Idle & Aim Stance**: Canted Aim blend (Blend Poses by bool, True/False Blend Time **0.17s**), then Slot 'Aiming'/Slot 'DefaultSlot', then ADS Blend Poses by bool True/False Blend Time **0.2s** (FP = 0.18s).
- **Apply Procedural Recoil**: TP amplifies the raw spring value — Translation × (3.0, 0.5, 4.0), Rotation × (Roll×2.0, Pitch×4.0, Yaw×1.7) before sending to ModifyBone on `ik_hand_gun`. FP passes the spring value directly (no multiplier).
- **Additive layers**: TP has Breathing additive (looping breathe Sequence) + SM_AimingTransitions (vs FP's SM_CrouchingTransitions / SM_RunningTransitions / SM_WalkingTransitions).
- **FABRIK**: identical in both — Left hand + Right hand, Alpha 1.0, same comment "Attach L/R Hands to the Weapon Using IK, Fixes Issue With Floaty Hands When Using Additive Animations".
- **Grip Pose**: identical — BlendPoses(E_TFA_GripAttachment) × 2 (normal + aimed), Grip Pose Switch Speed, then Blend by bool (Is Aiming), True/False Blend Time **0.15s** (same in both FP and TP).

---

## 3. Skeletal controls — exact [X]
**ModifyBone (Transform-Modify-Bone)** node fields: `BoneToModify, Translation, Rotation, Scale,
TranslationMode, RotationMode, ScaleMode, TranslationSpace, RotationSpace, ScaleSpace`. Spaces are
`BCS_BoneSpace | BCS_ComponentSpace | BCS_WorldSpace`. The 3 instances:
| Label | BoneToModify | Input | Mode | Space [I] |
|---|---|---|---|---|
| Aim Down Sights | `ik_hand_gun` | `CurrentAimDownSightsOffset` (Loc+Rot) | Add to existing | BoneSpace |
| Procedural Crouch Pose | `ik_hand_gun` | `CurrentCrouchOffset` | Add to existing | BoneSpace |
| Recoil | `ik_hand_gun` | `RecoilTransform` | Add to existing | BoneSpace |

**FABRIK** node fields: `EffectorTransform, EffectorTarget, TipBone, RootBone, Precision,
MaxIterations, EffectorTransformSpace (EIT_LocalSpace…), EffectorRotationSource, bEnableDebugDraw`.
Two instances:
| Label | TipBone | RootBone [I] | Effector | Alpha source |
|---|---|---|---|---|
| Left | `hand_l` | `clavicle_l` | `ik_hand_l` (Bone Space, Copy Target Rotation) | Pin (`bIsLeftHandOnWeapon`) |
| Right | `hand_r` | `clavicle_r` | `ik_hand_r` (Bone Space, Copy Target Rotation) | Pin (always 1.0) |

Both: Precision **0.01** (cm) = `min_distance 0.0001` m in Godot, MaxIterations **10**.

→ Godot: implement FABRIK (iterative, 2–3 bone chain, MaxIterations ~10, Precision ~1mm) per arm as a
post-animation pass (a `SkeletonModifier3D` or a high-`ProcessPriority` write so the tree can't undo it).
The effector targets must be CHILDREN of `ik_hand_gun` so they move with the ModifyBone offset.

---

## 4. Procedural offsets — values + application [S/X]
UE `FTransform` (Position + Rotation°, Scale always 1). Godot = (x=UE.y, y=UE.z, z=-UE.x)/100,
rotation UE-pitch(Y)° → Godot X = −(UE.Y)°:
| | UE Location | UE Rotation | Godot pos (m) | Godot rot (°) |
|---|---|---|---|---|
| OffsetAimDownSights | (-2, -6, 2.05) | (0, 8.4, 0) | (-0.06, 0.0205, 0.02) | (-8.4, 0, 0) |
| OffsetCrouch | (1.5, -2, -1.5) | (0, -4.3, 0) | (-0.02, -0.015, -0.015) | (4.3, 0, 0) |
| OffsetCantedAim | (-5, 1.5, -1) | (0, -35, 0) | (0.015, -0.01, 0.05) | (35, 0, 0) |

- `OffsetAimDownSights` = "calculated transform that aligns the weapon sights with the camera centre"
  → the BULK of the centring comes from the **aimed locomotion animations**; this offset is the final
  alignment. `OffsetCantedAim` = "additional offset added to the base ADS position when canted".
- Targets `TargetAimDownSightsOffset`; `CurrentAimDownSightsOffset` springs to it via **SpringAimDownSights**
  (VectorSpringInterp). Same pattern for crouch (`SpringCrouch`) and recoil (`SpringRecoil`).
- Applied via the ModifyBone on `ik_hand_gun` (§3), NOT the camera.

---

## 5. Springs & velocity [X structure / ? numbers]
- **VectorSpringInterp** params: `Stiffness, CriticalDampingFactor, Mass, TargetVelocityAmount`. UE keeps
  **separate Location and Rotation springs**: `LocStiffness/LocMass`, `RotStiffness/RotMass`. Three spring
  states: `SpringRecoil`, `SpringCrouch`, `SpringAimDownSights`. [?] exact stiffness/damping/mass — read in UE.
- **SimulatedVelocity**: `VInterpTo(SimulatedVelocity, LocTargetVelocity, dt, InterpSpeed)` with
  **asymmetric `InterpSpeedIncreasing` / `InterpSpeedDecreasing`** (accelerate ≠ decelerate). [?] values.
  Drives the −100..100 blend spaces. (Godot: `Lerp` + diamond clamp — DONE, single-rate for now.)
- **Grip**: `CurrentGripAlpha = FInterpTo(…, GripPoseBlendSpeed)`, switch via `GripPoseSwitchSpeed`;
  `CurrentGripPose` selected by `CurrentGrip` (E_TFA_GripAttachment). [U] **GripPoseBlendSpeed = 15.0**,
  **GripPoseSwitchSpeed = 0.065** (65 ms cross-blend in BlendPoses node when switching grip type).

---

## 6. Camera / FOV [X/U]
- ADS = **FOV only** via the `TL_ADS_FOV` timeline → `SetFieldOfView`. **DefaultFOV 100, AimedFOV 78** [U].
  Camera never translates/rotates for ADS. `bAnimateCamera` (IA_ToggleCameraAnimation) toggles baked head-bone
  camera anim. TP/bodycam use sockets `SOCKET_GunCamera/Helmet/Chest`; TP zoom: MinDist 65, MaxDist 270,
  ZoomStep 50, InitialDist 140 [U].
- Crouch ALSO lowers the FP mesh height via `K2_SetRelativeLocation` (separate from the gun-bone crouch
  offset). Eye heights: Base 64, Crouched 32 [U] → Godot drop 0.32 m.

---

## 7. Movement & speeds [U/X]
MaxWalkSpeed 600, MaxWalkSpeedCrouched 300, weapon MaxCustomMovementSpeed 600 [U]. **Run and Sprint are
two separate actions** (Shift = Run weapon-ready, Alt = Sprint = `CGraph_TacticalSprint`, weapon lowered).
Run/Sprint loops (`FP_RunLoop/SprintLoop`) are NOT blend-space samples — separate forward loops.

---

## 8. Locomotion blend spaces [S]
5-point 2D, axes H/V **Min −100 / Max 100, Grid 4**, smoothing Averaged: Idle(0,0), Walk_F(0,+100),
Walk_B(0,−100), Strafe_L(−100,0), Strafe_R(+100,0). Standing smoothing H0.5/V0.3, Aimed H0.6/V0.6,
Crouched H0.5/V0.35 (Standing clips @ RateScale 0.8). Crouch in/out = 1-D blend spaces over AimAlpha
(`FP_Transition_Crouch_Start/End`) via `SM_CrouchingTransitions`. Stop anims: `FP_Transition_WalkEnd/RunEnd`.

---

## 9. Inputs / keybinds [X actions]
Actions (IMC_TFA_Default): ADS, **CantedAiming** (toggle while ADS), Crouch (toggle), Equip, Fire,
FireModeSwitch, GrenadeThrow, Heal, Inspecting, Interact, Jump, Look, Melee, Reload_Empty,
Reload_MagCheck, Reload_Quick, Run, **Sprint** (≠ Run), WeaponJam, ToggleCameraPerspective,
ToggleCameraAnimation. Godot demo keys: WASD move, Shift Run, Alt Sprint, Space Jump, C Crouch,
LMB Fire, RMB ADS, **MMB toggle Canted**, V firemode, R reload (hold MagCheck), Q quick, E empty,
J jam, G grenade, F melee, T inspect (hold empty), B grip, H equip, U heal, X interact, L camera-anim, Tab HUD.

## 10. Enums [X]
- E_TFA_GripAttachment: 0 Default, 1 Angled, 2 Vertical.
- E_TFA_Stance: 0 Standing, 1 Crouched.
- E_TFA_FireMode: 0 Semi, 1 Auto, 2 Burst.
- E_TFA_CameraPerspectives: FirstPerson, ThirdPerson, …, GunCamera.

---

## 11. Godot status & plan
DONE: ±100 blend-spaces ×3 + stance/run/sprint Blend2, SimulatedVelocity (single-rate), additive grip,
montage OneShot, FOV ADS (100→78), crouch body-drop 0.32, recoil spring (camera view-kick), Pos/Rot
offset fields with inspector **animation dropdowns**, MMB canted toggle,
**Walk_End/Run_End** additive OneShot (`LocoStop` node before `Action`) — triggers when moveInput crosses zero while simVel > 20.
BROKEN/OFF: custom two-bone IK (flings arms) → disabled; procedural weapon offset → disabled (needs IK).
TODO (in order):
1. **Real FABRIK** (iterative) per arm as a post-tree skeleton pass; effector targets childed to `ik_hand_gun`.
2. Re-enable the **ModifyBone-equivalent** weapon offset (ADS/crouch/canted/recoil) on `ik_hand_gun`; both
   hands FABRIK to follow. Flip `_proceduralWeaponOffset` on.
3. Spring-based ADS/crouch/recoil (`VectorSpringInterp`, separate Loc/Rot springs) — read UE stiffness/damping/mass.
4. Asymmetric SimulatedVelocity (Increasing/Decreasing).
5. Tactical-sprint weapon lower.
[?] values to read in UE: spring Stiffness/Damping/Mass (recoil/crouch/ADS), InterpSpeedIncreasing/Decreasing,
GripPoseBlendSpeed/SwitchSpeed, fire RPM.
Confirmed blend times [S]: Grip blend 0.15s, ADS 0.18s (FP) / 0.20s (TP), Stance 0.20s (FP) / Canted 0.17s (TP).
