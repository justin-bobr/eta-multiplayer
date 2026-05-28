using Godot;
using System.Collections.Generic;

/// <summary>
/// Local client character — the owning player's body. Instantiated by
/// <see cref="NetMain.TryInitializeLocalPlayer"/> from <c>res://character/local_player.tscn</c> once
/// SpawnAck is received. Sets the mode flags (<c>IsLocalPlayer=true</c>, <c>IsServerAgent=false</c>,
/// <c>IsPuppet=false</c>) so the local-player code path runs. Holds all local-only fields and logic:
/// FPS and third-person cameras, FpsVisual + shadow tuning, third-person spring-arm collision + ADS
/// blend, mouse input handlers (mouse-look, fire-mode toggle, view-mode toggle), aim guide for grenade
/// trajectory preview, third-person foot IK ground snap, and mouse pitch state. PlayerCore contains
/// only the shared simulation (movement / hitscan / mantle / crouch / footsteps / grenade) plus puppet
/// and server specifics. All local-only methods are implemented here as overrides
/// (UpdateAimGuide / UpdateFootIk / UpdateTpsCameraCollision / ApplyViewMode / ActiveCamera).
/// </summary>
public partial class LocalPlayer : PlayerCore
{
	[ExportGroup("Cameras / Visuals")]
	[Export] public Camera3D Camera;
	[Export] public Camera3D TpsCamera;
	[Export] public Node3D FpsVisual;

	[ExportGroup("TPS Shadow (FPS-Mode)")]
	[Export] public GeometryInstance3D.ShadowCastingSetting TpsShadowInFpsMode = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
	[Export] public bool TpsLowerBodyShadowOnly = false;
	[Export] public Vector3 TpsShadowOffsetInFps = Vector3.Zero;
	[Export(PropertyHint.Range, "0.5,2.5,0.05")] public float TpsShadowScaleInFps = 1.0f;

	[ExportGroup("TPS Camera Collision")]
	[Export] public float TpsCamWallMargin = 0.15f;
	[Export] public uint TpsCamCollisionMask = 1;
	[Export] public float TpsCamSmoothRate = 12f;

	[ExportGroup("TPS ADS Camera")]
	[Export(PropertyHint.Range, "0,1,0.05")] public float TpsAdsCloserFactor = 0.45f;
	[Export] public float TpsAdsFov = 50f;
	[Export] public float TpsAdsLerpRate = 8f;

	[ExportGroup("Input — LocalPlayer Only")]
	[Export] public Key MouseCaptureToggleKey = Key.Escape;
	[Export] public Key FireModeToggleKey = Key.G;
	[Export] public Key UnlimitedAmmoToggleKey = Key.F1;
	[Export] public Key ViewModeToggleKey = Key.F7;
	[Export] public bool MouseLookEnabled = true;
	[Export] public bool CaptureMouseOnStart = true;

	private float _pitch;

	private Vector3 _tpsCamRestLocal;
	private bool _tpsCamRestCached;
	private float _tpsCamBaseFov;
	private float _tpsAdsBlend;

	private Vector3 _tpsVisualOrigPos;
	private Vector3 _tpsVisualOrigScale = Vector3.One;
	private bool _tpsVisualOrigPosCached;

	private GrenadeAimGuide _aimGuide;
	private readonly List<Vector3> _aimPath = new();
	private int _aimDbg;

	/// <summary>Derived from the concrete type — a LocalPlayer instance is by definition the local player.</summary>
	public override bool IsLocalPlayer => true;

	/// <summary>Caches the third-person rest position and base FOV, captures the mouse, and adds the aim guide.</summary>
	public override void _Ready()
	{
		base._Ready();

		if (TpsCamera != null)
		{
			_tpsCamRestLocal = TpsCamera.Position;
			_tpsCamRestCached = true;
			_tpsCamBaseFov = TpsCamera.Fov;
		}

		if (CaptureMouseOnStart)
			Input.MouseMode = Input.MouseModeEnum.Captured;

		_aimGuide = new GrenadeAimGuide();
		GetParent().CallDeferred(Node.MethodName.AddChild, _aimGuide);
	}

	/// <summary>Active camera used by logic reads (shoot origin, grenade spawn, HUD FOV).</summary>
	public override Camera3D ActiveCamera =>
		(ViewMode == ViewMode.Tps && TpsCamera != null) ? TpsCamera : Camera;

	/// <summary>Applies the live third-person shadow offset and scale while in FPS mode each frame.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("LocalPlayer._Process");
		base._Process(delta);
		if (ViewMode == ViewMode.Fps && TpsVisual != null && _tpsVisualOrigPosCached)
		{
			TpsVisual.Position = _tpsVisualOrigPos + TpsShadowOffsetInFps;
			TpsVisual.Scale = _tpsVisualOrigScale * TpsShadowScaleInFps;
		}
	}

	/// <summary>Switches between FPS / TPS / Disabled cameras and applies shadow tuning.</summary>
	protected override void ApplyViewMode()
	{
		bool wantTps = ViewMode == ViewMode.Tps && TpsCamera != null;
		bool wantDisabled = ViewMode == ViewMode.Disabled;
		bool wantFps = !wantTps && !wantDisabled;

		if (ViewMode == ViewMode.Tps && TpsCamera == null)
			GD.PushWarning("[LocalPlayer] ViewMode=Tps but TpsCamera not wired — staying in FPS.");

		if (Camera != null) Camera.Current = wantFps;
		if (TpsCamera != null) TpsCamera.Current = wantTps;

		if (FpsVisual != null) SetLayers(FpsVisual, 1u << 1);
		if (TpsVisual != null) SetLayers(TpsVisual, 1u << 0);

		if (TpsVisual != null && !_tpsVisualOrigPosCached)
		{
			_tpsVisualOrigPos = TpsVisual.Position;
			_tpsVisualOrigScale = TpsVisual.Scale;
			_tpsVisualOrigPosCached = true;
		}

		if (wantFps)
		{
			if (FpsVisual != null) { FpsVisual.Visible = true; SetShadowMode(FpsVisual, GeometryInstance3D.ShadowCastingSetting.Off); }
			if (TpsVisual != null)
			{
				bool needsActive = TpsShadowInFpsMode != GeometryInstance3D.ShadowCastingSetting.Off;
				TpsVisual.Visible = needsActive;
				TpsVisual.Position = _tpsVisualOrigPos + TpsShadowOffsetInFps;
				TpsVisual.Scale = _tpsVisualOrigScale * TpsShadowScaleInFps;
				if (TpsLowerBodyShadowOnly && TpsShadowInFpsMode == GeometryInstance3D.ShadowCastingSetting.ShadowsOnly)
					SetShadowModeFiltered(TpsVisual);
				else
				{
					RestoreFilteredVisibility(TpsVisual);
					SetShadowMode(TpsVisual, TpsShadowInFpsMode);
				}
			}
		}
		else if (wantTps)
		{
			if (FpsVisual != null) FpsVisual.Visible = false;
			if (TpsVisual != null)
			{
				TpsVisual.Visible = true;
				TpsVisual.Position = _tpsVisualOrigPos;
				TpsVisual.Scale = _tpsVisualOrigScale;
				RestoreFilteredVisibility(TpsVisual);
				SetShadowMode(TpsVisual, GeometryInstance3D.ShadowCastingSetting.On);
			}
		}
		else
		{
			if (FpsVisual != null) FpsVisual.Visible = false;
			if (TpsVisual != null) TpsVisual.Visible = false;
		}
	}

	/// <summary>Third-person spring-arm + ADS blend (FOV + closer factor). Skipped while not in TPS mode.</summary>
	protected override void UpdateTpsCameraCollision()
	{
		if (!_tpsCamRestCached || TpsCamera == null || HeadPitch == null) return;
		if (ViewMode != ViewMode.Tps) return;

		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return;

		float dt = _fixedDt;

		bool adsHeld = !InputGate.Blocked && Input.IsActionPressed(InputActions.Ads);
		_tpsAdsBlend = Mathf.Lerp(_tpsAdsBlend, adsHeld ? 1f : 0f, 1f - Mathf.Exp(-TpsAdsLerpRate * dt));

		Vector3 adsLocal = _tpsCamRestLocal.Lerp(Vector3.Zero, TpsAdsCloserFactor * _tpsAdsBlend);
		Vector3 worldDesired = HeadPitch.GlobalTransform * adsLocal;
		Vector3 pivot = HeadPitch.GlobalPosition;

		_rayQuery.From = pivot;
		_rayQuery.To = worldDesired;
		_rayQuery.CollisionMask = TpsCamCollisionMask;
		var hit = space.IntersectRay(_rayQuery);

		Vector3 targetLocal;
		if (hit.Count > 0)
		{
			Vector3 hitPos = (Vector3)hit["position"];
			Vector3 dir = worldDesired - pivot;
			float desiredDist = dir.Length();
			if (desiredDist > 0.001f)
			{
				float hitDist = (hitPos - pivot).Length();
				float safeDist = Mathf.Max(0.1f, hitDist - TpsCamWallMargin);
				Vector3 safeWorld = pivot + dir / desiredDist * safeDist;
				targetLocal = HeadPitch.GlobalTransform.AffineInverse() * safeWorld;
			}
			else targetLocal = adsLocal;
		}
		else targetLocal = adsLocal;

		float posLerpT = TpsCamSmoothRate > 0f ? (1f - Mathf.Exp(-TpsCamSmoothRate * dt)) : 1f;
		TpsCamera.Position = TpsCamera.Position.Lerp(targetLocal, posLerpT);

		float targetFov = Mathf.Lerp(_tpsCamBaseFov, TpsAdsFov, _tpsAdsBlend);
		float fovLerpT = 1f - Mathf.Exp(-TpsAdsLerpRate * dt);
		TpsCamera.Fov = Mathf.Lerp(TpsCamera.Fov, targetFov, fovLerpT);
	}

	/// <summary>Routes raw input events into the mouse-look, mouse-capture, and view-mode handlers.
	/// Also records the wallclock timestamp of any fire-press edge into <see cref="PlayerCore._lastFirePressUsec"/>
	/// for the subtick-fire pipeline — captured here rather than in <see cref="PlayerCore.SendNetInput"/>
	/// because the input-event handler fires at the exact moment of the press (sub-tick precision),
	/// not at the tick poll boundary.</summary>
	public override void _Input(InputEvent @event)
	{
		HandleMouseLook(@event);
		HandleMouseCaptureToggle(@event);
		HandleViewModeToggle(@event);
		if (@event.IsActionPressed(InputActions.Fire))
			_lastFirePressUsec = Time.GetTicksUsec();
	}

	/// <summary>Applies mouse motion to body yaw and head pitch. Reads <c>InputEventMouseMotion.Relative</c>
	/// (Godot's accumulated raw delta when MouseMode is Captured — already 1000Hz mouse-friendly, no OS
	/// acceleration injection) and scales it by the master sensitivity, per-axis yaw/pitch multipliers
	/// (Source-style <c>cl_m_yaw</c>/<c>cl_m_pitch</c>), and an ADS-time weapon-specific multiplier.
	/// Pitch is clamped to [<see cref="ClConVars.MinPitch"/>, <see cref="ClConVars.MaxPitch"/>]; yaw is
	/// applied directly to the body via <see cref="Node3D.RotateY"/>.</summary>
	private void HandleMouseLook(InputEvent @event)
	{
		if (@event is not InputEventMouseMotion mm) return;
		if (!MouseLookEnabled || Input.MouseMode != Input.MouseModeEnum.Captured) return;

		float sensMul = 1f;
		var weapon = WeaponHolder?.ActiveWeapon;
		if (weapon != null && Movement.AdsBlend > 0f)
			sensMul = Mathf.Lerp(1f, weapon.AdsSensitivityMul, Movement.AdsBlendVisual);
		float masterSens = ConVars.Cl.MouseSensitivity * sensMul;
		float yawSens = masterSens * ConVars.Cl.MYaw;
		float pitchSens = masterSens * ConVars.Cl.MPitch;

		RotateY(Mathf.DegToRad(-mm.Relative.X * yawSens));

		float pitchDelta = mm.Relative.Y * pitchSens;
		_pitch -= ConVars.Cl.InvertMouseY ? -pitchDelta : pitchDelta;
		_pitch = Mathf.Clamp(_pitch, ConVars.Cl.MinPitch, ConVars.Cl.MaxPitch);
		if (HeadPitch != null)
		{
			Vector3 rot = HeadPitch.RotationDegrees;
			rot.X = _pitch;
			HeadPitch.RotationDegrees = rot;
		}
	}

	/// <summary>Handles key-edge toggles for fire-mode switching and unlimited-ammo cheat.</summary>
	private void HandleMouseCaptureToggle(InputEvent @event)
	{
		if (@event is not InputEventKey k) return;
		if (!k.Pressed || k.Echo) return;
		if (k.Keycode == FireModeToggleKey)
		{
			Movement.FireMode = (Movement.FireMode + 1) % 2;
			Dbg.Print($"[fire] FireMode: {(Movement.FireMode == 0 ? "Automatic" : "SingleShot")}");
		}
		if (k.Keycode == UnlimitedAmmoToggleKey)
		{
			Movement.UnlimitedAmmo = !Movement.UnlimitedAmmo;
			if (Movement.UnlimitedAmmo && WeaponHolder?.ActiveWeapon != null)
				Movement.CurrentMag = WeaponHolder.ActiveWeapon.MagazineSize;
			Dbg.Print($"[ammo] UnlimitedAmmo: {Movement.UnlimitedAmmo}");
		}
	}

	/// <summary>Toggles between first-person and third-person view on key press.</summary>
	private void HandleViewModeToggle(InputEvent ev)
	{
		if (ev is not InputEventKey k || !k.Pressed || k.Echo) return;
		if (k.Keycode != ViewModeToggleKey) return;
		ViewMode = ViewMode == ViewMode.Tps ? ViewMode.Fps : ViewMode.Tps;
		ApplyViewMode();
	}

	/// <summary>Renders the trajectory preview while the grenade slot is active and the fire key is held.</summary>
	protected override void UpdateAimGuide()
	{
		if (_aimGuide == null) return;
		bool show = ActiveSlot == 1 && !InputGate.Blocked
			&& Input.IsActionPressed(InputActions.Fire) && _pendingThrowValid;
		_aimGuide.SetGuideVisible(show);
		if (!show) return;

		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return;

		GrenadeTrajectory.Predict(space, _pendingThrowOrigin, _pendingThrowVel, GetRid(), _aimPath,
			out Vector3 landing, out Vector3 landingNormal);
		_aimGuide.UpdatePath(_aimPath, landing, landingNormal);

		if (Dbg.Enabled && (++_aimDbg & 31) == 0)
			Dbg.Print($"[aimguide] slot={ActiveSlot} charge={GrenadeCharge:F2} pts={_aimPath.Count} " +
				$"landing=({landing.X:F1},{landing.Y:F1},{landing.Z:F1})");
	}
}
