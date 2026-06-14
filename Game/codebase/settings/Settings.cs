using Godot;
using System;

/// <summary>Shadow quality. Off disables all shadows globally — single biggest GPU win on dust-style maps.</summary>
public enum ShadowQuality { Off, Low, Medium, High }

/// <summary>Anti-aliasing mode. MSAA is absent — incompatible with the screen-space post-process compositor.</summary>
public enum AntiAliasingMode { Off, Fxaa, Smaa, Taa }

/// <summary>3D upscaler. Bilinear = plain stretch, Fsr1 = spatial-only, Fsr2 = temporal with built-in AA.
/// When Fsr2 is active the separate TAA/SMAA/FXAA passes are bypassed.</summary>
public enum UpscalingMode { Bilinear, Fsr1, Fsr2 }

/// <summary>Volumetric fog quality; Low/Medium/High = 64/96/160 voxels/side (cubic cost). Off must not be used
/// at runtime — smoke grenades render into the same fog texture, so Off makes smokes invisible.</summary>
public enum VolumetricFogQuality { Off, Low, Medium, High }

/// <summary>Overall graphics preset; Custom = individually tweaked values.</summary>
public enum QualityPreset { Low, Medium, High, Ultra, Custom }

/// <summary>Reflection-probe atlas quality (per-probe size + slot count). Read at viewport init, so changes need a level reload.</summary>
public enum ReflectionProbeQuality { Low, Medium, High, Ultra }

/// <summary>User settings (graphics, window mode, input), persisted to user://settings.cfg. Call order:
/// <see cref="Load"/> then <see cref="Apply"/>; <see cref="Save"/> on change.</summary>
public static class Settings
{
	private const string ConfigPath = "user://settings.cfg";

	public static DisplayServer.WindowMode WindowMode = DisplayServer.WindowMode.Windowed;
	public static Vector2I Resolution = new(1920, 1080);
	/// <summary>Monitor index the window lives on (0 = primary). Clamped to the screen count on load; resolution
	/// candidates are filtered against this monitor's native size.</summary>
	public static int MonitorIndex = 0;
	public static DisplayServer.VSyncMode VSync = DisplayServer.VSyncMode.Enabled;
	public static int FpsCap = 0;
	public static int MenuFpsCap = 60;
	public static float Brightness = 1.0f;

	public static QualityPreset Preset = QualityPreset.High;
	public static float RenderScale = 0.85f;
	/// <summary>Weapon viewmodel SubViewport scale, independent from world RenderScale. &gt;1.0 supersamples (SSAA),
	/// the cleanest AA for iron-sight edges. Mode stays Bilinear — the viewport is transparent_bg + own_world_3d
	/// (FSR on transparent BG is a Godot hazard).</summary>
	public static float ViewmodelRenderScale = 1.5f;
	/// <summary>UI content-scale factor on the root Window (Window.ContentScaleFactor). Runtime-changeable; scales every Control/CanvasItem.</summary>
	public static float UiScale = 1.0f;
	/// <summary>UI 2D MSAA on the root Viewport — smooths Control edges, font outlines and vector shapes.</summary>
	public static Viewport.Msaa UiMsaa = Viewport.Msaa.Disabled;
	public static AntiAliasingMode AntiAliasing = AntiAliasingMode.Taa;
	public static UpscalingMode Upscaler = UpscalingMode.Fsr1;
	public static ShadowQuality Shadows = ShadowQuality.High;
	public static bool AmbientOcclusion = true;
	public static bool Reflections = true;
	public static ReflectionProbeQuality ReflectionProbes = ReflectionProbeQuality.High;
	public static VolumetricFogQuality VolumetricFog = VolumetricFogQuality.Medium;
	public static bool Sky = true;
	public static bool CloudShadows = true;
	/// <summary>Master switch for the compute-shader post-process pass; Off kills the entire
	/// <see cref="PostProcessEffect"/> dispatch (the feature toggles below only gate sub-effects).</summary>
	public static bool PostProcessing = true;
	/// <summary>Max distance (m) cloud shadows render before fading out. 0 = camera vicinity only, 250+ = whole map.</summary>
	public static float CloudShadowDistance = 120f;
	public static bool GodRays = true;
	public static bool LensFlare = true;
	public static bool DustMotes = true;
	public static bool MotionBlur = true;
	public static bool FilmGrain = true;
	public static bool Vignette = true;
	public static bool ChromaticAberration = true;
	/// <summary>Toggle the luma-only post-process unsharp-mask pass. Auto-disabled under an FSR upscaler (FSR's RCAS already sharpens — stacking oversharpens).</summary>
	public static bool Sharpening = true;
	/// <summary>Canvas-stage sharpen strength on the Bilinear pipeline when the frame isn't softened by TAA or a sub-native upscale.</summary>
	private const float BaseSharpen = 0.25f;
	/// <summary>Canvas-stage sharpen strength when the Bilinear pipeline output is soft (TAA + sub-native upscale).
	/// Applied in PostCanvasFx after TAA/scaling — compositor sharpen runs before both and gets smeared.</summary>
	private const float TaaSharpen = 0.6f;
	/// <summary>Viewmodel container-shader sharpen strength. Only needed on FSR pipelines where the world gets RCAS
	/// but the weapon viewport doesn't. Fed per-frame via <see cref="ViewmodelSharpenStrength"/>.</summary>
	private const float ViewmodelSharpen = 0.5f;

	/// <summary>Effective viewmodel sharpen, mirroring the world so the weapon never looks sharper/softer: on FSR
	/// pipelines the container shader fills the RCAS gap; on Bilinear it stays off because PostCanvasFx already
	/// sharpens world and viewmodel together (else double-sharpened).</summary>
	public static float ViewmodelSharpenStrength =>
		ResolveScalingMode() != Viewport.Scaling3DModeEnum.Bilinear && Sharpening
			? ViewmodelSharpen : 0f;
	public static bool AdsDepthOfField = true;
	public static bool AdsFovZoom = true;
	/// <summary>Camera3D auto-exposure (CameraAttributesPractical.AutoExposureEnabled). Off = fixed brightness (competitive preference).</summary>
	public static bool AutoExposure = true;
	/// <summary>Team-glow composite (teammate silhouette + nameplate via SubViewports). Off hides the glow CanvasLayer.</summary>
	public static bool TeamGlow = true;
	public static bool ShowDebugBar = false;
	public static bool ShowNetGraph = false;
	// Debug viewmodel weapon-light sampler; Off skips its per-frame raycasts (top CPU cost in the profiler).
	public static bool WeaponLight = true;

	public static bool ViewBob = true;
	public static bool SprintSway = true;
	public static bool MouseInertia = true;
	public static bool DirectionLean = true;
	public static bool CameraShake = true;

	public static float MouseSensitivity = 2.0f;
	public static float Fov = 100f;

	public static float HudMarginH = 26f;
	public static float HudMarginV = 20f;

	/// <summary>Local identity token (16-byte GUID), persisted; the server detects reconnects by this.</summary>
	public static string NetIdentityToken = "";

	/// <summary>Dedicated-server mode: forces all visual-effect toggles off so nothing tries to render. Called by
	/// NetMain before world load; <see cref="Apply"/> later strips the env resource live.</summary>
	public static void ApplyServerHeadlessDefaults()
	{
		Shadows = ShadowQuality.Low;
		AmbientOcclusion = false;
		Reflections = false;
		ReflectionProbes = ReflectionProbeQuality.Low;
		VolumetricFog = VolumetricFogQuality.Low;
		Sky = false;
		CloudShadows = false;
		GodRays = false;
		LensFlare = false;
		DustMotes = false;
		MotionBlur = false;
		FilmGrain = false;
		Vignette = false;
		AdsDepthOfField = false;
		AdsFovZoom = false;
		ViewBob = false;
		SprintSway = false;
		MouseInertia = false;
		DirectionLean = false;
		CameraShake = false;
	}

	/// <summary>Sets all quality fields to a preset. Custom leaves everything unchanged.</summary>
	public static void ApplyPreset(QualityPreset p)
	{
		Preset = p;
		switch (p)
		{
			case QualityPreset.Low:
				RenderScale = 0.50f; ViewmodelRenderScale = 1.0f; Upscaler = UpscalingMode.Fsr2; AntiAliasing = AntiAliasingMode.Smaa;
				Shadows = ShadowQuality.Low; AmbientOcclusion = false;
				Reflections = false; ReflectionProbes = ReflectionProbeQuality.Low; VolumetricFog = VolumetricFogQuality.Low;
				Sky = true; CloudShadows = false; CloudShadowDistance = 60f; GodRays = false; LensFlare = false; DustMotes = false;
				MotionBlur = false; FilmGrain = false; Vignette = false; AdsDepthOfField = false;
				ViewBob = false; SprintSway = false; MouseInertia = false; DirectionLean = false; CameraShake = false;
				break;
			case QualityPreset.Medium:
				RenderScale = 0.75f; ViewmodelRenderScale = 1.5f; Upscaler = UpscalingMode.Fsr2; AntiAliasing = AntiAliasingMode.Taa;
				Shadows = ShadowQuality.Medium; AmbientOcclusion = true;
				Reflections = false; ReflectionProbes = ReflectionProbeQuality.Medium; VolumetricFog = VolumetricFogQuality.Low;
				Sky = true; CloudShadows = true; CloudShadowDistance = 60f; GodRays = false; LensFlare = true; DustMotes = false;
				MotionBlur = true; FilmGrain = false; Vignette = true; AdsDepthOfField = true;
				ViewBob = true; SprintSway = true; MouseInertia = true; DirectionLean = true; CameraShake = true;
				break;
			case QualityPreset.High:
				RenderScale = 0.85f; ViewmodelRenderScale = 1.5f; Upscaler = UpscalingMode.Fsr2; AntiAliasing = AntiAliasingMode.Taa;
				Shadows = ShadowQuality.High; AmbientOcclusion = true;
				Reflections = true; ReflectionProbes = ReflectionProbeQuality.High; VolumetricFog = VolumetricFogQuality.Medium;
				Sky = true; CloudShadows = true; CloudShadowDistance = 120f; GodRays = true; LensFlare = true; DustMotes = true;
				MotionBlur = true; FilmGrain = true; Vignette = true; AdsDepthOfField = true;
				ViewBob = true; SprintSway = true; MouseInertia = true; DirectionLean = true; CameraShake = true;
				break;
			case QualityPreset.Ultra:
				RenderScale = 1.0f; ViewmodelRenderScale = 2.0f; Upscaler = UpscalingMode.Bilinear; AntiAliasing = AntiAliasingMode.Taa;
				Shadows = ShadowQuality.High; AmbientOcclusion = true;
				Reflections = true; ReflectionProbes = ReflectionProbeQuality.Ultra; VolumetricFog = VolumetricFogQuality.High;
				Sky = true; CloudShadows = true; CloudShadowDistance = 200f; GodRays = true; LensFlare = true; DustMotes = true;
				MotionBlur = true; FilmGrain = true; Vignette = true; AdsDepthOfField = true;
				ViewBob = true; SprintSway = true; MouseInertia = true; DirectionLean = true; CameraShake = true;
				break;
			case QualityPreset.Custom:
				break;
		}
	}

	/// <summary>Reads the config file. If it does not exist the defaults remain.</summary>
	public static void Load()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(ConfigPath) != Error.Ok)
		{
			Dbg.Print("[settings] No config file — using defaults");
			return;
		}

		WindowMode = (DisplayServer.WindowMode)cfg.GetValue("video", "window_mode", (int)WindowMode).AsInt32();
		int rx = cfg.GetValue("video", "res_x", Resolution.X).AsInt32();
		int ry = cfg.GetValue("video", "res_y", Resolution.Y).AsInt32();
		Resolution = new Vector2I(rx, ry);
		MonitorIndex = cfg.GetValue("video", "monitor", MonitorIndex).AsInt32();
		int screenCount = DisplayServer.GetScreenCount();
		if (MonitorIndex < 0 || MonitorIndex >= screenCount) MonitorIndex = 0;
		VSync = (DisplayServer.VSyncMode)cfg.GetValue("video", "vsync", (int)VSync).AsInt32();
		FpsCap = cfg.GetValue("video", "fps_cap", FpsCap).AsInt32();
		MenuFpsCap = cfg.GetValue("video", "menu_fps_cap", MenuFpsCap).AsInt32();

		Preset = (QualityPreset)cfg.GetValue("graphics", "preset", (int)Preset).AsInt32();
		RenderScale = (float)cfg.GetValue("graphics", "render_scale", RenderScale).AsDouble();
		ViewmodelRenderScale = (float)cfg.GetValue("graphics", "viewmodel_render_scale", ViewmodelRenderScale).AsDouble();
		UiScale = (float)cfg.GetValue("video", "ui_scale", UiScale).AsDouble();
		UiMsaa = (Viewport.Msaa)cfg.GetValue("graphics", "ui_msaa", (int)UiMsaa).AsInt32();
		AntiAliasing = (AntiAliasingMode)cfg.GetValue("graphics", "aa", (int)AntiAliasing).AsInt32();
		Upscaler = (UpscalingMode)cfg.GetValue("graphics", "upscaler", (int)Upscaler).AsInt32();
		Shadows = (ShadowQuality)cfg.GetValue("graphics", "shadows", (int)Shadows).AsInt32();
		AmbientOcclusion = cfg.GetValue("graphics", "ao", AmbientOcclusion).AsBool();
		Reflections = cfg.GetValue("graphics", "reflections", Reflections).AsBool();
		ReflectionProbes = (ReflectionProbeQuality)cfg.GetValue("graphics", "reflection_probes", (int)ReflectionProbes).AsInt32();
		VolumetricFog = (VolumetricFogQuality)cfg.GetValue("graphics", "volumetric_fog", (int)VolumetricFog).AsInt32();
		Sky = cfg.GetValue("graphics", "sky", Sky).AsBool();
		CloudShadows = cfg.GetValue("graphics", "cloud_shadows", CloudShadows).AsBool();
		PostProcessing = cfg.GetValue("graphics", "post_processing", PostProcessing).AsBool();
		CloudShadowDistance = (float)cfg.GetValue("graphics", "cloud_shadow_distance", CloudShadowDistance).AsDouble();
		GodRays = cfg.GetValue("graphics", "god_rays", GodRays).AsBool();
		LensFlare = cfg.GetValue("graphics", "lens_flare", LensFlare).AsBool();
		DustMotes = cfg.GetValue("graphics", "dust_motes", DustMotes).AsBool();
		MotionBlur = cfg.GetValue("graphics", "motion_blur", MotionBlur).AsBool();
		FilmGrain = cfg.GetValue("graphics", "film_grain", FilmGrain).AsBool();
		Vignette = cfg.GetValue("graphics", "vignette", Vignette).AsBool();
		ChromaticAberration = cfg.GetValue("graphics", "chromatic_aberration", ChromaticAberration).AsBool();
		Sharpening = cfg.GetValue("graphics", "sharpening", Sharpening).AsBool();
		AdsDepthOfField = cfg.GetValue("graphics", "ads_dof", AdsDepthOfField).AsBool();
		AdsFovZoom = cfg.GetValue("graphics", "ads_fov_zoom", AdsFovZoom).AsBool();
		AutoExposure = cfg.GetValue("graphics", "auto_exposure", AutoExposure).AsBool();
		TeamGlow = cfg.GetValue("graphics", "team_glow", TeamGlow).AsBool();
		ViewBob = cfg.GetValue("camera", "view_bob", ViewBob).AsBool();
		SprintSway = cfg.GetValue("camera", "sprint_sway", SprintSway).AsBool();
		MouseInertia = cfg.GetValue("camera", "mouse_inertia", MouseInertia).AsBool();
		DirectionLean = cfg.GetValue("camera", "direction_lean", DirectionLean).AsBool();
		CameraShake = cfg.GetValue("camera", "camera_shake", CameraShake).AsBool();
		ShowDebugBar = cfg.GetValue("debug", "show_debug_bar", ShowDebugBar).AsBool();
		ShowNetGraph = cfg.GetValue("debug", "show_net_graph", ShowNetGraph).AsBool();
		WeaponLight = cfg.GetValue("debug", "weapon_light", WeaponLight).AsBool();
		Brightness = (float)cfg.GetValue("graphics", "brightness", Brightness).AsDouble();

		MouseSensitivity = (float)cfg.GetValue("input", "mouse_sens", MouseSensitivity).AsDouble();
		Fov = (float)cfg.GetValue("input", "fov", Fov).AsDouble();

		HudMarginH = (float)cfg.GetValue("hud", "margin_h", HudMarginH).AsDouble();
		HudMarginV = (float)cfg.GetValue("hud", "margin_v", HudMarginV).AsDouble();

		NetIdentityToken = cfg.GetValue("net", "identity_token", "").AsString();

		(int fogSize, int fogDepth) = ResolveVolumetricFogSize();
		ProjectSettings.SetSetting("rendering/environment/volumetric_fog/volume_size", fogSize);
		ProjectSettings.SetSetting("rendering/environment/volumetric_fog/volume_depth", fogDepth);

		(int refSize, int refCount) = ResolveReflectionAtlas();
		ProjectSettings.SetSetting("rendering/reflections/reflection_atlas/reflection_size", refSize);
		ProjectSettings.SetSetting("rendering/reflections/reflection_atlas/reflection_count", refCount);

		Dbg.Print($"[settings] Loaded from {ConfigPath} (fog vol {fogSize}×{fogDepth}, refl atlas {refSize}×{refCount})");
	}

	/// <summary>Persists the current settings to disk.</summary>
	public static void Save()
	{
		var cfg = new ConfigFile();
		cfg.SetValue("video", "window_mode", (int)WindowMode);
		cfg.SetValue("video", "res_x", Resolution.X);
		cfg.SetValue("video", "res_y", Resolution.Y);
		cfg.SetValue("video", "monitor", MonitorIndex);
		cfg.SetValue("video", "vsync", (int)VSync);
		cfg.SetValue("video", "fps_cap", FpsCap);
		cfg.SetValue("video", "menu_fps_cap", MenuFpsCap);

		cfg.SetValue("graphics", "preset", (int)Preset);
		cfg.SetValue("graphics", "render_scale", RenderScale);
		cfg.SetValue("graphics", "viewmodel_render_scale", ViewmodelRenderScale);
		cfg.SetValue("video", "ui_scale", UiScale);
		cfg.SetValue("graphics", "ui_msaa", (int)UiMsaa);
		cfg.SetValue("graphics", "aa", (int)AntiAliasing);
		cfg.SetValue("graphics", "upscaler", (int)Upscaler);
		cfg.SetValue("graphics", "shadows", (int)Shadows);
		cfg.SetValue("graphics", "ao", AmbientOcclusion);
		cfg.SetValue("graphics", "reflections", Reflections);
		cfg.SetValue("graphics", "reflection_probes", (int)ReflectionProbes);
		cfg.SetValue("graphics", "volumetric_fog", (int)VolumetricFog);
		cfg.SetValue("graphics", "sky", Sky);
		cfg.SetValue("graphics", "cloud_shadows", CloudShadows);
		cfg.SetValue("graphics", "post_processing", PostProcessing);
		cfg.SetValue("graphics", "cloud_shadow_distance", CloudShadowDistance);
		cfg.SetValue("graphics", "god_rays", GodRays);
		cfg.SetValue("graphics", "lens_flare", LensFlare);
		cfg.SetValue("graphics", "dust_motes", DustMotes);
		cfg.SetValue("graphics", "motion_blur", MotionBlur);
		cfg.SetValue("graphics", "film_grain", FilmGrain);
		cfg.SetValue("graphics", "vignette", Vignette);
		cfg.SetValue("graphics", "chromatic_aberration", ChromaticAberration);
		cfg.SetValue("graphics", "sharpening", Sharpening);
		cfg.SetValue("graphics", "ads_dof", AdsDepthOfField);
		cfg.SetValue("graphics", "ads_fov_zoom", AdsFovZoom);
		cfg.SetValue("graphics", "auto_exposure", AutoExposure);
		cfg.SetValue("graphics", "team_glow", TeamGlow);
		cfg.SetValue("camera", "view_bob", ViewBob);
		cfg.SetValue("camera", "sprint_sway", SprintSway);
		cfg.SetValue("camera", "mouse_inertia", MouseInertia);
		cfg.SetValue("camera", "direction_lean", DirectionLean);
		cfg.SetValue("camera", "camera_shake", CameraShake);
		cfg.SetValue("debug", "show_debug_bar", ShowDebugBar);
		cfg.SetValue("debug", "show_net_graph", ShowNetGraph);
		cfg.SetValue("debug", "weapon_light", WeaponLight);
		cfg.SetValue("graphics", "brightness", Brightness);

		cfg.SetValue("input", "mouse_sens", MouseSensitivity);
		cfg.SetValue("input", "fov", Fov);

		cfg.SetValue("hud", "margin_h", HudMarginH);
		cfg.SetValue("hud", "margin_v", HudMarginV);

		cfg.SetValue("net", "identity_token", NetIdentityToken);
		var err = cfg.Save(ConfigPath);
		Dbg.Print($"[settings] Save → {ConfigPath} ({err})");
	}

	/// <summary>Atomic window-mode + resolution apply.
	///   • Windowed → WindowSetSize(Resolution).
	///   • Borderless Fullscreen → desktop scanout stays native; Resolution ignored here (handled via RenderScale + FSR).
	///   • ExclusiveFullscreen, native res → plain Godot ExclusiveFullscreen.
	///   • ExclusiveFullscreen, sub-native → <see cref="Win32Display.TrySetMode"/> programmes the monitor scanout first
	///     (CDS_FULLSCREEN auto-restores on Alt-Tab/exit), then hands off to Godot; falls back to native-res Exclusive
	///     if the monitor doesn't advertise the mode.</summary>
	private static void ApplyWindowModeAndResolution()
	{
		DisplayServer.WindowMode current = DisplayServer.WindowGetMode();

		int screenCount = DisplayServer.GetScreenCount();
		if (MonitorIndex < 0 || MonitorIndex >= screenCount) MonitorIndex = 0;

		if (IsDisplayStateAlreadyCorrect(current)) return;

		DisplayServer.WindowSetCurrentScreen(MonitorIndex);

		if (WindowMode == DisplayServer.WindowMode.ExclusiveFullscreen)
		{
			Vector2I native = GetMonitorNativeResolution(MonitorIndex);
			bool isSubNative = Resolution.X < native.X || Resolution.Y < native.Y;

			if (current == DisplayServer.WindowMode.ExclusiveFullscreen || current == DisplayServer.WindowMode.Fullscreen)
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			DisplayServer.WindowSetCurrentScreen(MonitorIndex);
			DisplayServer.WindowSetSize(Resolution);

			if (isSubNative)
			{
				bool ok = false;
				if (Win32Display.IsSupported)
				{
					long hwndLong = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0);
					ok = Win32Display.TrySetMode(new IntPtr(hwndLong), Resolution.X, Resolution.Y);
				}
				else if (LinuxDisplay.IsSupported)
				{
					ok = LinuxDisplay.TrySetMode(MonitorIndex, Resolution.X, Resolution.Y);
				}
				if (!ok)
					GD.PushWarning($"[Settings] native mode-change to {Resolution.X}×{Resolution.Y} unavailable on this platform; using Exclusive Fullscreen at native res instead.");
			}
			else
			{
				ReleaseNativeOverrideIfHeld();
			}

			DisplayServer.WindowSetCurrentScreen(MonitorIndex);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
		}
		else
		{
			ReleaseNativeOverrideIfHeld();

			if (WindowMode == DisplayServer.WindowMode.Windowed)
			{
				if (current != DisplayServer.WindowMode.Windowed)
					DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
				if (DisplayServer.WindowGetSize() != Resolution)
					DisplayServer.WindowSetSize(Resolution);
			}
			else
			{
				// Borderless Fullscreen — desktop scanout stays native; size is ignored at this layer.
				if (current != WindowMode)
					DisplayServer.WindowSetMode(WindowMode);
			}
		}
	}

	/// <summary>Releases whichever native-backend mode override is held (no-op if none). Backend-agnostic.</summary>
	private static void ReleaseNativeOverrideIfHeld()
	{
		if (Win32Display.HasAppliedMode) Win32Display.Reset();
		if (LinuxDisplay.HasAppliedMode) LinuxDisplay.Reset();
	}

	/// <summary>True if the live display state already matches the target (mode, monitor, resolution), letting Apply
	/// no-op to avoid an unnecessary mode-change black flash.</summary>
	private static bool IsDisplayStateAlreadyCorrect(DisplayServer.WindowMode current)
	{
		if (current != WindowMode) return false;
		if (DisplayServer.WindowGetCurrentScreen() != MonitorIndex) return false;

		if (WindowMode == DisplayServer.WindowMode.ExclusiveFullscreen)
		{
			Vector2I native = GetMonitorNativeResolution(MonitorIndex);
			bool isSubNative = Resolution.X < native.X || Resolution.Y < native.Y;
			if (isSubNative)
			{
				if (Win32Display.HasAppliedMode && Win32Display.AppliedResolution == Resolution) return true;
				if (LinuxDisplay.HasAppliedMode && LinuxDisplay.AppliedResolution == Resolution) return true;
				return false;
			}
			return !Win32Display.HasAppliedMode && !LinuxDisplay.HasAppliedMode;
		}
		if (WindowMode == DisplayServer.WindowMode.Windowed)
		{
			return DisplayServer.WindowGetSize() == Resolution;
		}
		return true;
	}

	/// <summary>Best-available physical native resolution: Win32/xrandr first, Godot's DPI-scaled ScreenGetSize as fallback.</summary>
	private static Vector2I GetMonitorNativeResolution(int monitorIndex)
	{
		Vector2I r = Win32Display.IsSupported
			? Win32Display.GetNativeResolution(monitorIndex)
			: LinuxDisplay.IsSupported
				? LinuxDisplay.GetNativeResolution(monitorIndex)
				: Vector2I.Zero;
		return r == Vector2I.Zero ? DisplayServer.ScreenGetSize(monitorIndex) : r;
	}

	/// <summary>Display-level apply without viewport/environment/compositor touches — safe in autoload _Ready before
	/// the render pipeline exists (setting Vp.UseTaa/Msaa3D/Scaling3DMode there throws "Uniforms were never
	/// supplied for set 1"). Those are deferred to <see cref="Apply"/>.</summary>
	public static void ApplyDisplay()
	{
		ApplyWindowModeAndResolution();
		DisplayServer.WindowSetVsyncMode(VSync);
		if (!SettingsMenu.IsAnyOpen) Engine.MaxFps = FpsCap;
		ConVars.Cl.MouseSensitivity = MouseSensitivity;
		ConVars.Cl.Fov = Fov;
		ApplyShadows(Shadows);
	}

	/// <summary>Applies loaded/changed values to Godot + ConVars. Idempotent. Early-returns on a dedicated server
	/// (every branch below targets rendering/input the server lacks, and an Engine.MaxFps override would clobber
	/// NetMain's tick-rate cap).</summary>
	public static void Apply(SceneTree tree)
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server)
			return;

		ApplyWindowModeAndResolution();
		DisplayServer.WindowSetVsyncMode(VSync);

		if (tree?.Root is Window rootWindow) rootWindow.ContentScaleFactor = UiScale;

		Viewport.Scaling3DModeEnum scalingMode = ResolveScalingMode();
		bool fsr2Active = scalingMode == Viewport.Scaling3DModeEnum.Fsr2;
		bool taaCompatible = scalingMode == Viewport.Scaling3DModeEnum.Bilinear;
		AntiAliasingMode effectiveAa = (AntiAliasing == AntiAliasingMode.Taa && !taaCompatible)
			? AntiAliasingMode.Smaa
			: AntiAliasing;
		Viewport.ScreenSpaceAAEnum ssAa = fsr2Active
			? Viewport.ScreenSpaceAAEnum.Disabled
			: effectiveAa switch
			{
				AntiAliasingMode.Fxaa => Viewport.ScreenSpaceAAEnum.Fxaa,
				AntiAliasingMode.Smaa => Viewport.ScreenSpaceAAEnum.Smaa,
				_ => Viewport.ScreenSpaceAAEnum.Disabled,
			};
		bool useTaa = taaCompatible && AntiAliasing == AntiAliasingMode.Taa;

		if (tree?.Root is Viewport vp)
		{
			vp.UseTaa = false;
			vp.Msaa3D = Viewport.Msaa.Disabled;
			vp.Scaling3DScale = RenderScale;
			vp.Scaling3DMode = scalingMode;
			vp.FsrSharpness = 0.1f;
			vp.UseTaa = useTaa;
			vp.ScreenSpaceAA = ssAa;
			vp.Msaa2D = UiMsaa;
		}

		if (tree?.Root != null)
		{
			bool brightnessIsIdentity = Mathf.IsEqualApprox(Brightness, 1.0f);
			Color brightnessTint = new(Brightness, Brightness, Brightness, 1f);
			foreach (Node n in tree.Root.FindChildren("*", "SubViewport", true, false))
			{
				if (n is not SubViewport sv || !sv.OwnWorld3D) continue;
				sv.UseTaa = false;
				sv.Msaa3D = Viewport.Msaa.Disabled;
				sv.Scaling3DScale = ViewmodelRenderScale;
				sv.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
				sv.ScreenSpaceAA = AntiAliasing == AntiAliasingMode.Off
					? Viewport.ScreenSpaceAAEnum.Disabled
					: Viewport.ScreenSpaceAAEnum.Smaa;
				if (!brightnessIsIdentity && sv.GetParent() is SubViewportContainer svc)
					svc.Modulate = brightnessTint;
			}
		}

		if (!SettingsMenu.IsAnyOpen) Engine.MaxFps = FpsCap;
		ConVars.Cl.MouseSensitivity = MouseSensitivity;
		ConVars.Cl.Fov = Fov;

		ApplyShadows(Shadows, tree);
		ApplyEnvironment(tree);
		ApplyEffects(tree);
	}

	/// <summary>Resolves the Godot 3D scaling mode from Upscaler + RenderScale; always Bilinear at native resolution.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static Viewport.Scaling3DModeEnum ResolveScalingMode()
	{
		if (RenderScale >= 0.999f)
			return Viewport.Scaling3DModeEnum.Bilinear;
		return Upscaler switch
		{
			UpscalingMode.Fsr2 => Viewport.Scaling3DModeEnum.Fsr2,
			UpscalingMode.Fsr1 => Viewport.Scaling3DModeEnum.Fsr,
			_ => Viewport.Scaling3DModeEnum.Bilinear,
		};
	}

	/// <summary>Maps fog quality to the per-frame (size, depth) voxel grid; cost is ~cubic in side length.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static (int size, int depth) ResolveVolumetricFogSize() => VolumetricFog switch
	{
		VolumetricFogQuality.Low => (64, 64),
		VolumetricFogQuality.Medium => (96, 96),
		VolumetricFogQuality.High => (160, 160),
		_ => (64, 64),
	};

	/// <summary>Maps reflection-probe quality to (per-probe pixel size, atlas slot count). VRAM ≈ size²×count×4×6 faces (Ultra ≈ 24 MB down to Low ≈ 1 MB).</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static (int size, int count) ResolveReflectionAtlas() => ReflectionProbes switch
	{
		ReflectionProbeQuality.Low => (128, 64),
		ReflectionProbeQuality.Medium => (256, 64),
		ReflectionProbeQuality.High => (512, 32),
		ReflectionProbeQuality.Ultra => (1024, 16),
		_ => (256, 64),
	};

	/// <summary>Applies shadow atlas size, soft-filter quality, and a global on/off toggle to every Light3D. Off zeroes
	/// the directional atlas and disables each light's shadow — the single biggest GPU win on dust-style maps.</summary>
	private static void ApplyShadows(ShadowQuality q, SceneTree tree = null)
	{
		bool shadowsOn = q != ShadowQuality.Off;
		int dirAtlasSize = q switch
		{
			ShadowQuality.Off => 256,
			ShadowQuality.Low => 2048,
			ShadowQuality.Medium => 4096,
			ShadowQuality.High => 8192,
			_ => 4096,
		};
		RenderingServer.ShadowQuality filterQ = q switch
		{
			ShadowQuality.Low => RenderingServer.ShadowQuality.Hard,
			ShadowQuality.Medium => RenderingServer.ShadowQuality.SoftMedium,
			ShadowQuality.High => RenderingServer.ShadowQuality.SoftHigh,
			_ => RenderingServer.ShadowQuality.Hard,
		};
		RenderingServer.DirectionalShadowAtlasSetSize(dirAtlasSize, false);
		RenderingServer.DirectionalSoftShadowFilterSetQuality(filterQ);
		RenderingServer.PositionalSoftShadowFilterSetQuality(filterQ);
		if (tree?.Root != null)
		{
			foreach (Node n in tree.Root.FindChildren("*", "Light3D", true, false))
			{
				if (n is not Light3D light) continue;
				if (!light.HasMeta(ShadowOrigMetaKey))
					light.SetMeta(ShadowOrigMetaKey, light.ShadowEnabled);
				if (!light.HasMeta(EnergyOrigMetaKey))
					light.SetMeta(EnergyOrigMetaKey, light.LightEnergy);

				bool origShadow = (bool)light.GetMeta(ShadowOrigMetaKey);
				float origEnergy = (float)light.GetMeta(EnergyOrigMetaKey);

				light.ShadowEnabled = shadowsOn && origShadow;
				if (light is DirectionalLight3D dir)
				{
					light.LightEnergy = shadowsOn ? origEnergy : origEnergy * NoShadowSunDim;
					// Cascade count + shadow distance scale with quality: Low/Medium halve cascades and cut
					// the caster set via distance; High keeps the full 4-split look.
					if (shadowsOn)
					{
						dir.DirectionalShadowMode = q == ShadowQuality.High
							? DirectionalLight3D.ShadowMode.Parallel4Splits
							: DirectionalLight3D.ShadowMode.Parallel2Splits;
						dir.DirectionalShadowMaxDistance = q switch
						{
							ShadowQuality.Low => 50f,
							ShadowQuality.Medium => 80f,
							_ => 150f,
						};
					}
				}
				else
					light.LightEnergy = origEnergy;
			}
		}
	}

	/// <summary>Per-Light meta key remembering each light's scene-default ShadowEnabled, so toggling Shadows On doesn't enable scene-disabled lights.</summary>
	private static readonly StringName ShadowOrigMetaKey = "_settings_shadow_orig";
	/// <summary>Per-Light meta key for scene-default LightEnergy; used to dim the DirectionalLight when Shadows are Off and restore it later.</summary>
	private static readonly StringName EnergyOrigMetaKey = "_settings_energy_orig";
	/// <summary>DirectionalLight energy multiplier when Shadows are Off (compensates the "light through walls" indoor wash).</summary>
	private const float NoShadowSunDim = 0.15f;

	/// <summary>Diagnostic one-shot dumping LightmapGI state at first ApplyEnvironment (release looked washed-out, suspected the .bptc.ctexarray failing to load in the PCK). Drop once fixed.</summary>
	private static bool _lightmapDiagDone;
	private static void DumpLightmapGIState(SceneTree tree)
	{
		if (_lightmapDiagDone || tree?.Root == null) return;
		_lightmapDiagDone = true;
		foreach (Node n in tree.Root.FindChildren("*", "LightmapGI", true, false))
		{
			if (n is not LightmapGI lm) continue;
			LightmapGIData data = lm.LightData;
			GD.Print($"[lightmap-diag] LightmapGI '{lm.Name}': data={(data == null ? "NULL" : "OK")}");
		}
	}

	/// <summary>Toggles environment features (AO, reflections, volumetric fog) per quality.</summary>
	private static void ApplyEnvironment(SceneTree tree)
	{
		DumpLightmapGIState(tree);
		if (FindWorldEnvironment(tree)?.Environment is not Godot.Environment env)
			return;
		env.SsaoEnabled = AmbientOcclusion;
		env.SsrEnabled = Reflections;
		env.SsilEnabled = Reflections;
		env.VolumetricFogEnabled = VolumetricFog != VolumetricFogQuality.Off;
		const float kSceneDefaultBrightness = 1.25f;
		env.AdjustmentEnabled = true;
		env.AdjustmentBrightness = kSceneDefaultBrightness * Brightness;
	}

	private static Sky _cachedSkyResource;
	private static Godot.Environment.BGMode _cachedBgMode = Godot.Environment.BGMode.Sky;
	private static Godot.Environment.AmbientSource _cachedAmbientSource = Godot.Environment.AmbientSource.Bg;
	private static bool _skyCached;

	/// <summary>Toggles the map VFX nodes (cloud shadows, god rays, lens flare, dust motes), the post-FX
	/// compositor, and the sky (background mode + sky resource clear/restore) per their settings.</summary>
	private static void ApplyEffects(SceneTree tree)
	{
		WorldEnvironment we = FindWorldEnvironment(tree);
		Node mapRoot = we?.GetParent();
		if (mapRoot == null)
			return;

		if (we?.Environment is Godot.Environment env)
		{
			if (!_skyCached)
			{
				_cachedSkyResource = env.Sky;
				_cachedBgMode = env.BackgroundMode;
				_cachedAmbientSource = env.AmbientLightSource;
				_skyCached = true;
			}
			if (Sky)
			{
				env.Sky = _cachedSkyResource;
				env.BackgroundMode = _cachedBgMode;
				env.AmbientLightSource = _cachedAmbientSource;
			}
			else
			{
				env.Sky = null;
				env.BackgroundMode = Godot.Environment.BGMode.ClearColor;
				env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
			}
		}

		if (mapRoot.GetNodeOrNull("CloudShadows") is Node3D cs)
		{
			cs.Visible = CloudShadows;
			if (cs is MeshInstance3D mi && mi.MaterialOverride is ShaderMaterial smat)
				smat.SetShaderParameter("max_distance", CloudShadowDistance);
		}
		if (mapRoot.GetNodeOrNull("GodRays") is Node3D gr) gr.Visible = GodRays;
		if (mapRoot.GetNodeOrNull("LensFlare") is Node3D lf) lf.Visible = LensFlare;
		if (mapRoot.GetNodeOrNull("DustMotes") is Node3D dm) dm.Visible = DustMotes;

		if (tree?.Root != null)
		{
			foreach (Node n in tree.Root.FindChildren("*", "ReflectionProbe", true, false))
			{
				if (n is ReflectionProbe rp) rp.Visible = Reflections;
			}
		}

		UpscalingMode effective = Upscaler;
		bool useFullCanvas = effective == UpscalingMode.Fsr2;
		bool bilinearPipeline = ResolveScalingMode() == Viewport.Scaling3DModeEnum.Bilinear;
		bool canvasSharpen = Sharpening && bilinearPipeline;
		float canvasSharpenStrength = AntiAliasing == AntiAliasingMode.Taa || RenderScale < 0.999f
			? TaaSharpen : BaseSharpen;
		bool ppeFound = false;
		bool ppeEnabled = false;
		if (we?.Compositor is Compositor comp)
			foreach (CompositorEffect effect in comp.CompositorEffects)
			{
				if (effect == null) continue;
				if (effect is PostProcessEffect ppe)
				{
					ppeFound = true;
					ppe.Enabled = PostProcessing && !useFullCanvas;
					ppeEnabled = ppe.Enabled;
					ppe.MotionBlur = MotionBlur;
					ppe.FilmGrain = FilmGrain;
					ppe.Vignette = Vignette;
					ppe.Sharpening = false;
					ppe.ChromaticAberration = ChromaticAberration;
				}
			}
		if (PostCanvasFx.Instance != null)
		{
			PostCanvasFx.Instance.Visible = useFullCanvas || canvasSharpen;
			PostCanvasFx.Instance.ChromaticAberrationEnabled = useFullCanvas;
			PostCanvasFx.Instance.SharpeningEnabled = canvasSharpen;
			PostCanvasFx.Instance.Sharpen = canvasSharpenStrength;
			PostCanvasFx.Instance.VignetteEnabled = useFullCanvas && Vignette;
			PostCanvasFx.Instance.FilmGrainEnabled = useFullCanvas && FilmGrain;
		}
		ViewmodelMotionBlur.Configure(
			enabled: PostProcessing && !useFullCanvas,
			chromaticAberration: ChromaticAberration,
			sharpening: false,
			vignette: Vignette,
			filmGrain: FilmGrain,
			motionBlur: MotionBlur);
		GD.Print($"[Settings.FX] worldEnv={we?.Name} hasCompositor={we?.Compositor is Compositor} ppeFound={ppeFound} " +
			$"upscaler={Upscaler} effective={effective} useFullCanvas={useFullCanvas} PostProcessing={PostProcessing} " +
			$"=> ppeEnabled={ppeEnabled} | canvasInstance={PostCanvasFx.Instance != null} canvasVisible={PostCanvasFx.Instance?.Visible}");
		if (tree?.Root != null)
		{
			foreach (Node n in tree.Root.FindChildren("*", "Camera3D", true, false))
			{
				if (n is Camera3D cam && cam.Attributes is CameraAttributesPractical attrs)
					attrs.AutoExposureEnabled = AutoExposure;
			}
		}
	}

	/// <summary>Finds the world's WorldEnvironment node, preferring the one with a compositor.</summary>
	private static WorldEnvironment FindWorldEnvironment(SceneTree tree)
	{
		if (tree?.Root == null)
			return null;
		WorldEnvironment first = null;
		foreach (Node n in tree.Root.FindChildren("*", "WorldEnvironment", true, false))
		{
			if (n is not WorldEnvironment we) continue;
			if (ViewmodelMotionBlur.IsViewmodelEnvironment(we)) continue;
			if (we.Compositor != null) return we;
			first ??= we;
		}
		return first;
	}
}
