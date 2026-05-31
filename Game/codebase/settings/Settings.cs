using Godot;
using System;

/// <summary>Shadow quality preset. Off disables all directional and positional shadows globally — single biggest GPU win on dust-style maps (sun-shadow alone can be 30–50% of frame budget at 1080p).</summary>
public enum ShadowQuality { Off, Low, Medium, High }

/// <summary>Anti-aliasing mode. MSAA is intentionally absent because it is incompatible with the screen-space post-processing compositor.</summary>
public enum AntiAliasingMode { Off, Fxaa, Smaa, Taa }

/// <summary>3D upscaler mode. Bilinear is plain stretch (cheapest), Fsr1 is spatial-only (fast, sharp), Fsr2 is temporal upscaling with built-in AA (best quality, requires motion vectors). When Fsr2 is active, the separate TAA/SMAA/FXAA passes are bypassed because FSR2 handles AA itself.</summary>
public enum UpscalingMode { Bilinear, Fsr1, Fsr2 }

/// <summary>Volumetric fog quality. Controls the size of the per-frame voxel volume. Low/Medium/High map to 64/96/160 voxels per side — cost scales cubically. Off exists in the enum for legacy compat but MUST NOT be used at runtime: the smoke-grenade rendering system writes into the same volumetric-fog texture, so Off would make smokes invisible. The settings UI and all presets are gated to Low or higher.</summary>
public enum VolumetricFogQuality { Off, Low, Medium, High }

/// <summary>Overall graphics preset. Tiers map to FSR2 Performance/Balanced/Quality/Native upscaling. Custom = user has tweaked individual values.</summary>
public enum QualityPreset { Low, Medium, High, Ultra, Custom }

/// <summary>Reflection-probe atlas resolution preset. Each tier maps to a per-probe pixel size + atlas slot count. Higher = sharper baked reflections on shiny floors/decals but more VRAM. Like volumetric fog, the underlying ProjectSettings are read at viewport init, so changes need a level reload to take effect.</summary>
public enum ReflectionProbeQuality { Low, Medium, High, Ultra }

/// <summary>
/// User settings (graphics, window mode, input). Persisted via ConfigFile to
/// user://settings.cfg.
/// Call order: <see cref="Load"/> at game start, then <see cref="Apply"/>.
/// User can change via the menu and call <see cref="Save"/>.
/// </summary>
public static class Settings
{
	private const string ConfigPath = "user://settings.cfg";

	public static DisplayServer.WindowMode WindowMode = DisplayServer.WindowMode.Windowed;
	public static Vector2I Resolution = new(1920, 1080);
	/// <summary>Monitor index the game window lives on (0 = primary). Clamped to actual screen count
	/// on load — bogus indices from a multi-monitor setup that has since been unplugged fall back
	/// to 0. Resolution-dropdown candidates are filtered against THIS monitor's native size, not the
	/// monitor the window happens to currently be on.</summary>
	public static int MonitorIndex = 0;
	public static DisplayServer.VSyncMode VSync = DisplayServer.VSyncMode.Enabled;
	public static int FpsCap = 0;
	public static int MenuFpsCap = 60;
	public static float Brightness = 1.0f;

	public static QualityPreset Preset = QualityPreset.High;
	public static float RenderScale = 0.85f;
	/// <summary>Scale of the weapon viewmodel SubViewport. Independent from the world RenderScale so the user can keep iron-sight clarity at 100% while running the world at e.g. 75%. Mode stays Bilinear because the viewmodel viewport is transparent_bg + own_world_3d (FSR on transparent BG is a known Godot hazard).</summary>
	public static float ViewmodelRenderScale = 1.0f;
	/// <summary>UI Content-Scale-Factor applied to the root Window. Maps directly to Window.ContentScaleFactor — runtime-changeable, scales every Control/CanvasItem (HUD, crosshair, menus). 1.0 = native UI, &lt;1 = smaller (more screen real estate), &gt;1 = larger (better readability on 4K/large monitors). Lives in the Display tab — controls size/readability, not visual quality.</summary>
	public static float UiScale = 1.0f;
	/// <summary>UI rendering quality — 2D MSAA on the root Viewport. Smooths jagged Control edges (rounded corners, font outlines, vector shapes) at the cost of a few percent GPU. Disabled = native, 2x/4x/8x = progressively smoother text and HUD shapes. Lives in the Graphics tab — controls visual quality, not size.</summary>
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
	/// <summary>Master switch for the compute-shader post-process pass (chromatic aberration, sharpen,
	/// vignette, film grain, motion blur). Disabling kills the entire <see cref="PostProcessEffect"/>
	/// compositor effect — useful for diagnosing periodic frame spikes traced to the "Post Transparent
	/// Compositor Effects" stage in the Visual Profiler. The individual feature toggles below
	/// (ChromaticAberration, Sharpening, Vignette, FilmGrain, MotionBlur) gate sub-effects but the
	/// compute pass itself still runs; this master kills the dispatch entirely.</summary>
	public static bool PostProcessing = true;
	/// <summary>Max distance in metres up to which cloud shadows render. Beyond that
	/// the shader fades out (no pixel-shader cost). 0 = only the immediate camera
	/// vicinity, 250+ = entire map. Default 120m = sweet spot between look and perf.</summary>
	public static float CloudShadowDistance = 120f;
	public static bool GodRays = true;
	public static bool LensFlare = true;
	public static bool DustMotes = true;
	public static bool MotionBlur = true;
	public static bool FilmGrain = true;
	public static bool Vignette = true;
	public static bool ChromaticAberration = true;
	/// <summary>Toggle the post-process unsharp-mask pass in <c>post_process.glsl</c> (luma-only). Auto-disabled when an FSR upscaler is active because FSR1's RCAS and FSR2's built-in sharpener both run after the compositor — stacking would oversharpen. Off here forces the pass off in every mode.</summary>
	public static bool Sharpening = true;
	public static bool AdsDepthOfField = true;
	public static bool AdsFovZoom = true;
	/// <summary>Toggles Camera3D auto-exposure (CameraAttributesPractical.AutoExposureEnabled). When on, the camera adapts ISO sensitivity to scene brightness (cinematic look); when off, brightness is fixed (kompetitiv preference — bright/dark areas read identically). Scene-default is true.</summary>
	public static bool AutoExposure = true;
	/// <summary>Toggle the CS2-style team-glow composite (teammate body silhouette + nameplate
	/// rendered via two SubViewports composited onto the screen). Off = the entire glow CanvasLayer
	/// is hidden, which also halts the two SubViewport render passes (Godot stops updating viewports
	/// whose parent canvas is invisible at WHEN_VISIBLE update mode — for our ALWAYS mode the cameras
	/// still tick but contribute nothing on-screen). Used to A/B compare the world's look with vs.
	/// without the glow overlay.</summary>
	public static bool TeamGlow = true;
	public static bool ShowDebugBar = false;
	public static bool ShowNetGraph = false;

	public static bool ViewBob = true;
	public static bool SprintSway = true;
	public static bool MouseInertia = true;
	public static bool DirectionLean = true;
	public static bool CameraShake = true;

	public static float MouseSensitivity = 2.0f;
	public static float Fov = 90f;

	public static float HudMarginH = 26f;
	public static float HudMarginV = 20f;

	/// <summary>Local identity token (16-byte GUID), persisted. Server detects reconnects by this.</summary>
	public static string NetIdentityToken = "";

	/// <summary>Dedicated-server mode: forces ALL visual-effect toggles to false so Sky cubemap,
	/// compositor, volumetric fog, AO, SSR, VFX nodes, cloud-shadow script and post-FX do not
	/// attempt to render. Called by NetMain in server mode BEFORE the world is loaded; NetServer
	/// then invokes <see cref="Apply"/> once world.tscn is active to strip the env resource live.</summary>
	public static void ApplyServerHeadlessDefaults()
	{
		Shadows = ShadowQuality.Low;
		AmbientOcclusion = false;
		Reflections = false;
		ReflectionProbes = ReflectionProbeQuality.Low;
		// Headless-server: keep VolFog enum non-Off so the smoke-grenade renderer can
		// still allocate its volume even if it ever runs in a headless build.
		// In practice the server has no WorldEnvironment, so this never reaches the
		// rendering layer, but we keep the invariant "VolumetricFog != Off" everywhere.
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
				// FSR1 Performance — 50% scale. Kompetitiv-Setup: keine Camera-Effekte, keine Atmosphäre.
				// FSR1 (spatial-only) statt FSR2 (temporal) — kompatibel mit Compositor-PostFX +
				// echtem velocity-Motion-Blur. FSR2 lässt sich manuell im Dropdown wählen, deaktiviert
				// dann aber PostProcessEffect + Motion Blur (Godot's pipeline kann nicht beides).
				// VolFog bleibt Low (statt Off) — Smoke-Grenade-Rendering nutzt dasselbe Voxel-Volume,
				// Off würde Smokes komplett unsichtbar machen.
				RenderScale = 0.50f; Upscaler = UpscalingMode.Fsr1; AntiAliasing = AntiAliasingMode.Fxaa;
				Shadows = ShadowQuality.Low; AmbientOcclusion = false;
				Reflections = false; ReflectionProbes = ReflectionProbeQuality.Low; VolumetricFog = VolumetricFogQuality.Low;
				Sky = true; CloudShadows = false; CloudShadowDistance = 60f; GodRays = false; LensFlare = false; DustMotes = false;
				MotionBlur = false; FilmGrain = false; Vignette = false; AdsDepthOfField = false;
				ViewBob = false; SprintSway = false; MouseInertia = false; DirectionLean = false; CameraShake = false;
				break;
			case QualityPreset.Medium:
				// FSR1 Balanced — 75% scale. Mainstream-GPU. VolFog Low, kein SSR, kein GodRay.
				// God-Rays/Lens-Flare sind in Medium aus, daher Render-Scale etwas niedriger als
				// High erlaubt (kein Sub-pixel-Stabilitätsproblem). TAA-Pflicht trotzdem.
				RenderScale = 0.75f; Upscaler = UpscalingMode.Fsr1; AntiAliasing = AntiAliasingMode.Taa;
				Shadows = ShadowQuality.Medium; AmbientOcclusion = true;
				Reflections = false; ReflectionProbes = ReflectionProbeQuality.Medium; VolumetricFog = VolumetricFogQuality.Low;
				Sky = true; CloudShadows = true; CloudShadowDistance = 60f; GodRays = false; LensFlare = true; DustMotes = false;
				MotionBlur = true; FilmGrain = false; Vignette = true; AdsDepthOfField = true;
				ViewBob = true; SprintSway = true; MouseInertia = true; DirectionLean = true; CameraShake = true;
				break;
			case QualityPreset.High:
				// FSR1 Quality — 85% scale. Sweet-Spot: voller Effekt-Stack + Motion Blur + GPU-Win.
				// Höher als FSR1 Quality-typische 0.77 (= "Quality" preset) weil Lens-Flare und
				// God-Rays Screen-Space-Shadern bei zu niedriger Scale Sub-pixel-jittern. 0.85 ist
				// ein guter Kompromiss: ~12-15% GPU-Win, kaum noch sichtbare Stabilität-Probleme.
				// TAA-Pflicht für jegliche Render-Scale < 1.0.
				RenderScale = 0.85f; Upscaler = UpscalingMode.Fsr1; AntiAliasing = AntiAliasingMode.Taa;
				Shadows = ShadowQuality.High; AmbientOcclusion = true;
				Reflections = true; ReflectionProbes = ReflectionProbeQuality.High; VolumetricFog = VolumetricFogQuality.Medium;
				Sky = true; CloudShadows = true; CloudShadowDistance = 120f; GodRays = true; LensFlare = true; DustMotes = true;
				MotionBlur = true; FilmGrain = true; Vignette = true; AdsDepthOfField = true;
				ViewBob = true; SprintSway = true; MouseInertia = true; DirectionLean = true; CameraShake = true;
				break;
			case QualityPreset.Ultra:
				// Native + TAA — kein Upscaler. Max Distance, größte Volume-Fog-Box. GPU-heavy.
				RenderScale = 1.0f; Upscaler = UpscalingMode.Bilinear; AntiAliasing = AntiAliasingMode.Taa;
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
		Brightness = (float)cfg.GetValue("graphics", "brightness", Brightness).AsDouble();

		MouseSensitivity = (float)cfg.GetValue("input", "mouse_sens", MouseSensitivity).AsDouble();
		Fov = (float)cfg.GetValue("input", "fov", Fov).AsDouble();

		HudMarginH = (float)cfg.GetValue("hud", "margin_h", HudMarginH).AsDouble();
		HudMarginV = (float)cfg.GetValue("hud", "margin_v", HudMarginV).AsDouble();

		NetIdentityToken = cfg.GetValue("net", "identity_token", "").AsString();

		// Volumetric-fog voxel-grid size is read from ProjectSettings once at scene-load
		// time and cached by the WorldEnvironment. Writing it via RenderingServer at
		// runtime caused "uninitialized RID" errors on mid-frame fog-buffer reallocation,
		// so we set it here — Load() runs from NetMain._Ready BEFORE any world scene
		// loads, so the engine picks up the user's quality choice on first map load.
		// Changing the quality in-game requires a level reload to take effect.
		(int fogSize, int fogDepth) = ResolveVolumetricFogSize();
		ProjectSettings.SetSetting("rendering/environment/volumetric_fog/volume_size", fogSize);
		ProjectSettings.SetSetting("rendering/environment/volumetric_fog/volume_depth", fogDepth);

		// Reflection-probe atlas size + slot count — same rationale as fog: setting these
		// after the viewport's reflection atlas RID has been created causes "uninitialized
		// RID" hazards on Godot 4.6, so we write them via ProjectSettings before any scene
		// loads. Quality change in-game requires a level reload to take effect.
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
		cfg.SetValue("graphics", "brightness", Brightness);

		cfg.SetValue("input", "mouse_sens", MouseSensitivity);
		cfg.SetValue("input", "fov", Fov);

		cfg.SetValue("hud", "margin_h", HudMarginH);
		cfg.SetValue("hud", "margin_v", HudMarginV);

		cfg.SetValue("net", "identity_token", NetIdentityToken);
		var err = cfg.Save(ConfigPath);
		Dbg.Print($"[settings] Save → {ConfigPath} ({err})");
	}

	/// <summary>Display-level apply WITHOUT viewport/environment/compositor touches.
	/// Safe in autoload _Ready (before any scene load), while the render pipeline is
	/// not fully initialised. Setting Vp.UseTaa/Msaa3D/Scaling3DMode in that phase
	/// produces "Uniforms were never supplied for set 1" errors out of Godot's
	/// TAA/velocity pipeline. Those parts are deferred to <see cref="Apply"/> later
	/// once PlayerCore spawns and the world is loaded.</summary>
	/// <summary>
	/// Atomic window-mode + resolution apply.
	///
	///   • Windowed                    → WindowSetSize(Resolution)
	///   • Fullscreen (Borderless)     → desktop scanout stays native; Resolution is ignored at this
	///                                   layer (sub-native via RenderScale + FSR pipeline)
	///   • ExclusiveFullscreen + native res    → plain Godot ExclusiveFullscreen, no Win32 intervention
	///   • ExclusiveFullscreen + sub-native    → Win32 <see cref="Win32Display.TrySetMode"/> first
	///                                           (programmes the monitor scanout like CS2 / CoD), then
	///                                           hand off to Godot's ExclusiveFullscreen which inherits
	///                                           the new mode. CDS_FULLSCREEN flag → Windows auto-
	///                                           restores the desktop mode on Alt-Tab and on process
	///                                           exit, no extra hooks needed for the happy path.
	///                                           Falls back to native-res Exclusive if the monitor
	///                                           does not advertise the picked mode.
	/// </summary>
	private static void ApplyWindowModeAndResolution()
	{
		DisplayServer.WindowMode current = DisplayServer.WindowGetMode();

		int screenCount = DisplayServer.GetScreenCount();
		if (MonitorIndex < 0 || MonitorIndex >= screenCount) MonitorIndex = 0;

		// Early-out: if the current state already matches the target (same mode + same monitor +
		// same effective resolution), skip the whole mode-cycle. Settings.Apply gets called multiple
		// times during normal flow (boot, settings-menu changes, post-map-load to push graphics
		// tunables), and without this guard each call triggers an unnecessary black-flash mode
		// change even though nothing display-relevant changed.
		if (IsDisplayStateAlreadyCorrect(current)) return;

		DisplayServer.WindowSetCurrentScreen(MonitorIndex);

		if (WindowMode == DisplayServer.WindowMode.ExclusiveFullscreen)
		{
			Vector2I native = GetMonitorNativeResolution(MonitorIndex);
			bool isSubNative = Resolution.X < native.X || Resolution.Y < native.Y;

			// Strict order to avoid the "monitor at 1080p but Godot's swap-chain still 4K → game
			// renders only the top-left quadrant" issue:
			//   1. Drop to Windowed (releases ExclusiveFullscreen lock — WindowSetSize is no-op
			//      while in Exclusive). Skip if we're already Windowed at boot.
			//   2. SetCurrentScreen(MonitorIndex) AGAIN — going to Windowed often snaps the window
			//      back to the previous Exclusive monitor instead of obeying our pre-set.
			//   3. WindowSetSize(Resolution) → Godot's internal framebuffer / viewport matches.
			//   4. Win32/Linux mode-change → monitor scanout matches.
			//   5. SetCurrentScreen(MonitorIndex) AGAIN — paranoia before re-entering exclusive,
			//      because WindowSetMode(ExclusiveFullscreen) tends to snap to whichever monitor
			//      Godot internally last associated with exclusive mode.
			//   6. WindowSetMode(ExclusiveFullscreen) → Godot enters exclusive at the now-matched
			//      size on the now-matched monitor.
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
			// Leaving Exclusive (or never entered) → release any held native override eagerly.
			// Win32's CDS_FULLSCREEN auto-restores on focus loss too, but the user explicitly switched
			// modes here so do it immediately.
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

	/// <summary>Releases whichever native-backend mode override is currently held. Safe to call when
	/// none is held (no-op). Single place so Apply paths don't have to know the backend.</summary>
	private static void ReleaseNativeOverrideIfHeld()
	{
		if (Win32Display.HasAppliedMode) Win32Display.Reset();
		if (LinuxDisplay.HasAppliedMode) LinuxDisplay.Reset();
	}

	/// <summary>True if the live display state already matches what Apply would set up — same
	/// WindowMode, same monitor, same effective resolution. Returning true here makes Apply a
	/// fast no-op which keeps map-load Apply calls from re-triggering an unnecessary monitor
	/// mode-change with its associated black flash.</summary>
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
				// A sub-native target only matches if we currently hold an override at exactly that res.
				if (Win32Display.HasAppliedMode && Win32Display.AppliedResolution == Resolution) return true;
				if (LinuxDisplay.HasAppliedMode && LinuxDisplay.AppliedResolution == Resolution) return true;
				return false;
			}
			// Native target only matches if no override is held (i.e. scanout = native).
			return !Win32Display.HasAppliedMode && !LinuxDisplay.HasAppliedMode;
		}
		if (WindowMode == DisplayServer.WindowMode.Windowed)
		{
			return DisplayServer.WindowGetSize() == Resolution;
		}
		// Borderless Fullscreen — desktop always at native, resolution not applicable.
		return true;
	}

	/// <summary>Best-available PHYSICAL native resolution for a monitor: Win32 / xrandr first,
	/// Godot's DPI-scaled <c>ScreenGetSize</c> as last resort.</summary>
	private static Vector2I GetMonitorNativeResolution(int monitorIndex)
	{
		Vector2I r = Win32Display.IsSupported
			? Win32Display.GetNativeResolution(monitorIndex)
			: LinuxDisplay.IsSupported
				? LinuxDisplay.GetNativeResolution(monitorIndex)
				: Vector2I.Zero;
		return r == Vector2I.Zero ? DisplayServer.ScreenGetSize(monitorIndex) : r;
	}

	public static void ApplyDisplay()
	{
		ApplyWindowModeAndResolution();
		DisplayServer.WindowSetVsyncMode(VSync);
		if (!SettingsMenu.IsAnyOpen) Engine.MaxFps = FpsCap;
		ConVars.Cl.MouseSensitivity = MouseSensitivity;
		ConVars.Cl.Fov = Fov;
		ApplyShadows(Shadows);
	}

	/// <summary>Applies loaded/changed values to Godot + ConVars. Safe to call multiple times.
	/// Early-returns on a dedicated headless server (<see cref="NetMode.Server"/>) — every branch
	/// below targets rendering or input state the server doesn't have: window mode + vsync (no
	/// display), <see cref="Engine.MaxFps"/> override (would clobber the <c>Cli.TickRate</c> cap
	/// NetMain sets at boot), viewport scaling / TAA / MSAA (no viewport), mouse/FOV ConVars
	/// (no client), shadow atlas + soft-filter quality + per-Light3D walk (dummy RenderingServer
	/// on headless ignores these anyway), Sky/SSR/SSIL/Volumetric/Compositor toggles (no render
	/// pipeline). The headless defaults from <see cref="ApplyServerHeadlessDefaults"/> stay as
	/// static field values; they would only matter if rendering were active.</summary>
	public static void Apply(SceneTree tree)
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server)
			return;

		ApplyWindowModeAndResolution();
		DisplayServer.WindowSetVsyncMode(VSync);

		// UI Content-Scale runtime — applied to the root Window. ContentScaleFactor
		// scales every Control / CanvasItem (HUD, crosshair, menus) uniformly without
		// re-rendering at a different resolution. Cheaper than a UI-Viewport approach
		// and matches CS2's "UI Scale" semantics.
		if (tree?.Root is Window rootWindow) rootWindow.ContentScaleFactor = UiScale;

		Viewport.Scaling3DModeEnum scalingMode = ResolveScalingMode();
		bool fsr2Active = scalingMode == Viewport.Scaling3DModeEnum.Fsr2;
		// TAA is only compatible with Bilinear (native or trivial scale). FSR2 has its own
		// temporal pass and forces TAA off internally. FSR1 is spatial-only and doesn't see
		// the TAA jitter pattern — the temporal history can't converge through the upscale,
		// frames disagree, and the result is visible flicker + PCF-shadow noise that never
		// gets averaged out. Treat both FSR modes as TAA-incompatible.
		bool taaCompatible = scalingMode == Viewport.Scaling3DModeEnum.Bilinear;
		// If the user picked TAA but the active upscaler can't honour it, fall back to FXAA
		// for the screen-space pass so they still get SOME anti-aliasing instead of nothing.
		AntiAliasingMode effectiveAa = (AntiAliasing == AntiAliasingMode.Taa && !taaCompatible)
			? AntiAliasingMode.Fxaa
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
			// Disable TAA first so the in-between state never has both TAA + Fsr2 active
			// (Godot warns "FSR 2 ... not compatible with TAA. Disabling TAA internally."
			// when setting UseTaa while Scaling3DMode is still on Fsr2 from a previous Apply).
			vp.UseTaa = false;
			vp.Msaa3D = Viewport.Msaa.Disabled;
			vp.Scaling3DScale = RenderScale;
			vp.Scaling3DMode = scalingMode;
			vp.UseTaa = useTaa;
			vp.ScreenSpaceAA = ssAa;
			// 2D MSAA on the root viewport — smooths Control edges (HUD/text/menu)
			// without affecting 3D AA. Independent of Msaa3D which we keep disabled
			// (TAA path can't coexist with 3D MSAA).
			vp.Msaa2D = UiMsaa;
		}

		if (tree?.Root != null)
		{
			bool brightnessIsIdentity = Mathf.IsEqualApprox(Brightness, 1.0f);
			Color brightnessTint = new(Brightness, Brightness, Brightness, 1f);
			foreach (Node n in tree.Root.FindChildren("*", "SubViewport", true, false))
			{
				if (n is not SubViewport sv || !sv.OwnWorld3D) continue;
				// Weapon viewmodel viewport has transparent_bg + own_world_3d. Force the
				// safest path: native scale + Bilinear + no temporal/screen-space AA.
				// Changing Scaling3DMode at runtime on a transparent SubViewport caused
				// "Attempting to use an uninitialized RID" errors plus a black world — the
				// viewmodel is small and cheap to render at native, so the upscaler win
				// from scaling it down is not worth the buffer-state hazards.
				sv.UseTaa = false;
				sv.Msaa3D = Viewport.Msaa.Disabled;
				sv.Scaling3DScale = ViewmodelRenderScale;
				sv.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
				sv.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
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

	/// <summary>Resolves the effective Godot 3D scaling mode from the user's Upscaler preference, current RenderScale, and whether Motion Blur is on. FSR1 auto-upgrades to FSR2 when Motion Blur is off — without MB the Compositor doesn't need to write the color buffer, so FSR2's temporal-upscale can run safely on a clean buffer. At native resolution we always use Bilinear.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static Viewport.Scaling3DModeEnum ResolveScalingMode()
	{
		if (RenderScale >= 0.999f)
			return Viewport.Scaling3DModeEnum.Bilinear;
		return ResolveEffectiveUpscaler() switch
		{
			UpscalingMode.Fsr2 => Viewport.Scaling3DModeEnum.Fsr2,
			UpscalingMode.Fsr1 => Viewport.Scaling3DModeEnum.Fsr,
			_ => Viewport.Scaling3DModeEnum.Bilinear,
		};
	}

	/// <summary>Resolves the effective upscaler. Honoured as-is from the user pick. An earlier version auto-upgraded FSR1 to FSR2 when Motion Blur was off (Compositor wouldn't write color anyway → safe), but FSR2's temporal reprojection mis-handles velocity-less screen-space shaders (lens flare, god rays — color animates but motion vectors are zero, so FSR2 rejects history and the effect flickers). Better to leave the user's pick alone: if they want FSR2's GPU win, they pick it explicitly.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static UpscalingMode ResolveEffectiveUpscaler() => Upscaler;

	/// <summary>Maps the fog quality preset to the (size, depth) voxel grid the renderer allocates per frame. Cost is roughly cubic in the side length, so going from 160 to 96 saves ~80% of the volumetric pass cost.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static (int size, int depth) ResolveVolumetricFogSize() => VolumetricFog switch
	{
		VolumetricFogQuality.Low => (64, 64),
		VolumetricFogQuality.Medium => (96, 96),
		VolumetricFogQuality.High => (160, 160),
		_ => (64, 64),
	};

	/// <summary>Maps the reflection-probe quality preset to (per-probe pixel size, atlas slot count). de_dust2 has 11 probes so even the Ultra tier's 16 slots is enough. VRAM ≈ size² × count × 4 bytes per channel × 6 cube faces — Ultra ≈ 24 MB, High ≈ 8 MB, Medium ≈ 4 MB, Low ≈ 1 MB.</summary>
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static (int size, int count) ResolveReflectionAtlas() => ReflectionProbes switch
	{
		ReflectionProbeQuality.Low => (128, 64),
		ReflectionProbeQuality.Medium => (256, 64),
		ReflectionProbeQuality.High => (512, 32),
		ReflectionProbeQuality.Ultra => (1024, 16),
		_ => (256, 64),
	};

	/// <summary>Applies shadow atlas size and soft-shadow filter quality for the given preset.</summary>
	/// <summary>Applies shadow atlas size, soft-shadow filter quality, AND a global on/off toggle for every Light3D in the scene. Off zeroes the directional atlas and walks the scene flipping each Light3D's ShadowEnabled flag — single biggest GPU win available (the directional sun shadow alone is ~30-50% of the frame on dust-style maps).</summary>
	private static void ApplyShadows(ShadowQuality q, SceneTree tree = null)
	{
		bool shadowsOn = q != ShadowQuality.Off;
		int dirAtlasSize = q switch
		{
			ShadowQuality.Off => 0,
			ShadowQuality.Low => 2048,
			ShadowQuality.Medium => 4096,
			ShadowQuality.High => 8192,
			_ => 4096,
		};
		// PCF-Soft-Filter mit dem Sun bei voller Energy (1.5) macht stippling-Noise
		// die auch durch Atlas-Vergrößerung nicht weggeht — der Noise sitzt im Sample-
		// Pattern selbst, nicht in der Texel-Auflösung. Shadows=Low fährt deshalb Hard
		// (CS-Style: harte Texel-Kanten, KEIN Noise — kompetitiv sauber). Medium/High
		// haben weiter PCF, aber mit höheren Sample-Counts für glatteres Result.
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
			// Per-Light3D: cache scene-default ShadowEnabled + Energy as Meta on first touch.
			// shadowsOn=false  → force ShadowEnabled=false on everything; DirectionalLight
			//                    also gets Energy × NoShadowSunDim so the sun without shadow
			//                    occlusion doesn't blow out the indoors (light bleeds through
			//                    walls when there's no shadow map → over-bright wash).
			// shadowsOn=true   → restore each light to its scene-default state, including
			//                    the DirectionalLight's original Energy.
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
				if (light is DirectionalLight3D)
					light.LightEnergy = shadowsOn ? origEnergy : origEnergy * NoShadowSunDim;
				else
					light.LightEnergy = origEnergy;
			}
		}
	}

	/// <summary>Per-Light meta key used by <see cref="ApplyShadows"/> to remember each light's scene-default ShadowEnabled state so toggling Shadows On after Off doesn't enable scene-side-disabled lights.</summary>
	private static readonly StringName ShadowOrigMetaKey = "_settings_shadow_orig";
	/// <summary>Per-Light meta key for scene-default LightEnergy. Used to dim the DirectionalLight when Shadows are Off (no occlusion → walls don't block sun → indoors wash out) and to restore it cleanly when Shadows are turned back on.</summary>
	private static readonly StringName EnergyOrigMetaKey = "_settings_energy_orig";
	/// <summary>Multiplier applied to DirectionalLight energy when Shadows are Off. 0.15 ≈ 15% sun — aggressive cut to compensate for the "light through walls" indoor wash. Outdoor still receives sky+ambient + lightmap-baked direct, so it stays visible. If outdoor becomes too dark for taste, push up toward 0.25–0.35.</summary>
	private const float NoShadowSunDim = 0.15f;

	/// <summary>Diagnostic one-shot: dumps LightmapGI state at first ApplyEnvironment. Lets us
	/// tell F5 vs Release whether the lightmap actually loaded (release looked washed-out and
	/// matched "Light Data cleared" in F5, strong signal the .bptc.ctexarray fails to load in
	/// the exported PCK). Drop this method once root cause is fixed.</summary>
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
		// Reflections toggle controls BOTH screen-space reflections (SSR) and screen-space indirect
		// lighting (SSIL). SSIL is defined per-scene in WorldEnvironment (e.g., de_dust2 ships with
		// ssil_radius=4.0, ssil_intensity=1.2) and previously had no Settings hook, so users who
		// turned off Reflections expecting a perf save still paid the SSIL cost. Tying both to one
		// flag matches CS2's "Screen Space Effects" combined toggle and keeps the menu lean.
		env.SsrEnabled = Reflections;
		env.SsilEnabled = Reflections;
		// VolumetricFog is the rendering substrate the smoke-grenade system writes into. The
		// "Off" quality option disables the volume here for performance-diagnosis sessions —
		// known suspect for periodic Gen2 GC spikes via RDTextureFormat churn in the temporal-
		// reprojection pass. Smokes thrown while Off renders nothing (FogVolumes only display
		// inside an enabled VolumetricFog). Quality change (volume-grid size) requires a level
		// reload because volume_size is read once at scene-load via ProjectSettings.
		env.VolumetricFogEnabled = VolumetricFog != VolumetricFogQuality.Off;
		// Adjustment slots (Brightness/Contrast/Saturation/Color-Correction-LUT) are
		// the scene's responsibility. de_dust2 ships with adjustment_brightness=1.25,
		// adjustment_saturation=1.4 + a colour-correction LUT — these define the
		// intended look. Settings.Brightness is treated as a multiplier on top.
		//
		// Robustness: force AdjustmentEnabled=true regardless of prior state. The
		// pre-fix version of this code wrote AdjustmentEnabled=false whenever the
		// user's Brightness was 1.0, which permanently disabled scene grading after
		// the first Apply. We override that here on every Apply, so even reloads
		// or stale state can't keep grading off. Contrast/Saturation/LUT are NEVER
		// written — those keep their scene-set values and pop back as soon as
		// Enabled flips true.
		const float kSceneDefaultBrightness = 1.25f;
		env.AdjustmentEnabled = true;
		env.AdjustmentBrightness = kSceneDefaultBrightness * Brightness;
	}

	private static Sky _cachedSkyResource;
	private static Godot.Environment.BGMode _cachedBgMode = Godot.Environment.BGMode.Sky;
	private static Godot.Environment.AmbientSource _cachedAmbientSource = Godot.Environment.AmbientSource.Bg;
	private static bool _skyCached;

	/// <summary>Toggles the individual map VFX nodes (cloud shadows, god rays, lens flare, dust
	/// motes) per visibility (each has its own settings toggle for bisect testing). Plus the
	/// post-FX compositor and the sky toggle on the world environment (background mode + sky
	/// resource clear/restore).</summary>
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
				// Sky off: clear the sky resource AND switch ambient/reflection sources off the
				// Sky cube. The scene-default ambient_light_source = Sky (3) — when env.Sky becomes
				// null, sampling that cube returns undefined / white texels, which shows up as
				// blown-out walls on materials that take their ambient term from it. Force the
				// explicit ambient_light_color path instead.
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

		// Reflection Probes laufen unabhängig von SSR/SSIL — Settings.Reflections=Off
		// schaltet nur die Screen-Space-Effekte ab, die Cubemap-Probes sampelten weiter
		// die Outdoor-Sky-HDR und projizierten sie auch in Tunnel-Innenräume (Probes
		// die interior=false haben). Toggle hier mit, damit Reflections=Off wirklich
		// ALLE Reflexionen tötet — sowohl Screen-Space als auch Probe-basiert.
		if (tree?.Root != null)
		{
			foreach (Node n in tree.Root.FindChildren("*", "ReflectionProbe", true, false))
			{
				if (n is ReflectionProbe rp) rp.Visible = Reflections;
			}
		}

		// Two post-FX paths — only one is active at a time. FSR2 needs an unmodified
		// color buffer for its temporal-upscale pass, so the full Compositor effect path
		// (which writes scene-color in PostTransparent) is disabled in that mode and
		// Canvas-PostFx covers CA / sharpen / vignette / grain from above the viewmodel.
		//
		// Motion blur is velocity-buffer-reconstruction (Unity-FPSSample style) — runs
		// only in the Compositor path because Canvas-stage has no velocity data after
		// upscaling. World-MB comes from the WorldEnvironment Compositor here; weapon-MB
		// is set up separately on the viewmodel SubViewport via PostCanvasFx.Instance...
		// no wait — it's set up by NetMain on a per-viewmodel Compositor (see
		// ViewmodelMotionBlur.AttachTo). At FSR2 there is no world-MB (industry standard
		// trade-off — BF2042, many racing games do the same).
		// useFullCanvas is true whenever the EFFECTIVE upscaler is FSR2 — that's either
		// because the user explicitly picked FSR2, or because they picked FSR1 + turned
		// off Motion Blur (and ResolveEffectiveUpscaler auto-upgraded). In both cases
		// the Compositor must not write the color buffer, so Canvas-PostFx takes over.
		UpscalingMode effective = ResolveEffectiveUpscaler();
		bool useFullCanvas = effective == UpscalingMode.Fsr2;
		// FSR1's RCAS and FSR2's built-in sharpening both run AFTER our post-process.
		// Stacking our compositor sharpening on top of that causes oversharpen halos
		// (visible as a "weird crunch" on high-frequency content like god rays). When
		// an FSR upscaler is active, skip our own sharpen pass — let the upscaler do it.
		// User Sharpening toggle gates the whole thing — Off forces the pass off in
		// every mode regardless of upscaler.
		bool upscalerProvidesSharpen = effective != UpscalingMode.Bilinear;
		if (we?.Compositor is Compositor comp)
			foreach (CompositorEffect effect in comp.CompositorEffects)
			{
				if (effect == null) continue;
				if (effect is PostProcessEffect ppe)
				{
					ppe.Enabled = PostProcessing && !useFullCanvas;
					ppe.MotionBlur = MotionBlur;
					ppe.FilmGrain = FilmGrain;
					ppe.Vignette = Vignette;
					ppe.Sharpening = Sharpening && !upscalerProvidesSharpen;
					ppe.ChromaticAberration = ChromaticAberration;
				}
			}
		if (PostCanvasFx.Instance != null)
		{
			PostCanvasFx.Instance.Visible = useFullCanvas;
			PostCanvasFx.Instance.ChromaticAberrationEnabled = useFullCanvas;
			PostCanvasFx.Instance.SharpeningEnabled = false;
			PostCanvasFx.Instance.VignetteEnabled = useFullCanvas && Vignette;
			PostCanvasFx.Instance.FilmGrainEnabled = useFullCanvas && FilmGrain;
		}
		// Toggle camera auto-exposure on every Camera3D with a CameraAttributesPractical
		// resource. Scene-default is on (in local_player.tscn). Kompetitiv-Spieler bevorzugen
		// fixed brightness so dark areas read consistently — Settings.AutoExposure exposes it.
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
			if (we.Compositor != null) return we;
			first ??= we;
		}
		return first;
	}
}
