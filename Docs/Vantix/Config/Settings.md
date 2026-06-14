# Settings

`Vantix.Config.Settings`

User settings (graphics, window mode, input), persisted to user://settings.cfg. Call order: `Load` then `Apply`; `Save` on change.

## Properties

| Name | Summary |
|------|---------|
| `ViewmodelSharpenStrength` | Effective viewmodel sharpen, mirroring the world so the weapon never looks sharper/softer: on FSR pipelines the container shader fills the RCAS gap; on Bilinear it stays off because PostCanvasFx already sharpens world and viewmodel together (else double-sharpened). |

## Fields

| Name | Summary |
|------|---------|
| `AutoExposure` | Camera3D auto-exposure (CameraAttributesPractical.AutoExposureEnabled). Off = fixed brightness (competitive preference). |
| `BaseSharpen` | Canvas-stage sharpen strength on the Bilinear pipeline when the frame isn't softened by TAA or a sub-native upscale. |
| `CloudShadowDistance` | Max distance (m) cloud shadows render before fading out. 0 = camera vicinity only, 250+ = whole map. |
| `EnergyOrigMetaKey` | Per-Light meta key for scene-default LightEnergy; used to dim the DirectionalLight when Shadows are Off and restore it later. |
| `MonitorIndex` | Monitor index the window lives on (0 = primary). Clamped to the screen count on load; resolution candidates are filtered against this monitor's native size. |
| `NetIdentityToken` | Local identity token (16-byte GUID), persisted; the server detects reconnects by this. |
| `NoShadowSunDim` | DirectionalLight energy multiplier when Shadows are Off (compensates the "light through walls" indoor wash). |
| `PostProcessing` | Master switch for the compute-shader post-process pass; Off kills the entire `PostProcessEffect` dispatch (the feature toggles below only gate sub-effects). |
| `ShadowOrigMetaKey` | Per-Light meta key remembering each light's scene-default ShadowEnabled, so toggling Shadows On doesn't enable scene-disabled lights. |
| `Sharpening` | Toggle the luma-only post-process unsharp-mask pass. Auto-disabled under an FSR upscaler (FSR's RCAS already sharpens - stacking oversharpens). |
| `TaaSharpen` | Canvas-stage sharpen strength when the Bilinear pipeline output is soft (TAA + sub-native upscale). Applied in PostCanvasFx after TAA/scaling - compositor sharpen runs before both and gets smeared. |
| `TeamGlow` | Team-glow composite (teammate silhouette + nameplate via SubViewports). Off hides the glow CanvasLayer. |
| `UiMsaa` | UI 2D MSAA on the root Viewport - smooths Control edges, font outlines and vector shapes. |
| `UiScale` | UI content-scale factor on the root Window (Window.ContentScaleFactor). Runtime-changeable; scales every Control/CanvasItem. |
| `ViewmodelRenderScale` | Weapon viewmodel SubViewport scale, independent from world RenderScale. >1.0 supersamples (SSAA), the cleanest AA for iron-sight edges. Mode stays Bilinear - the viewport is transparent_bg + own_world_3d (FSR on transparent BG is a Godot hazard). |
| `ViewmodelSharpen` | Viewmodel container-shader sharpen strength. Only needed on FSR pipelines where the world gets RCAS but the weapon viewport doesn't. Fed per-frame via `ViewmodelSharpenStrength`. |
| `_lightmapDiagDone` | Diagnostic one-shot dumping LightmapGI state at first ApplyEnvironment (release looked washed-out, suspected the .bptc.ctexarray failing to load in the PCK). Drop once fixed. |

## Methods

| Name | Summary |
|------|---------|
| `Apply(SceneTree)` | Applies loaded/changed values to Godot + ConVars. Idempotent. Early-returns on a dedicated server (every branch below targets rendering/input the server lacks, and an Engine.MaxFps override would clobber NetMain's tick-rate cap). |
| `ApplyDisplay()` | Display-level apply without viewport/environment/compositor touches — safe in autoload _Ready before the render pipeline exists (setting Vp.UseTaa/Msaa3D/Scaling3DMode there throws "Uniforms were never supplied for set 1"). Those are deferred to `Apply`. |
| `ApplyEffects(SceneTree)` | Toggles the map VFX nodes (cloud shadows, god rays, lens flare, dust motes), the post-FX compositor, and the sky (background mode + sky resource clear/restore) per their settings. |
| `ApplyEnvironment(SceneTree)` | Toggles environment features (AO, reflections, volumetric fog) per quality. |
| `ApplyPreset(Vantix.Config.QualityPreset)` | Sets all quality fields to a preset. Custom leaves everything unchanged. |
| `ApplyServerHeadlessDefaults()` | Dedicated-server mode: forces all visual-effect toggles off so nothing tries to render. Called by NetMain before world load; `Apply` later strips the env resource live. |
| `ApplyShadows(Vantix.Config.ShadowQuality, SceneTree)` | Applies shadow atlas size, soft-filter quality, and a global on/off toggle to every Light3D. Off zeroes the directional atlas and disables each light's shadow — the single biggest GPU win on dust-style maps. |
| `ApplyWindowModeAndResolution()` | Atomic window-mode + resolution apply. • Windowed → WindowSetSize(Resolution). • Borderless Fullscreen → desktop scanout stays native; Resolution ignored here (handled via RenderScale + FSR). • ExclusiveFullscreen, native res → plain Godot ExclusiveFullscreen. • ExclusiveFullscreen, sub-native → `TrySetMode` programmes the monitor scanout first (CDS_FULLSCREEN auto-restores on Alt-Tab/exit), then hands off to Godot; falls back to native-res Exclusive if the monitor doesn't advertise the mode. |
| `FindWorldEnvironment(SceneTree)` | Finds the world's WorldEnvironment node, preferring the one with a compositor. |
| `GetMonitorNativeResolution(int)` | Best-available physical native resolution: Win32/xrandr first, Godot's DPI-scaled ScreenGetSize as fallback. |
| `IsDisplayStateAlreadyCorrect(DisplayServer.WindowMode)` | True if the live display state already matches the target (mode, monitor, resolution), letting Apply no-op to avoid an unnecessary mode-change black flash. |
| `Load()` | Reads the config file. If it does not exist the defaults remain. |
| `ReleaseNativeOverrideIfHeld()` | Releases whichever native-backend mode override is held (no-op if none). Backend-agnostic. |
| `ResolveReflectionAtlas()` | Maps reflection-probe quality to (per-probe pixel size, atlas slot count). VRAM ≈ size²×count×4×6 faces (Ultra ≈ 24 MB down to Low ≈ 1 MB). |
| `ResolveScalingMode()` | Resolves the Godot 3D scaling mode from Upscaler + RenderScale; always Bilinear at native resolution. |
| `ResolveVolumetricFogSize()` | Maps fog quality to the per-frame (size, depth) voxel grid; cost is ~cubic in side length. |
| `Save()` | Persists the current settings to disk. |
