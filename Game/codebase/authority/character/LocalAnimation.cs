using Godot;

/// <summary>
/// First-person viewmodel and camera-feel driver. Computes additive pose offsets for the active
/// weapon (bob, sway, lean, recoil, jump/land, sprint-lower, ADS, wall-pushback) and applies
/// camera-side effects (FOV ramp, blur, DOF, vignette). Designed for the local player view; for
/// remote/AI characters _Process early-exits.
/// </summary>
[Tool]
public partial class LocalAnimation : Node3D
{
	[Export] public Node3D Target;
	[Export] public Camera3D Camera;
	[Export] public Camera3D ViewmodelCamera;
	[Export] public SubViewportContainer ViewmodelContainer;
	[Export] public AnimationTree AnimTree;
	[Export] public Node3D FingerMove;
	[Export] public SkeletonModifier3D FingerIk;
	/// <summary>Active weapon - one .tscn per weapon with all per-weapon settings (refs, ADS, muzzle, tracer, shell).
	/// The getters below read from the Weapon component; swapping the weapon swaps look and feel.</summary>
	[Export] public Weapon Weapon;
	public Node3D Muzzle => Weapon?.Muzzle;
	public GpuParticles3D MuzzleFlashParticles => Weapon?.MuzzleFlashParticles;
	public GpuParticles3D MuzzleSmokeParticles => Weapon?.MuzzleSmokeParticles;
	public GpuParticles3D MuzzleSparksParticles => Weapon?.MuzzleSparksParticles;
	public Node3D EjectionPort => Weapon?.EjectionPort;
	public AnimationTree WeaponAnimTree => Weapon?.AnimTree;
	public Color MuzzleLightColor => Weapon?.MuzzleLightColor ?? new Color(1f, 0.75f, 0.35f);
	public float MuzzleLightEnergy => Weapon?.MuzzleLightEnergy ?? 5f;
	public float MuzzleLightRange => Weapon?.MuzzleLightRange ?? 3.5f;
	public float MuzzleLightDuration => Weapon?.MuzzleLightDuration ?? 0.05f;
	public bool TracerEnabled => Weapon?.TracerEnabled ?? true;
	public int TracerEveryNthShot => Weapon?.TracerEveryNthShot ?? 5;
	public Color TracerColor => Weapon?.TracerColor ?? new Color(2.5f, 1.6f, 0.5f, 1f);
	public float TracerWidth => Weapon?.TracerWidth ?? 0.02f;
	public float TracerStreakLength => Weapon?.TracerStreakLength ?? 2f;
	public float TracerSpeed => Weapon?.TracerSpeed ?? 80f;
	public float TracerMaxRange => Weapon?.TracerMaxRange ?? 80f;
	public bool ShellEnabled => Weapon?.ShellEnabled ?? true;
	public Vector3 ShellSpawnOffset => Weapon?.ShellSpawnOffset ?? Vector3.Zero;
	public Vector3 ShellEjectDirection => Weapon?.ShellEjectDirection ?? new Vector3(0.6f, 0.1f, 1f);
	public Vector3 ShellInitialRotationDeg => Weapon?.ShellInitialRotationDeg ?? new Vector3(0f, 0f, 90f);
	public float ShellEjectSpeed => Weapon?.ShellEjectSpeed ?? 2.5f;
	public float ShellSpreadAngleDeg => Weapon?.ShellSpreadAngleDeg ?? 6f;
	public float ShellLifetime => Weapon?.ShellLifetime ?? 30f;

	[Export] public StringName ActionInspect = "inspect";
	[Export] public StringName InspectRequestParam = "parameters/OneShot/request";

	[Export] public StringName ArmsReloadRequestParam = "parameters/Reload/request";
	[Export] public StringName WeaponReloadRequestParam = "parameters/Reload/request";

	/// <summary>Active weapon (immutable record from ConVars.Weapons.&lt;X&gt;).</summary>
	public WeaponStats ActiveWeapon = ConVars.Weapons.M4A1;

	public ClConVars Cl = ConVars.Cl;
	public SvConVars Sv = ConVars.Sv;

	/// <summary>MovementController reference (set by character.cs for fire-state + AimPunch).</summary>
	public MovementController Movement;

	private PlayerCore _ownerCharCached;
	/// <summary>Walks the parent chain to find the owning <see cref="PlayerCore"/>.</summary>
	private PlayerCore FindOwnerCharacter()
	{
		Node n = this;
		while (n != null)
		{
			if (n is PlayerCore lc) return lc;
			n = n.GetParent();
		}
		return null;
	}

	/// <summary>FootstepController reference (set by character.cs). Drives the master clock for
	/// the view-bob so the camera/weapon dip lines up exactly with the step sound instead of
	/// drifting against a separate oscillator.</summary>
	public FootstepController Footstep;

	[Export] public bool BreathingEnabled = true;
	[Export] public bool SprintSwayEnabled = true;
	[Export] public bool CameraEffectsEnabled = true;
	[Export] public Key BreathToggleKey = Key.B;
	[Export] public Key SprintSwayToggleKey = Key.N;
	[Export] public Key CameraToggleKey = Key.M;
	[Export] public StringName ActionCrouch = "crouch";

	public bool AdsTestMode => Weapon?.AdsTestMode ?? false;
	public Vector3 AdsTestPosOffset => Weapon?.AdsTestPosOffset ?? new Vector3(-0.05f, 0.04f, -0.06f);
	public Vector3 AdsTestRotOffset => Weapon?.AdsTestRotOffset ?? Vector3.Zero;
	public bool AdsAffectsFov => Weapon?.AdsAffectsFov ?? true;
	public float AdsFovScale => Weapon?.AdsFovScale ?? 0.8f;
	public Vector3 AdsTestCrouchPosAdd => Weapon?.AdsTestCrouchPosAdd ?? Vector3.Zero;
	public Vector3 AdsTestCrouchRotAdd => Weapon?.AdsTestCrouchRotAdd ?? Vector3.Zero;
	public float AdsCalibrationDistance => Weapon?.AdsCalibrationDistance ?? 10f;
	public float AdsCalibrationSize => Weapon?.AdsCalibrationSize ?? 0.04f;
	public Color AdsCalibrationColor => Weapon?.AdsCalibrationColor ?? new Color(1f, 0.2f, 0.2f, 1f);

	private MeshInstance3D _calibrationMarker;
	private MeshInstance3D _calibrationLineH, _calibrationLineV;

	public Vector3 CurrentVelocity = Vector3.Zero;
	public float CurrentBodyYaw;
	public float CurrentSpeed => CurrentVelocity.Length();

	private Node3D _node;
	private Vector3 _basePos;
	private Vector3 _baseRotDeg;
	private float _baseFov;
	private bool _weaponIsCameraChild;
	private Transform3D _weaponBaseInCamSpace;

	private float _breathPhase;
	private float _breathBlend;
	private float _bobPhase;
	private float _bobBlend;
	private Vector3 _smoothedDirRatio;
	private float _swayBlend;
	private float _cameraBlend;
	private float _sprintFovBlend;
	private float _sprintSide = 1f;
	private float _sprintSideVel;
	private ShaderMaterial _sprintBlurMat;
	private ColorRect _sprintBlurRect;
	private Vector3 _appliedCamRot;
	private float _appliedFovDelta;

	private Vector3 _weaponKickRot;
	private Vector3 _weaponKickPos;
	private Vector3 _weaponKickRotVel;
	private Vector3 _weaponKickPosVel;

	private Vector3 _camShakeRot;
	private Vector3 _camShakeRotVel;
	private readonly Godot.RandomNumberGenerator _camShakeRng = new();

	private Vector3 _jumpKickPos;
	private Vector3 _jumpKickPosVel;
	private Vector3 _jumpKickRot;
	private Vector3 _jumpKickRotVel;
	private float _jumpTimer = -1f;
	private bool _externalJump;
	private float _airBlend;

	private float _crouchBlend;

	private Vector2 _mouseDeltaAccum;
	private Vector3 _mouseInertia;
	private Vector3 _mouseInertiaSmoothed;

	private Vector3 _prevVelocity;
	private Vector3 _inertiaTilt;

	private Vector3 _dirLeanSpringVel;

	private float _prevBodyYaw;
	private bool _bodyYawInit;
	private float _bodyYawLag;

	private float _idleTimer;
	private float _lowerBlend;
	private float _dryFireWobblePhase;
	private float _dryFireWobbleAmp;
	private bool _lastSeenDryFire;
	private Vector3 _aimPunchSmoothed;
	private RandomNumberGenerator _visualRng = new();
	private int _lastSeenShotIndex;
	private bool _lastSeenReloading;
	private bool _lastSeenInspecting;

	private Vector3 _fingerRestPos;
	private float _fingerZOffset;
	private StringName _inspectActiveParam;
	private StringName _armsReloadActiveParam;

	// Pre-cached StringNames für Shader-Parameter (sonst werden bei jedem SetShaderParameter("sprint", ...)
	// die strings → StringName konvertiert = Alloc pro Frame).
	private static readonly StringName _shaderSprintParam = "sprint";
	private static readonly StringName _shaderAdsBlendParam = "ads_blend";

	private Vector3 _editorRestPos;
	private Vector3 _editorRestRotDeg;
	private float _editorRestFov;
	private bool _editorRestCaptured;
	private bool _editorPrevAdsTestMode;

	/// <summary>Captures rest-pose state, builds the sprint-blur overlay and caches AnimTree param paths.</summary>
	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			_node = Target ?? this;
			_editorPrevAdsTestMode = AdsTestMode;
			return;
		}

		_node = Target ?? this;
		_basePos = _node.Position;
		_baseRotDeg = _node.RotationDegrees;
		_breathBlend = BreathingEnabled ? 1f : 0f;
		_cameraBlend = CameraEffectsEnabled ? 1f : 0f;

		if (Camera != null)
		{
			_baseFov = Camera.Fov;
			_weaponIsCameraChild = HasAncestor(_node, Camera);
			if (!_weaponIsCameraChild)
			{
				Camera3D anchor = ViewmodelCamera ?? Camera;
				_weaponBaseInCamSpace = anchor.GlobalTransform.AffineInverse() * _node.GlobalTransform;
				Dbg.Print($"[weapon] Sibling-Setup detected. anchor={anchor.Name}");
			}
		}

		if (FingerMove != null) _fingerRestPos = FingerMove.Position;

		SetupSprintBlur();

		_inspectActiveParam = new StringName(InspectRequestParam.ToString().Replace("/request", "/active"));
		_armsReloadActiveParam = new StringName(ArmsReloadRequestParam.ToString().Replace("/request", "/active"));
	}

	/// <summary>Returns true if <paramref name="potentialAncestor"/> appears in the parent chain of <paramref name="node"/>.</summary>
	private static bool HasAncestor(Node node, Node potentialAncestor)
	{
		if (node == null || potentialAncestor == null) return false;
		Node n = node.GetParent();
		while (n != null)
		{
			if (n == potentialAncestor) return true;
			n = n.GetParent();
		}
		return false;
	}

	/// <summary>Creates the sprint peripheral-blur overlay (full-screen ColorRect on its own
	/// CanvasLayer beneath the HUD). Fed by ApplyCameraEffects.</summary>
	private void SetupSprintBlur()
	{
		var shader = GD.Load<Shader>("res://maps/dust/sprint_blur.gdshader");
		if (shader == null) return;
		_sprintBlurMat = new ShaderMaterial { Shader = shader };
		_sprintBlurRect = new ColorRect
		{
			Name = "_SprintBlur",
			Material = _sprintBlurMat,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		_sprintBlurRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		var layer = new CanvasLayer { Name = "_SprintBlurLayer", Layer = -1 };
		layer.AddChild(_sprintBlurRect);
		_sprintBlurLayerHandle = layer;
		GetTree().Root.AddChild(layer);
	}

	private CanvasLayer _sprintBlurLayerHandle;
	private bool _adsDbgArmed;
	private PostProcessEffect _cachedPostFx;
	private bool _postFxLookupDone;

	/// <summary>Caches the PostProcessEffect (on the world WorldEnvironment.Compositor) and feeds
	/// AdsBlend into the ADS-vignette-boost logic. Lazy lookup, performed once per lifetime - fast
	/// enough for a 60 Hz call.</summary>
	private void FeedAdsBlendToPostFx(float adsBlend)
	{
		if (!_postFxLookupDone)
		{
			_postFxLookupDone = true;
			foreach (Node n in GetTree().Root.FindChildren("*", "WorldEnvironment", true, false))
			{
				if (n is WorldEnvironment we && we.Compositor is Compositor c)
					foreach (CompositorEffect e in c.CompositorEffects)
						if (e is PostProcessEffect ppe) { _cachedPostFx = ppe; break; }
				if (_cachedPostFx != null) break;
			}
		}
		if (_cachedPostFx != null) _cachedPostFx.AdsBlend = adsBlend;
		// Mirror to PostCanvasFx (FSR2 path). Only one of the two is visible at a time
		// (Settings.ApplyEffects gates by Upscaler), but feeding both costs ~nothing.
		if (PostCanvasFx.Instance != null) PostCanvasFx.Instance.AdsBlend = adsBlend;
	}

	/// <summary>Applies near-DOF to a camera (no-op if Camera is null or Attributes is not Practical).
	/// Called once for both the world and viewmodel cameras so both get DOF on ADS.</summary>
	private static void ApplyDofTo(Camera3D cam, bool enabled, float distance, float amount)
	{
		if (cam?.Attributes is not CameraAttributesPractical a) return;
		a.DofBlurFarEnabled = false;
		a.DofBlurNearEnabled = enabled;
		a.DofBlurNearDistance = distance;
		a.DofBlurNearTransition = 0.18f;
		a.DofBlurAmount = amount;
	}

	/// <summary>Frees the tree-root owned helper nodes (sprint-blur layer and world muzzle light) to avoid leaks on respawn.</summary>
	public override void _ExitTree()
	{
		if (Godot.GodotObject.IsInstanceValid(_sprintBlurLayerHandle))
		{
			_sprintBlurLayerHandle.QueueFree();
			_sprintBlurLayerHandle = null;
		}
		if (Godot.GodotObject.IsInstanceValid(_worldMuzzleLight))
		{
			_worldMuzzleLight.QueueFree();
			_worldMuzzleLight = null;
		}
	}

	/// <summary>
	/// Live preview in the editor viewport: when AdsTestMode is true the Target Position/Rotation
	/// is shifted by the test offsets. When toggled off, the rest pose is restored.
	/// Important: switch AdsTestMode OFF before saving the scene, otherwise the offset position
	/// gets persisted.
	/// </summary>
	private void UpdateEditorAdsPreview()
	{
		Node3D t = Target ?? this;
		if (t == null) { Dbg.Print("[ads-test] no target node"); return; }

		bool testMode = AdsTestMode;

		if (testMode && !_editorPrevAdsTestMode)
		{
			_editorRestPos = t.Position;
			_editorRestRotDeg = t.RotationDegrees;
			_editorRestFov = Camera?.Fov ?? 0f;
			_editorRestCaptured = true;
			SpawnCalibrationMarker();
			Dbg.Print($"[ads-test] enable on '{t.Name}' rest={_editorRestPos:F3} fov={_editorRestFov:F1}");
		}
		else if (!testMode && _editorPrevAdsTestMode && _editorRestCaptured)
		{
			t.Position = _editorRestPos;
			t.RotationDegrees = _editorRestRotDeg;
			if (Camera != null && _editorRestFov > 0f) Camera.Fov = _editorRestFov;
			_editorRestCaptured = false;
			DespawnCalibrationMarker();
			Dbg.Print($"[ads-test] disable, restored pos={_editorRestPos:F3} fov={_editorRestFov:F1}");
		}
		_editorPrevAdsTestMode = testMode;

		if (testMode && _editorRestCaptured)
		{
			t.Position = _editorRestPos + AdsTestPosOffset;
			t.RotationDegrees = _editorRestRotDeg + AdsTestRotOffset;
			if (Camera != null) Camera.Fov = AdsAffectsFov ? _editorRestFov * AdsFovScale : _editorRestFov;
			// Wenn der Marker fehlt obwohl wir testMode haben (z.B. nach Script-Reload mit aktivem
			// AdsTestMode → Edge-Detection nicht ausgelöst) → respawn lazy. Dann updaten.
			if (_calibrationMarker == null) SpawnCalibrationMarker();
			UpdateCalibrationMarkerPose();
		}
	}

	/// <summary>Spawns a red sphere and H/V crosshair lines as children of the camera at AdsCalibrationDistance meters forward. Owner=null so they are not saved into the scene.
	/// Layer wird auf die CullMask der Camera gesetzt — sonst rendern die Marker zwar, sind aber außerhalb
	/// der Camera-Sicht (Camera rendert nur Layer 2 = FPS-Layer, Default-MeshInstance ist Layer 1).</summary>
	/// <summary>Wenn das Weapon innerhalb eines SubViewport (own_world_3d=true) liegt, MUSS der Marker
	/// auch dort spawnen — sonst landet er in der Main-World und die SubViewport-Cam (die im 2D-Preview
	/// rendert) sieht ihn nicht. Wir walken von 'this' aufwärts; wenn wir einen SubViewport finden, dessen
	/// Camera3D nehmen. Sonst fallback auf das wired Camera-Field (= z.B. fps_camera in Main-World).</summary>
	private Camera3D ResolveMarkerCamera()
	{
		Node n = this;
		while (n != null)
		{
			if (n is SubViewport sv)
			{
				foreach (Node c in sv.GetChildren())
					if (c is Camera3D cam) return cam;
				break;
			}
			n = n.GetParent();
		}
		return Camera;
	}

	private void SpawnCalibrationMarker()
	{
		Camera3D markerCam = ResolveMarkerCamera();
		if (_calibrationMarker != null || markerCam == null)
		{
			GD.Print($"[ads-calib] SpawnCalibrationMarker SKIP — marker={(_calibrationMarker != null ? "exists" : "null")} camera={(markerCam != null ? markerCam.Name : "NULL")}");
			return;
		}

		uint cameraLayer = markerCam.CullMask != 0 ? markerCam.CullMask : 1u;
		GD.Print($"[ads-calib] Spawning marker at dist={AdsCalibrationDistance} size={AdsCalibrationSize} color={AdsCalibrationColor} cameraLayer={cameraLayer:X8} (camera.CullMask={markerCam.CullMask:X8}) parent={markerCam.Name}");

		_calibrationMarker = new MeshInstance3D
		{
			Name = "_AdsCalibrationMarker",
			Mesh = new SphereMesh { Radius = AdsCalibrationSize, Height = AdsCalibrationSize * 2f },
			MaterialOverride = MakeUnshadedMat(AdsCalibrationColor),
			Layers = cameraLayer,
		};
		markerCam.AddChild(_calibrationMarker);
		_calibrationMarker.Owner = null;

		_calibrationLineH = new MeshInstance3D
		{
			Name = "_AdsCalibrationLineH",
			Mesh = new BoxMesh { Size = new Vector3(100f, AdsCalibrationSize, AdsCalibrationSize) },
			MaterialOverride = MakeUnshadedMat(AdsCalibrationColor),
			Layers = cameraLayer,
		};
		markerCam.AddChild(_calibrationLineH);
		_calibrationLineH.Owner = null;

		_calibrationLineV = new MeshInstance3D
		{
			Name = "_AdsCalibrationLineV",
			Mesh = new BoxMesh { Size = new Vector3(AdsCalibrationSize, 100f, AdsCalibrationSize) },
			MaterialOverride = MakeUnshadedMat(AdsCalibrationColor),
			Layers = cameraLayer,
		};
		markerCam.AddChild(_calibrationLineV);
		_calibrationLineV.Owner = null;

		UpdateCalibrationMarkerPose();
	}

	/// <summary>Builds an unshaded, depth-test-disabled StandardMaterial3D in the given color.</summary>
	private static StandardMaterial3D MakeUnshadedMat(Color color) => new()
	{
		AlbedoColor = color,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		NoDepthTest = true,
	};

	/// <summary>Updates the calibration marker position, mesh dimensions and color from the current export values.</summary>
	private void UpdateCalibrationMarkerPose()
	{
		if (_calibrationMarker == null) return;
		var pos = new Vector3(0f, 0f, -AdsCalibrationDistance);
		_calibrationMarker.Position = pos;
		if (_calibrationMarker.Mesh is SphereMesh sm)
		{
			sm.Radius = AdsCalibrationSize;
			sm.Height = AdsCalibrationSize * 2f;
		}
		if (_calibrationMarker.MaterialOverride is StandardMaterial3D m) m.AlbedoColor = AdsCalibrationColor;

		float thick = AdsCalibrationSize;
		if (_calibrationLineH != null)
		{
			_calibrationLineH.Position = pos;
			if (_calibrationLineH.Mesh is BoxMesh bh) bh.Size = new Vector3(100f, thick, thick);
			if (_calibrationLineH.MaterialOverride is StandardMaterial3D mh) mh.AlbedoColor = AdsCalibrationColor;
		}
		if (_calibrationLineV != null)
		{
			_calibrationLineV.Position = pos;
			if (_calibrationLineV.Mesh is BoxMesh bv) bv.Size = new Vector3(thick, 100f, thick);
			if (_calibrationLineV.MaterialOverride is StandardMaterial3D mv) mv.AlbedoColor = AdsCalibrationColor;
		}
	}

	/// <summary>Removes the calibration marker and crosshair lines from the scene.</summary>
	private void DespawnCalibrationMarker()
	{
		if (_calibrationMarker != null) { _calibrationMarker.QueueFree(); _calibrationMarker = null; }
		if (_calibrationLineH != null) { _calibrationLineH.QueueFree(); _calibrationLineH = null; }
		if (_calibrationLineV != null) { _calibrationLineV.QueueFree(); _calibrationLineV = null; }
	}

	/// <summary>Accumulates relative mouse motion for the mouse-inertia layer. Suppressed when any settings menu is open.</summary>
	public override void _Input(InputEvent @event)
	{
		if (Engine.IsEditorHint()) return;
		if (SettingsMenu.IsAnyOpen) return;
		if (@event is InputEventMouseMotion mm)
			_mouseDeltaAccum += mm.Relative;
	}

	/// <summary>Handles debug toggle hotkeys for breathing, sprint sway and camera effects.</summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (Engine.IsEditorHint()) return;
		if (@event is not InputEventKey k || !k.Pressed || k.Echo) return;

		if (k.Keycode == BreathToggleKey) BreathingEnabled = !BreathingEnabled;
		else if (k.Keycode == SprintSwayToggleKey)
		{
			SprintSwayEnabled = !SprintSwayEnabled;
			Dbg.Print($"[weapon] SprintSway: {SprintSwayEnabled}");
		}
		else if (k.Keycode == CameraToggleKey)
		{
			CameraEffectsEnabled = !CameraEffectsEnabled;
			Dbg.Print($"[weapon] CameraEffects: {CameraEffectsEnabled}");
		}
	}

	/// <summary>Per-frame driver: edge-detects animation triggers, integrates springs, builds the
	/// additive pose layers and applies them to the weapon transform and camera. Skipped for
	/// non-local characters and in the editor (where only the ADS preview runs).</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("LocalAnimation._Process");
		if (Engine.IsEditorHint())
		{
			UpdateEditorAdsPreview();
			return;
		}

		if (_ownerCharCached == null) _ownerCharCached = FindOwnerCharacter();
		if (_ownerCharCached != null && (_ownerCharCached.IsServerAgent || _ownerCharCached.IsPuppet))
			return;

		if (_node == null) return;
		if (!_didWarmup && Muzzle != null && ActiveWeapon != null)
		{
			_didWarmup = true;
			WarmupTriggers();
		}
		float dt = (float)delta;

		_breathPhase = (_breathPhase + dt * Cl.BreathSpeed) % 1f;
		_breathBlend = Mathf.MoveToward(_breathBlend, BreathingEnabled ? 1f : 0f, Cl.BreathBlendSpeed * dt);

		bool inspectingNow = Movement?.IsInspecting ?? false;
		if (inspectingNow && !_lastSeenInspecting)
		{
			AnimTree?.Set(InspectRequestParam, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			_idleTimer = 0f;
		}
		_lastSeenInspecting = inspectingNow;

		bool reloadingNow = Movement?.IsReloading ?? false;
		if (reloadingNow && !_lastSeenReloading)
		{
			AnimTree?.Set(ArmsReloadRequestParam, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			WeaponAnimTree?.Set(WeaponReloadRequestParam, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			AnimTree?.Set(InspectRequestParam, (int)AnimationNodeOneShot.OneShotRequest.Abort);
			_idleTimer = 0f;
		}
		_lastSeenReloading = reloadingNow;

		bool inspectActive = AnimTree != null && AnimTree.Get(_inspectActiveParam).AsBool();
		bool reloadActive = (Movement?.IsReloading ?? false)
			|| (AnimTree != null && AnimTree.Get(_armsReloadActiveParam).AsBool());
		bool ikActive = !(inspectActive || reloadActive);

		if (FingerIk != null) FingerIk.Influence = ikActive ? 1f : 0.5f;

		if (FingerMove != null && ActiveWeapon != null)
		{
			if (!ikActive)
			{
				_fingerZOffset = 0f;
			}
			else
			{
				float fingerTarget = (Movement?.RecentlyFired ?? false) ? ActiveWeapon.FingerKickZ : 0f;
				_fingerZOffset = Mathf.Lerp(_fingerZOffset, fingerTarget, Mathf.Min(1f, ActiveWeapon.FingerKickRecovery * dt));
				FingerMove.Position = _fingerRestPos + new Vector3(0f, 0f, _fingerZOffset);
			}
		}

		if (Movement != null)
		{
			if (Movement.ShotIndex > _lastSeenShotIndex)
			{
				// Per-Trigger Sub-Sampling — die 10ms Peaks in LocalAnimation._Process landen praktisch
				// IMMER auf Fire-Frames wegen der Mesh/Particle-Spawns. Hier sehen wir welches Trigger
				// schuldig ist (MuzzleFlash spawnt z.B. OmniLight3D + GPUParticles → kann heavy sein).
				using (MiniProfiler.SampleClient("LocalAnimation.TriggerVisualWeaponKick")) TriggerVisualWeaponKick(Movement.LastShotPatternEntry, Movement.LastShotSpread);
				using (MiniProfiler.SampleClient("LocalAnimation.TriggerMuzzleFlash")) TriggerMuzzleFlash();
				using (MiniProfiler.SampleClient("LocalAnimation.TriggerMuzzleSmoke")) TriggerMuzzleSmoke();
				using (MiniProfiler.SampleClient("LocalAnimation.TriggerMuzzleSparks")) TriggerMuzzleSparks();
				using (MiniProfiler.SampleClient("LocalAnimation.TriggerMuzzleLight")) TriggerMuzzleLight();
				using (MiniProfiler.SampleClient("LocalAnimation.TriggerBulletTracer")) TriggerBulletTracer();
				using (MiniProfiler.SampleClient("LocalAnimation.TriggerShellEjection")) TriggerShellEjection();
				if (AnimTree != null)
					AnimTree.Set(InspectRequestParam, (int)AnimationNodeOneShot.OneShotRequest.Abort);
			}
			_lastSeenShotIndex = Movement.ShotIndex;
		}

		StepWeaponKickSpring(dt);
		StepJumpSpring(dt);
		StepAimPunchSmoothing(dt);
		StepMuzzleLightDecay(dt);

		float speed = CurrentVelocity.Length();
		Vector3 dir = speed > 0.01f ? CurrentVelocity / speed : Vector3.Zero;
		float backwardness = Mathf.Max(0f, dir.Z);
		float effSpeed = speed * Mathf.Lerp(1f, Cl.BackwardSpeedFactor, backwardness);
		float walkT = Mathf.Clamp(effSpeed / Mathf.Max(0.01f, Sv.WalkSpeed), 0f, 1f);
		float runT = Mathf.Clamp((effSpeed - Sv.WalkSpeed) / Mathf.Max(0.01f, Sv.SprintSpeed - Sv.WalkSpeed), 0f, 1f);
		bool isSprinting = Movement?.ActuallySprinting ?? false;

		float crouchTarget = Movement?.CrouchBlend ?? 0f;
		_crouchBlend = Mathf.MoveToward(_crouchBlend, crouchTarget, Sv.CrouchTransitionSpeed * dt);
		float crouchBobMul = Mathf.Lerp(1f, Cl.CrouchBobScale, _crouchBlend);

		float speedFreq;
		if (effSpeed <= Sv.ShiftSpeed)
			speedFreq = Mathf.Lerp(0f, Cl.BobFreqShift, effSpeed / Mathf.Max(0.01f, Sv.ShiftSpeed));
		else if (effSpeed <= Sv.WalkSpeed)
			speedFreq = Mathf.Lerp(Cl.BobFreqShift, Cl.BobFreqWalk, (effSpeed - Sv.ShiftSpeed) / Mathf.Max(0.01f, Sv.WalkSpeed - Sv.ShiftSpeed));
		else
			speedFreq = Mathf.Lerp(Cl.BobFreqWalk, Cl.BobFreqRun, runT);
		float currentBobFreq = speedFreq * Mathf.Lerp(1f, 0.7f, _crouchBlend);
		float currentAmpMul = Mathf.Lerp(1f, Cl.RunAmpMultiplier, runT);
		if (Footstep != null)
			_bobPhase = (Footstep.ContinuousPhase + 0.75f) * Mathf.Pi;
		else
			_bobPhase += dt * currentBobFreq * Mathf.Tau;
		bool inAir = _jumpTimer > 0f;
		float bobTarget = inAir ? 0f : walkT;
		float swayTarget = (inAir || !SprintSwayEnabled || !isSprinting) ? 0f : 1f;
		_bobBlend = Mathf.MoveToward(_bobBlend, bobTarget, Cl.BobBlendSpeed * dt);
		_swayBlend = Mathf.MoveToward(_swayBlend, swayTarget, Cl.SwayBlendSpeed * dt);
		_cameraBlend = Mathf.MoveToward(_cameraBlend, CameraEffectsEnabled ? 1f : 0f, Cl.BobBlendSpeed * dt);
		if (Footstep == null && _bobBlend <= 0.001f) _bobPhase = 0f;

		Vector3 dirRatio = dir * Mathf.Min(speed / Mathf.Max(0.01f, Sv.SprintSpeed), 1.2f);
		Vector3 springAccel = (dirRatio - _smoothedDirRatio) * Cl.DirectionLeanStiffness - _dirLeanSpringVel * Cl.DirectionLeanDamping;
		_dirLeanSpringVel += springAccel * dt;
		_smoothedDirRatio += _dirLeanSpringVel * dt;

		StepMouseInertia(dt);
		StepInertiaTilt(dt);
		StepBodyYawLag(dt);

		_airBlend = Mathf.MoveToward(_airBlend, _jumpTimer > 0f ? 1f : 0f, Cl.AirBlendSpeed * dt);

		bool firingRecently = Movement?.RecentlyFired ?? false;
		bool isActive = firingRecently || speed > 0.1f || _crouchBlend > 0.05f || _jumpTimer > 0f
						|| Mathf.Abs(_mouseInertia.X) + Mathf.Abs(_mouseInertia.Y) > 0.05f
						|| (Movement?.AdsBlend ?? 0f) > 0.01f;
		if (isActive) _idleTimer = 0f; else _idleTimer += dt;
		float lowerTarget = _idleTimer >= Cl.LowerIdleDelay ? 1f : 0f;
		float lowerRate = lowerTarget > _lowerBlend ? Cl.LowerBlendSpeed : Cl.LowerExitSpeed;
		_lowerBlend = Mathf.MoveToward(_lowerBlend, lowerTarget, lowerRate * dt);

		Vector3 breathPos = Vector3.Zero;
		Vector3 breathRot = Vector3.Zero;
		ApplyBreathing(ref breathPos, ref breathRot);
		Vector3 ambPos = Vector3.Zero;
		Vector3 ambRot = Vector3.Zero;
		ApplyDirectionLean(ref ambPos, ref ambRot);
		ApplyBob(ref ambPos, ref ambRot, currentAmpMul, crouchBobMul, runT);
		Vector3 swayRot = ApplySprintSway(ref ambPos, ref ambRot);
		ApplyMouseInertia(ref ambRot);
		ApplyBodyYawLag(ref ambRot);
		ApplyVelocityTilt(ref ambRot);

		Vector3 pos = Vector3.Zero;
		Vector3 rotDeg = Vector3.Zero;
		ApplyWeaponKick(ref pos, ref rotDeg);
		ApplyJumpAnim(ref pos, ref rotDeg);
		ApplyCrouchPose(ref pos, ref rotDeg);
		ApplySprintLowerPose(ref pos, ref rotDeg, dt);
		ApplyAirPose(ref pos, ref rotDeg);
		ApplyLowerPose(ref pos, ref rotDeg);

		float adsBlend = AdsTestMode ? 1f : (Movement?.AdsBlendVisual ?? 0f);
		float adsAmbientMul = ActiveWeapon?.AdsAmbientMul ?? 0.3f;
		float adsSuppress = Mathf.Lerp(1f, adsAmbientMul, adsBlend);
		float breathSwayMul = Movement?.BreathSwayMul ?? 1f;
		float breathBreathingMul = Movement?.BreathBreathingMul ?? 1f;
		float swaySuppress = Mathf.Lerp(1f, breathSwayMul, adsBlend);
		float breathingSuppress = Mathf.Lerp(1f, breathBreathingMul, adsBlend);
		float totalAmbSuppress = adsSuppress * swaySuppress;
		float totalBreathSuppress = adsSuppress * breathingSuppress;
		pos += breathPos * totalBreathSuppress;
		rotDeg += breathRot * totalBreathSuppress;
		pos += ambPos * totalAmbSuppress;
		rotDeg += ambRot * totalAmbSuppress;

		ApplyAdsPose(ref pos, ref rotDeg, adsBlend);
		ApplyDryFireWobble(ref pos, ref rotDeg, dt);

		ApplyCameraEffects(dt, swayRot * totalAmbSuppress, adsBlend);
		ApplyWeaponTransform(pos, rotDeg);
	}

	/// <summary>Triggers the visual weapon-kick animation. Called from _Process when Movement.DidFireThisFrame.</summary>
	private void TriggerVisualWeaponKick(Vector2 patternEntry, float spreadMag)
	{
		var w = ActiveWeapon;
		float impulseBase = 2.71828f * Mathf.Sqrt(w.WeaponKickStiffness);
		float adsBlend = Movement?.AdsBlendVisual ?? 0f;
		float impulseScaleRot = impulseBase * Mathf.Lerp(1f, w.AdsKickMul, adsBlend);
		float impulseScalePos = impulseBase * Mathf.Lerp(1f, w.AdsKickPosMul, adsBlend);

		float kickPitch = w.WeaponKickPitch * (1f + _visualRng.RandfRange(-1f, 1f) * 0.1f * w.WeaponRandomness);
		float kickYaw = w.WeaponKickYaw * Mathf.Sign(patternEntry.X) * Mathf.Min(1f, Mathf.Abs(patternEntry.X) * 2f)
						+ _visualRng.RandfRange(-1f, 1f) * w.WeaponKickYaw * w.WeaponRandomness * 0.5f;
		float kickRoll = _visualRng.RandfRange(-1f, 1f) * w.WeaponKickRoll;

		float wSpread = spreadMag * w.SpreadWeaponMul;
		float wSpreadPitch = _visualRng.RandfRange(-1f, 1f) * wSpread;
		float wSpreadYaw = _visualRng.RandfRange(-1f, 1f) * wSpread;

		_weaponKickRotVel += new Vector3(-(kickPitch + wSpreadPitch), kickYaw + wSpreadYaw, kickRoll) * impulseScaleRot;
		_weaponKickPosVel += new Vector3(0f, w.WeaponKickUp, w.WeaponKickBack) * impulseScalePos;

		if (Settings.CameraShake)
		{
			float camShakeImpulse = 2.71828f * Mathf.Sqrt(Cl.CamShakeStiffness) * w.CamShakeAmount;
			camShakeImpulse *= Mathf.Lerp(1f, w.AdsCamShakeMul, adsBlend);
			float shakePitch = Cl.CamShakeImpulsePitch * (0.6f + 0.4f * _camShakeRng.Randf());
			float shakeYaw = Cl.CamShakeImpulseYaw * _camShakeRng.RandfRange(-1f, 1f);
			float shakeRoll = Cl.CamShakeImpulseRoll * _camShakeRng.RandfRange(-1f, 1f);
			_camShakeRotVel += new Vector3(shakePitch, shakeYaw, shakeRoll) * camShakeImpulse;
		}
	}

	/// <summary>Triggers the muzzle flash via Restart() on the pre-configured GpuParticles3D node.
	/// Suppressed during ADS so iron-sights are not overpowered. Configuration is entirely in the
	/// inspector - the code only calls Restart.</summary>
	private void TriggerMuzzleFlash()
	{
		if (Movement != null && Movement.AdsBlendVisual > 0.5f) return;
		MuzzleFlashParticles?.Restart();
	}

	/// <summary>Forward sparks from the barrel - small particles that fly forward. Also skipped during ADS.</summary>
	private void TriggerMuzzleSparks()
	{
		if (Movement != null && Movement.AdsBlendVisual > 0.5f) return;
		MuzzleSparksParticles?.Restart();
	}

	private OmniLight3D _muzzleLight;
	private OmniLight3D _worldMuzzleLight;

	// Manual energy decay statt CreateTween — Tween allokiert pro Aufruf (Godot.Tween + Tweener) und
	// produzierte ~1ms Spike pro Shot in Auto-Fire. _muzzleLightTime decay'd in _Process linear gegen 0.
	private float _muzzleLightTime;
	private float _worldMuzzleLightTime;

	/// <summary>Brief point-light on shot. Pooled - the light is created once and ramped up + faded
	/// per shot. When the SubViewport setup is active there are TWO lights: one at the SubViewport
	/// muzzle (viewmodel hand highlight) and one in the real world at the world muzzle position
	/// (walls light up when firing).</summary>
	private void TriggerMuzzleLight()
	{
		if (Muzzle == null || MuzzleLightEnergy <= 0f) return;
		if (Movement != null && Movement.AdsBlendVisual > 0.5f) return;

		if (_muzzleLight == null || !Godot.GodotObject.IsInstanceValid(_muzzleLight))
		{
			_muzzleLight = new OmniLight3D
			{
				LightColor = MuzzleLightColor,
				OmniRange = MuzzleLightRange,
				ShadowEnabled = false,
				LightEnergy = 0f,
			};
			Muzzle.AddChild(_muzzleLight);
		}

		// Statt Tween: setze Time auf Duration, decay'd manuell im _Process (= _UpdateMuzzleLightDecay).
		_muzzleLight.LightColor = MuzzleLightColor;
		_muzzleLight.OmniRange = MuzzleLightRange;
		_muzzleLight.LightEnergy = MuzzleLightEnergy;
		_muzzleLightTime = MuzzleLightDuration;

		if (ViewmodelCamera != null && Camera != null)
		{
			if (_worldMuzzleLight == null || !Godot.GodotObject.IsInstanceValid(_worldMuzzleLight))
			{
				_worldMuzzleLight = new OmniLight3D
				{
					LightColor = MuzzleLightColor,
					OmniRange = MuzzleLightRange,
					ShadowEnabled = false,
					LightEnergy = 0f,
				};
				GetTree().Root.AddChild(_worldMuzzleLight);
			}
			_worldMuzzleLight.GlobalPosition = MuzzleWorldPosition;
			_worldMuzzleLight.LightColor = MuzzleLightColor;
			_worldMuzzleLight.OmniRange = MuzzleLightRange;
			_worldMuzzleLight.LightEnergy = MuzzleLightEnergy;
			_worldMuzzleLightTime = MuzzleLightDuration;
		}
	}

	/// <summary>World position of the muzzle for external consumers (audio, decals, etc.) -
	/// converts the SubViewport bone position into real world coordinates. In a single-camera
	/// setup this is simply Muzzle.GlobalPosition.</summary>
	public Vector3 MuzzleWorldPosition => Muzzle == null ? Vector3.Zero : SubviewToWorld(Muzzle).Origin;

	/// <summary>
	/// Converts a position/rotation that lives in the SubViewport world-space (e.g. muzzle bone,
	/// ejection-port bone - children of the SubViewport skeleton) into the REAL game world.
	/// Mechanic: the anchor in the SubViewport is <see cref="ViewmodelCamera"/> (typically at
	/// identity), the mirror in the real world is <see cref="Camera"/>. We compute the SubView
	/// transform relative to the sub-camera and apply it to the world camera. Without a
	/// SubViewport setup (ViewmodelCamera == null) the original GlobalTransform is returned
	/// (backward-compatible to single-camera setups).
	/// </summary>
	private Transform3D SubviewToWorld(Node3D n)
	{
		if (n == null) return Transform3D.Identity;
		if (ViewmodelCamera == null || Camera == null) return n.GlobalTransform;
		Transform3D camLocal = ViewmodelCamera.GlobalTransform.AffineInverse() * n.GlobalTransform;
		return Camera.GlobalTransform * camLocal;
	}

	/// <summary>Spawns a bullet tracer from the muzzle along LastShotDirection. Only fires every N-th
	/// shot. Endpoint determined by raycast so the tracer clips at the first wall instead of
	/// flying through.</summary>
	private void TriggerBulletTracer()
	{
		if (!TracerEnabled || Muzzle == null || Movement == null) return;
		int idx = Mathf.Max(0, Movement.ShotIndex - 1);
		if (TracerEveryNthShot > 1 && idx % TracerEveryNthShot != 0) return;

		Vector3 origin = SubviewToWorld(Muzzle).Origin;
		Vector3 dir = Movement.LastShotDirection;
		if (dir.LengthSquared() < 0.001f) return;

		Vector3 endpoint = origin + dir * TracerMaxRange;
		var space = GetWorld3D().DirectSpaceState;
		if (_tracerQuery == null)
		{
			_tracerQuery = PhysicsRayQueryParameters3D.Create(origin, endpoint);
			_tracerQuery.CollideWithBodies = true;
			_tracerQuery.CollideWithAreas = false;
		}
		_tracerQuery.From = origin;
		_tracerQuery.To = endpoint;
		if (space.IntersectRayInto(_tracerQuery, _tracerResult))
			endpoint = _tracerResult.GetPosition();

		// MultiMesh-Pool statt per-shot Node3D+CylinderMesh+StandardMaterial3D: erste First-Fire-Kost
		// von ~5.6ms (Mesh/Material/Node allocation + GPU upload) → ~0.05ms (just write Transform +
		// Color in MultiMesh-Instance). Fallback auf alt-Spawn nur wenn Pool nicht initialisiert.
		if (BulletTracerPool.Instance != null)
			BulletTracerPool.Instance.Emit(origin, endpoint, TracerColor, TracerSpeed, TracerStreakLength);
		else
			BulletTracer.Spawn(GetTree(), origin, endpoint, TracerColor, TracerWidth, TracerSpeed, TracerStreakLength);
	}

	private bool _shellPoolRegistered;

	private PhysicsRayQueryParameters3D _tracerQuery;
	private readonly PhysicsRayQueryResult3D _tracerResult = new();

	/// <summary>Registers the owning CharacterBody3D with the global ShellPool so shells exclude the player body. Idempotent.</summary>
	private void RegisterPlayerWithShellPool()
	{
		if (_shellPoolRegistered || ShellPool.Instance == null) return;
		Node n = this;
		while (n != null)
		{
			if (n is CharacterBody3D body) { ShellPool.Instance.AddExcludedBody(body); break; }
			n = n.GetParent();
		}
		_shellPoolRegistered = true;
	}

	/// <summary>Emits a casing into the global ShellPool. Direction comes from the camera basis
	/// (not from the EjectionPort bone basis - bones often use weird axis conventions). Spawn
	/// position comes from the EjectionPort. The player velocity is inherited.</summary>
	private void TriggerShellEjection()
	{
		if (!ShellEnabled || EjectionPort == null || ShellPool.Instance == null) return;
		RegisterPlayerWithShellPool();

		Basis portBasis = SubviewToWorld(EjectionPort).Basis.Orthonormalized();
		Basis dirBasis = (Camera != null && Godot.GodotObject.IsInstanceValid(Camera))
			? Camera.GlobalTransform.Basis.Orthonormalized()
			: portBasis;
		Vector3 baseDir = (dirBasis * ShellEjectDirection).Normalized();

		float spreadRad = Mathf.DegToRad(ShellSpreadAngleDeg);
		Vector3 anyOther = Mathf.Abs(baseDir.Y) > 0.99f ? Vector3.Right : Vector3.Up;
		Vector3 perp1 = baseDir.Cross(anyOther).Normalized();
		Vector3 perp2 = baseDir.Cross(perp1).Normalized();
		float coneAngle = _visualRng.RandfRange(0f, Mathf.Tau);
		float coneRadius = Mathf.Tan(spreadRad) * Mathf.Sqrt(_visualRng.Randf());
		Vector3 offset = (perp1 * Mathf.Cos(coneAngle) + perp2 * Mathf.Sin(coneAngle)) * coneRadius;
		Vector3 dir = (baseDir + offset).Normalized();

		Vector3 inheritedVel = Movement?.Velocity ?? Vector3.Zero;
		inheritedVel.Y = 0f;
		Vector3 velocity = inheritedVel + dir * ShellEjectSpeed * (0.85f + 0.30f * _visualRng.Randf());
		float tumbleSpeed = _visualRng.RandfRange(12f, 20f);
		float tiltY = _visualRng.RandfRange(-0.15f, 0.15f);
		Vector3 tumbleAxis = (dirBasis.X + dirBasis.Y * tiltY).Normalized();
		Vector3 angularVel = tumbleAxis * tumbleSpeed;

		Basis offsetBasis = Basis.FromEuler(new Vector3(
			Mathf.DegToRad(ShellInitialRotationDeg.X),
			Mathf.DegToRad(ShellInitialRotationDeg.Y),
			Mathf.DegToRad(ShellInitialRotationDeg.Z)));
		Vector3 spawnPos = SubviewToWorld(EjectionPort).Origin + portBasis * ShellSpawnOffset;
		Transform3D spawnTf = new(dirBasis * offsetBasis, spawnPos);

		ShellPool.Instance.Emit(spawnTf, velocity, angularVel, ShellLifetime);
	}

	/// <summary>Duplicates the smoke template per shot and releases it at the world muzzle
	/// position. This way every smoke cloud stays in world-space while the player keeps moving
	/// and multiple shots do not interfere (each burst gets its own particles node). The template
	/// should have Emitting=false (otherwise it would constantly spawn at its drop position).
	/// Auto-cleanup after lifetime + buffer.</summary>
	// MuzzleSmoke-Pool: lazy-init bei erstem Trigger, 3 Duplicate-Slots round-robin. Per-shot
	// Duplicate() (= deep clone GpuParticles3D + ParticleProcessMaterial = ~2ms first fire) wird
	// eliminiert. Pool persistiert; Slots werden via Emitting=true wieder-getriggert.
	private GpuParticles3D[] _smokePool;
	private int _smokePoolCursor;
	private const int SmokePoolSize = 3;

	private bool _didWarmup;

	/// <summary>Pre-allokiert alle First-Fire-Ressourcen damit der erste echte Schuss nicht stuttert.
	/// MuzzleSmoke-Pool-Slots, OmniLight3D, World-MuzzleLight, BulletTracerPool — alles erstellt
	/// + offscreen versteckt. Particle-Shader (Flash/Sparks/Smoke) werden via Restart() im Hidden-
	/// State pre-compiled. Ruft 1× beim ersten _Process-Frame nachdem Weapon+Muzzle resolved sind.</summary>
	private void WarmupTriggers()
	{
		// MuzzleSmoke-Pool pre-allokieren (3 Duplicates) — sonst passiert das beim ersten Schuss.
		if (MuzzleSmokeParticles != null && _smokePool == null)
		{
			_smokePool = new GpuParticles3D[SmokePoolSize];
			var root = GetTree().Root;
			for (int i = 0; i < SmokePoolSize; i++)
			{
				var slot = (GpuParticles3D)MuzzleSmokeParticles.Duplicate();
				root.AddChild(slot);
				slot.Owner = null;
				slot.Emitting = false;
				slot.OneShot = true;
				// Offscreen platzieren damit Shader-Compile passieren kann ohne visible pop. Far-below.
				slot.GlobalPosition = new Vector3(0f, -10000f, 0f);
				_smokePool[i] = slot;
			}
		}

		// MuzzleLight (OmniLight3D) pre-erstellen mit Energy=0 — keine sichtbare Light-Source aber
		// der Light-Node existiert + ist im Renderer registriert.
		if (Muzzle != null && _muzzleLight == null)
		{
			_muzzleLight = new OmniLight3D
			{
				LightColor = MuzzleLightColor,
				OmniRange = MuzzleLightRange,
				ShadowEnabled = false,
				LightEnergy = 0f,
			};
			Muzzle.AddChild(_muzzleLight);
		}

		// Particle-Shader pre-compile via Restart() — bei Emitting=false sollte kein visible pop sein.
		// Trick: Restart + sofort Emitting=false. Bei manchen Drivers wird der Shader trotzdem
		// compiled weil GPU-State touched.
		MuzzleFlashParticles?.Restart();
		if (MuzzleFlashParticles != null) MuzzleFlashParticles.Emitting = false;
		MuzzleSparksParticles?.Restart();
		if (MuzzleSparksParticles != null) MuzzleSparksParticles.Emitting = false;

		Dbg.Print("[LocalAnimation] WarmupTriggers done — smoke-pool, muzzle-light, particle-shaders pre-allocated");
	}

	private void TriggerMuzzleSmoke()
	{
		if (MuzzleSmokeParticles == null || Muzzle == null) return;

		// Lazy-init beim ersten Trigger weil MuzzleSmokeParticles abhängig von Weapon ist (kann bei
		// LocalAnimation._Ready noch null sein). Drei Slots — bei rapid fire bekommt der älteste neu.
		if (_smokePool == null)
		{
			_smokePool = new GpuParticles3D[SmokePoolSize];
			var root = GetTree().Root;
			for (int i = 0; i < SmokePoolSize; i++)
			{
				var slot = (GpuParticles3D)MuzzleSmokeParticles.Duplicate();
				root.AddChild(slot);
				slot.Owner = null;
				slot.Emitting = false;
				slot.OneShot = true;
				_smokePool[i] = slot;
			}
		}

		var smoke = _smokePool[_smokePoolCursor];
		_smokePoolCursor = (_smokePoolCursor + 1) % SmokePoolSize;
		if (smoke == null || !Godot.GodotObject.IsInstanceValid(smoke)) return;

		Transform3D worldMuzzle = SubviewToWorld(Muzzle);
		smoke.GlobalPosition = worldMuzzle.Origin;
		smoke.GlobalRotation = worldMuzzle.Basis.GetEuler();
		smoke.Restart();
	}

	/// <summary>Applies a jump impulse to the position/rotation springs and starts the air timer.
	/// autoLand=true causes auto-land after Cl.AirTime; otherwise the caller drives the landing.</summary>
	public void TriggerJump(bool autoLand = true)
	{
		if (_jumpTimer >= 0f) return;
		float scale = 2.71828f * Mathf.Sqrt(Cl.JumpKickStiffness);
		_jumpKickPosVel += new Vector3(0f, Cl.JumpImpulseUp, 0f) * scale;
		_jumpKickRotVel += new Vector3(-Cl.JumpPitchAmount, 0f, 0f) * scale;
		_jumpTimer = autoLand ? Cl.AirTime : 1f;
		_externalJump = !autoLand;
	}

	/// <summary>Applies a landing impulse scaled by impactSpeed. Tiny landings (step-down, settling) are skipped.</summary>
	public void TriggerLand(float impactSpeed = 0f)
	{
		if (impactSpeed < Cl.LandImpactMinSpeed) { _jumpTimer = -1f; _externalJump = false; return; }
		float t = Mathf.Min(impactSpeed / Mathf.Max(0.01f, Cl.LandImpactSpeedRef), Cl.LandImpactMaxScale);
		float scale = 2.71828f * Mathf.Sqrt(Cl.JumpKickStiffness);
		_jumpKickPosVel += new Vector3(0f, -Cl.LandImpulseDown * t, -Cl.LandImpulseForward * t) * scale;
		_jumpKickRotVel += new Vector3(Cl.LandPitchAmount * t, 0f, 0f) * scale;
		_jumpTimer = -1f;
		_externalJump = false;
	}

	/// <summary>Linear decay des MuzzleLight-Energy. Ersetzt die früheren Tween-Allocations
	/// (1 Tween + Tweener pro Shot = ~50KB GC pro Sekunde bei Auto-Fire). Wenn Time <= 0,
	/// Energy bleibt 0 und Decay schläft (kein Setter-Spam).</summary>
	private void StepMuzzleLightDecay(float dt)
	{
		if (_muzzleLightTime > 0f && _muzzleLight != null && Godot.GodotObject.IsInstanceValid(_muzzleLight))
		{
			_muzzleLightTime -= dt;
			if (_muzzleLightTime <= 0f)
			{
				_muzzleLightTime = 0f;
				_muzzleLight.LightEnergy = 0f;
			}
			else
			{
				_muzzleLight.LightEnergy = MuzzleLightEnergy * (_muzzleLightTime / MuzzleLightDuration);
			}
		}
		if (_worldMuzzleLightTime > 0f && _worldMuzzleLight != null && Godot.GodotObject.IsInstanceValid(_worldMuzzleLight))
		{
			_worldMuzzleLightTime -= dt;
			if (_worldMuzzleLightTime <= 0f)
			{
				_worldMuzzleLightTime = 0f;
				_worldMuzzleLight.LightEnergy = 0f;
			}
			else
			{
				_worldMuzzleLight.LightEnergy = MuzzleLightEnergy * (_worldMuzzleLightTime / MuzzleLightDuration);
			}
		}
	}

	/// <summary>Integrates the weapon-kick spring (pos + rot) and the high-frequency cam-shake spring for one frame.</summary>
	private void StepWeaponKickSpring(float dt)
	{
		float k = ActiveWeapon.WeaponKickStiffness, d = ActiveWeapon.WeaponKickDamping;
		Vector3 posAccel = -_weaponKickPos * k - _weaponKickPosVel * d;
		_weaponKickPosVel += posAccel * dt;
		_weaponKickPos += _weaponKickPosVel * dt;
		Vector3 rotAccel = -_weaponKickRot * k - _weaponKickRotVel * d;
		_weaponKickRotVel += rotAccel * dt;
		_weaponKickRot += _weaponKickRotVel * dt;

		float ck = Cl.CamShakeStiffness, cd = Cl.CamShakeDamping;
		Vector3 shakeAccel = -_camShakeRot * ck - _camShakeRotVel * cd;
		_camShakeRotVel += shakeAccel * dt;
		_camShakeRot += _camShakeRotVel * dt;
	}

	/// <summary>Integrates the jump/landing spring (pos + rot) and advances the auto-land timer for one frame.</summary>
	private void StepJumpSpring(float dt)
	{
		if (_jumpTimer > 0f && !_externalJump)
		{
			_jumpTimer -= dt;
			if (_jumpTimer <= 0f) TriggerLand();
		}
		float k = Cl.JumpKickStiffness, d = Cl.JumpKickDamping;
		Vector3 posAccel = -_jumpKickPos * k - _jumpKickPosVel * d;
		_jumpKickPosVel += posAccel * dt;
		_jumpKickPos += _jumpKickPosVel * dt;
		Vector3 rotAccel = -_jumpKickRot * k - _jumpKickRotVel * d;
		_jumpKickRotVel += rotAccel * dt;
		_jumpKickRot += _jumpKickRotVel * dt;
	}

	/// <summary>LP filter: smoothed version of Movement.AimPunch for the camera (visual only).</summary>
	private void StepAimPunchSmoothing(float dt)
	{
		Vector3 target = Movement?.AimPunch ?? Vector3.Zero;
		_aimPunchSmoothed = _aimPunchSmoothed.Lerp(target, Mathf.Min(1f, ActiveWeapon.AimPunchSmoothing * dt));
	}

	/// <summary>Accumulates mouse delta into the inertia state and smooths it asymmetrically (fast in, slow out) for a heavy-weapon feel.</summary>
	private void StepMouseInertia(float dt)
	{
		_mouseInertia.Y += _mouseDeltaAccum.X * Cl.MouseInertiaYaw;
		_mouseInertia.X += _mouseDeltaAccum.Y * Cl.MouseInertiaPitch;
		_mouseDeltaAccum = Vector2.Zero;
		_mouseInertia.X = Mathf.Clamp(_mouseInertia.X, -Cl.MouseInertiaMaxPitch, Cl.MouseInertiaMaxPitch);
		_mouseInertia.Y = Mathf.Clamp(_mouseInertia.Y, -Cl.MouseInertiaMaxYaw, Cl.MouseInertiaMaxYaw);
		_mouseInertia = _mouseInertia.Lerp(Vector3.Zero, Mathf.Min(1f, Cl.MouseInertiaRecovery * dt));
		bool building = _mouseInertia.LengthSquared() > _mouseInertiaSmoothed.LengthSquared();
		float smoothRate = building ? Cl.MouseInertiaSmoothingIn : Cl.MouseInertiaSmoothingOut;
		_mouseInertiaSmoothed = _mouseInertiaSmoothed.Lerp(_mouseInertia, Mathf.Min(1f, smoothRate * dt));
	}

	/// <summary>Builds a velocity-derived tilt for counter-strafe feel and lerps it back to zero over time.</summary>
	private void StepInertiaTilt(float dt)
	{
		Vector3 accel = dt > 0.0001f ? (CurrentVelocity - _prevVelocity) / dt : Vector3.Zero;
		_prevVelocity = CurrentVelocity;
		_inertiaTilt += new Vector3(-accel.Z, 0f, accel.X) * Cl.InertiaTiltStrength * dt;
		_inertiaTilt.X = Mathf.Clamp(_inertiaTilt.X, -Cl.InertiaTiltMax, Cl.InertiaTiltMax);
		_inertiaTilt.Z = Mathf.Clamp(_inertiaTilt.Z, -Cl.InertiaTiltMax, Cl.InertiaTiltMax);
		_inertiaTilt = _inertiaTilt.Lerp(Vector3.Zero, Mathf.Min(1f, Cl.InertiaTiltRecovery * dt));
	}

	/// <summary>
	/// Body-yaw lag: the weapon trails behind quick body rotations. Measures yaw rate (deg/s),
	/// computes the target lag as a counter-rotation and smooths it.
	/// </summary>
	private void StepBodyYawLag(float dt)
	{
		if (!_bodyYawInit) { _prevBodyYaw = CurrentBodyYaw; _bodyYawInit = true; }
		float yawDelta = Mathf.AngleDifference(_prevBodyYaw, CurrentBodyYaw);
		_prevBodyYaw = CurrentBodyYaw;
		float yawRateDeg = Mathf.RadToDeg(yawDelta / Mathf.Max(0.0001f, dt));
		float targetLag = Mathf.Clamp(-yawRateDeg * Cl.BodyYawLagStrength, -Cl.BodyYawLagMax, Cl.BodyYawLagMax);
		_bodyYawLag = Mathf.Lerp(_bodyYawLag, targetLag, Mathf.Min(1f, Cl.BodyYawLagSmoothing * dt));
	}

	/// <summary>Breathing layer: applies cyclic chest-rise offset and pitch/roll to pos/rotDeg additively.</summary>
	private void ApplyBreathing(ref Vector3 pos, ref Vector3 rotDeg)
	{
		if (_breathBlend <= 0f) return;
		float b = BreathCurve(_breathPhase, Cl.InhaleFraction);
		pos += new Vector3(0f, b * Cl.BreathPosAmount, -Mathf.Max(b, 0f) * Cl.BreathForwardAmount) * _breathBlend;
		rotDeg += new Vector3(-b * Cl.BreathRotAmount, 0f, b * Cl.BreathRotAmount * 0.4f) * _breathBlend;
	}

	/// <summary>Direction-lean layer: tilts weapon by strafe/forward inputs for natural body-motion feel.</summary>
	private void ApplyDirectionLean(ref Vector3 pos, ref Vector3 rotDeg)
	{
		if (!Settings.DirectionLean) return;
		float strafe = _smoothedDirRatio.X;
		float forward = -_smoothedDirRatio.Z;
		pos += new Vector3(
			strafe * Cl.StrafeLeanPos,
			-Mathf.Max(0f, forward) * Cl.ForwardLeanPosDown + Mathf.Max(0f, -forward) * Cl.ForwardLeanPosDown * 0.6f,
			-forward * Cl.ForwardLeanPosForward
		);
		rotDeg += new Vector3(-forward * Cl.ForwardLeanPitch, 0f, -strafe * Cl.StrafeLeanRoll);
	}

	/// <summary>View-bob layer: horizontal/vertical sin waves on pos and rotDeg, sharpened at sprint speeds.</summary>
	private void ApplyBob(ref Vector3 pos, ref Vector3 rotDeg, float ampMul, float crouchBobMul, float runT)
	{
		if (!Settings.ViewBob) return;
		if (_bobBlend <= 0f) return;
		float horiz = Mathf.Sin(_bobPhase);
		float vRaw = Mathf.Sin(_bobPhase * 2f);
		float vert = Mathf.Lerp(vRaw, vRaw * vRaw * vRaw, runT * Cl.RunSharpness);
		float amp = _bobBlend * ampMul * crouchBobMul;
		pos += new Vector3(horiz * Cl.BobHorizontalAmount, vert * Cl.BobVerticalAmount, 0f) * amp;
		rotDeg += new Vector3(vert * Cl.BobPitchAmount, 0f, -horiz * Cl.BobRollAmount) * amp;
	}

	/// <summary>Sprint-sway layer: figure-eight motion while sprinting. Returns the rotation-only contribution for camera feedback.</summary>
	private Vector3 ApplySprintSway(ref Vector3 pos, ref Vector3 rotDeg)
	{
		if (!Settings.SprintSway) return Vector3.Zero;
		if (_swayBlend <= 0f) return Vector3.Zero;
		float swayPhase = _bobPhase * Cl.SwayFreqMul;
		float h = Mathf.Sin(swayPhase);
		float v = Mathf.Sin(swayPhase * 2f);
		float d = Mathf.Cos(swayPhase);
		pos += new Vector3(h * Cl.SwayHorizontal, v * Cl.SwayVertical, -d * Cl.SwayDepth) * _swayBlend;
		Vector3 swayRot = new Vector3(v * Cl.SwayPitch, -h * Cl.SwayYaw, -h * Cl.SwayRoll) * _swayBlend;
		rotDeg += swayRot;
		return swayRot;
	}

	/// <summary>Weapon-kick layer: adds the spring-integrated kick offsets to pos/rotDeg.</summary>
	private void ApplyWeaponKick(ref Vector3 pos, ref Vector3 rotDeg)
	{
		pos += _weaponKickPos;
		rotDeg += _weaponKickRot;
	}

	/// <summary>Jump/land animation layer: applies the jump-kick spring; suppressed during ADS so the iron-sight blend is not jittered by the lingering landing oscillation.</summary>
	private void ApplyJumpAnim(ref Vector3 pos, ref Vector3 rotDeg)
	{
		float adsSup = 1f - (Movement?.AdsBlendVisual ?? 0f);
		pos += _jumpKickPos * adsSup;
		rotDeg += _jumpKickRot * adsSup;
	}

	/// <summary>Crouch pose layer: pulls the weapon back and inward; suppressed during ADS where the ADS pose owns the weapon position.</summary>
	private void ApplyCrouchPose(ref Vector3 pos, ref Vector3 rotDeg)
	{
		float adsSup = 1f - (Movement?.AdsBlendVisual ?? 0f);
		float blend = _crouchBlend * adsSup;
		pos.X -= Cl.CrouchWeaponInward * blend;
		pos.Y -= Cl.CrouchWeaponDrop * blend;
		pos.Z += Cl.CrouchWeaponBack * blend;
		rotDeg.X += Cl.CrouchWeaponPitch * blend;
		rotDeg.Y += Cl.CrouchWeaponYaw * blend;
		rotDeg.Z += Cl.CrouchWeaponRoll * blend;
	}

	/// <summary>Air-pose layer: small upward drift and pitch tilt while in the air; suppressed during ADS so iron-sights stay aligned mid-jump.</summary>
	private void ApplyAirPose(ref Vector3 pos, ref Vector3 rotDeg)
	{
		float adsSup = 1f - (Movement?.AdsBlendVisual ?? 0f);
		pos.Y += Cl.AirDriftUp * _airBlend * adsSup;
		rotDeg.X -= Cl.AirPitchTilt * _airBlend * adsSup;
	}

	/// <summary>
	/// Sprint-lower pose: weapon drops down and sideways while sprinting; muzzle tilts upward.
	/// Blend comes directly from <c>MovementController.WeaponRaiseBlend</c> (server state).
	/// While the weapon is lowered, FireStep blocks firing.
	///
	/// Side mirror: the side multiplier (-1..+1) moves continuously with strafe strength, driven
	/// by a spring for overshoot/swing on direction change. Forward / strafe-right -> side = +1
	/// (default pose), strafe-left lerps towards -1 (mirrored). Pos.X / Yaw / Roll are multiplied
	/// by side; pitch / PosDown / PosBack are fixed.
	/// </summary>
	private void ApplySprintLowerPose(ref Vector3 pos, ref Vector3 rotDeg, float dt)
	{
		float lower = 1f - (Movement?.WeaponRaiseBlend ?? 1f);

		float strafe = _smoothedDirRatio.X;
		float forward = -_smoothedDirRatio.Z;

		float strafeNorm = Mathf.Clamp(-strafe / Mathf.Max(0.001f, Cl.SprintLowerSideStrafeRange), 0f, 1f);
		float sideTarget = Mathf.Lerp(1f, -1f, strafeNorm);

		if (lower <= 0.001f)
		{
			_sprintSide = 1f;
			_sprintSideVel = 0f;
			return;
		}

		float springAccel = (sideTarget - _sprintSide) * Cl.SprintLowerSideStiffness - _sprintSideVel * Cl.SprintLowerSideDamping;
		_sprintSideVel += springAccel * dt;
		_sprintSide += _sprintSideVel * dt;
		float side = _sprintSide;

		pos.X += Cl.SprintLowerPosRight * lower * side;
		pos.Y -= Cl.SprintLowerPosDown * lower;
		pos.Z += Cl.SprintLowerPosBack * lower;
		rotDeg.X += Cl.SprintLowerPitch * lower;
		rotDeg.Y += Cl.SprintLowerYaw * lower * side;
		rotDeg.Z += Cl.SprintLowerRoll * lower * side;

		rotDeg.X += -forward * Cl.SprintLowerForwardPitch * lower;
	}

	/// <summary>Mouse-inertia layer: adds the smoothed mouse-inertia rotation to rotDeg.</summary>
	private void ApplyMouseInertia(ref Vector3 rotDeg)
	{
		if (!Settings.MouseInertia) return;
		rotDeg += new Vector3(_mouseInertiaSmoothed.X, _mouseInertiaSmoothed.Y, -_mouseInertiaSmoothed.Y * Cl.MouseInertiaRollMul);
	}

	/// <summary>Body-yaw-lag layer: adds the lag yaw offset to rotDeg.</summary>
	private void ApplyBodyYawLag(ref Vector3 rotDeg)
	{
		rotDeg.Y += _bodyYawLag;
	}

	/// <summary>Velocity-tilt layer: adds the inertia-tilt X/Z to rotDeg.</summary>
	private void ApplyVelocityTilt(ref Vector3 rotDeg)
	{
		rotDeg += new Vector3(_inertiaTilt.X, 0f, _inertiaTilt.Z);
	}

	/// <summary>Idle "lower" pose: gently drops the weapon when the player is idle for a while.</summary>
	private void ApplyLowerPose(ref Vector3 pos, ref Vector3 rotDeg)
	{
		pos += new Vector3(0f, -Cl.LowerOffsetDown * _lowerBlend, -Cl.LowerOffsetForward * _lowerBlend);
		rotDeg += new Vector3(Cl.LowerPitch * _lowerBlend, 0f, -Cl.LowerRoll * _lowerBlend);
	}

	/// <summary>
	/// ADS pose: weapon slides linearly to the iron-sight position. Offsets come from WeaponStats
	/// - or from the AdsTest* exports when <see cref="AdsTestMode"/> is active (for live tuning in
	/// the inspector). The CrouchBlend correction is added additively (the camera drops on crouch
	/// so without an add the iron-sights would drift).
	/// </summary>
	private void ApplyAdsPose(ref Vector3 pos, ref Vector3 rotDeg, float adsBlend)
	{
		if (adsBlend <= 0.001f) return;
		float cb = Movement?.CrouchBlend ?? 0f;
		Vector3 posOff, rotOff;
		if (Weapon != null)
		{
			posOff = AdsTestPosOffset + AdsTestCrouchPosAdd * cb;
			rotOff = AdsTestRotOffset + AdsTestCrouchRotAdd * cb;
		}
		else
		{
			posOff = (ActiveWeapon?.AdsPosOffset ?? Vector3.Zero) + (ActiveWeapon?.AdsCrouchPosAdd ?? Vector3.Zero) * cb;
			rotOff = (ActiveWeapon?.AdsRotOffset ?? Vector3.Zero) + (ActiveWeapon?.AdsCrouchRotAdd ?? Vector3.Zero) * cb;
		}
		pos += posOff * adsBlend;
		rotDeg += rotOff * adsBlend;
	}

	/// <summary>Dry-fire wobble: when the player pulls the trigger on an empty magazine the viewmodel
	/// gets a short, fast jiggle so the empty state is immediately readable. Triggered on the rising
	/// edge of <see cref="MovementController.DidDryFireThisFrame"/>; amplitude decays exponentially
	/// over roughly half a second.</summary>
	private void ApplyDryFireWobble(ref Vector3 pos, ref Vector3 rotDeg, float dt)
	{
		bool dryNow = Movement?.DidDryFireThisFrame ?? false;
		if (dryNow && !_lastSeenDryFire)
		{
			_dryFireWobbleAmp = 1f;
			_dryFireWobblePhase = 0f;
		}
		_lastSeenDryFire = dryNow;

		if (_dryFireWobbleAmp <= 0.001f) return;
		_dryFireWobbleAmp *= Mathf.Exp(-dt * 7f);
		_dryFireWobblePhase += dt * 5.5f;
		float t = _dryFireWobblePhase * Mathf.Tau;
		float w = _dryFireWobbleAmp;
		pos += new Vector3(Mathf.Sin(t * 1.3f) * 0.0018f, Mathf.Sin(t) * 0.0028f, 0f) * w;
		rotDeg += new Vector3(Mathf.Sin(t * 1.7f) * 0.22f, Mathf.Sin(t * 0.9f) * 0.16f, Mathf.Sin(t * 1.1f) * 0.32f) * w;
	}

	/// <summary>Applies FOV, sprint-peripheral blur, ADS DOF/vignette and rotational kick/sway/shake to the camera.</summary>
	private void ApplyCameraEffects(float dt, Vector3 swayRot, float adsBlend)
	{
		if (Camera == null) return;

		bool sprinting = Movement?.ActuallySprinting ?? false;
		float sprintTarget = (CameraEffectsEnabled && sprinting) ? 1f : 0f;
		_sprintFovBlend = Mathf.Lerp(_sprintFovBlend, sprintTarget, Mathf.Min(1f, Cl.FovBlendSpeed * dt));
		float sprintEased = _sprintFovBlend * _sprintFovBlend * (3f - 2f * _sprintFovBlend);
		float baseFov = Cl.Fov + Cl.FovBoost * sprintEased;

		if (_sprintBlurRect != null)
		{
			bool show = sprintEased > 0.002f && Settings.MotionBlur;
			_sprintBlurRect.Visible = show;
			if (show)
				_sprintBlurMat.SetShaderParameter(_shaderSprintParam, sprintEased);
		}
		if (AdsAffectsFov && Settings.AdsFovZoom)
		{
			float adsFov = Weapon != null ? baseFov * AdsFovScale : (ActiveWeapon?.AdsFov ?? baseFov * 0.8f);
			Camera.Fov = Mathf.Lerp(baseFov, adsFov, adsBlend);
		}
		else
		{
			Camera.Fov = baseFov;
		}
		if (ViewmodelCamera != null) ViewmodelCamera.Fov = Camera.Fov;

		bool dofWanted = adsBlend > 0.01f && Settings.AdsDepthOfField;
		float dofDist = Mathf.Lerp(0.05f, 0.2f, adsBlend);
		float dofAmt = Mathf.Lerp(0.05f, 0.07f, adsBlend);
		ApplyDofTo(Camera, dofWanted, dofDist, dofAmt);
		ApplyDofTo(ViewmodelCamera, dofWanted, dofDist, dofAmt);

		if (ViewmodelContainer?.Material is ShaderMaterial blurMat)
			blurMat.SetShaderParameter("ads_blend", dofWanted ? adsBlend : 0f);

		FeedAdsBlendToPostFx(adsBlend);

		if (Dbg.Enabled)
		{
			if (adsBlend > 0.5f && !_adsDbgArmed)
			{
				_adsDbgArmed = true;
				bool shared = ViewmodelCamera != null && Camera.Attributes == ViewmodelCamera.Attributes;
				Dbg.Print($"[ads-dof] blend={adsBlend:F2} settingEnabled={Settings.AdsDepthOfField} " +
					$"worldAttrs={Camera.Attributes?.GetType().Name ?? "null"} " +
					$"vmAttrs={ViewmodelCamera?.Attributes?.GetType().Name ?? "null"} shared={shared}");
			}
			else if (adsBlend < 0.1f) _adsDbgArmed = false;
		}

		float camKickAdsScale = Mathf.Lerp(1f, ActiveWeapon.AdsCameraKickMul, adsBlend);
		Vector3 aimPunchCam = new Vector3(-_aimPunchSmoothed.X, _aimPunchSmoothed.Y, _aimPunchSmoothed.Z);
		Vector3 effectsOffset = swayRot * Cl.CameraSwayMul * _cameraBlend
			+ aimPunchCam * ActiveWeapon.CameraAimPunchMul * camKickAdsScale
			+ _camShakeRot;
		Camera.RotationDegrees += effectsOffset - _appliedCamRot;
		_appliedCamRot = effectsOffset;
	}

	/// <summary>Writes the final pos/rotDeg to the weapon node. Handles both the camera-child setup and the sibling/SubViewport setup with a camera-local anchor.</summary>
	private void ApplyWeaponTransform(Vector3 pos, Vector3 rotDeg)
	{
		if (_weaponIsCameraChild || Camera == null)
		{
			_node.Position = _basePos + pos;
			_node.RotationDegrees = _baseRotDeg + rotDeg;
		}
		else
		{
			Basis animBasis = Basis.FromEuler(new Vector3(
				Mathf.DegToRad(rotDeg.X),
				Mathf.DegToRad(rotDeg.Y),
				Mathf.DegToRad(rotDeg.Z)));
			Camera3D anchor = ViewmodelCamera ?? Camera;
			_node.GlobalTransform = anchor.GlobalTransform * new Transform3D(animBasis, pos) * _weaponBaseInCamSpace;
		}
	}

	/// <summary>Returns a [-1,1] breath curve: smoothstep inhale of length inhaleFraction, then smoothstep exhale across the rest.</summary>
	private static float BreathCurve(float t, float inhaleFraction)
	{
		if (t < inhaleFraction)
		{
			float u = t / inhaleFraction;
			return Smoothstep(u) * 2f - 1f;
		}
		else
		{
			float u = (t - inhaleFraction) / (1f - inhaleFraction);
			return 1f - Smoothstep(u) * 2f;
		}
	}

	/// <summary>Hermite-style smoothstep on the clamped [0,1] range.</summary>
	private static float Smoothstep(float x)
	{
		x = Mathf.Clamp(x, 0f, 1f);
		return x * x * (3f - 2f * x);
	}
}
