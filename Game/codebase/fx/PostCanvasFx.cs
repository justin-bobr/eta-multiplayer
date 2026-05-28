using Godot;

/// <summary>
/// Canvas-stage post-process layer. FSR2-compatible counterpart to
/// <see cref="PostProcessEffect"/>. Runs AFTER FSR2/TAA upscaling, in the Canvas
/// stage, so it does not corrupt FSR2's temporal-upscale input. Reads
/// SCREEN_TEXTURE and writes the final pixel.
///
/// Spawned by NetMain on the client; toggled on/off by Settings.ApplyEffects
/// based on whether FSR2 is the active upscaler.
///
/// Supported effects: chromatic aberration, sharpening, vignette, film grain.
/// Heat haze and motion blur are NOT available at this stage (no depth/velocity
/// after upscale). Motion blur falls back to Godot's built-in
/// Environment.MotionBlurEnabled when FSR2 is active.
/// </summary>
public partial class PostCanvasFx : CanvasLayer
{
	public static PostCanvasFx Instance { get; private set; }

	private ColorRect _rect;
	private ShaderMaterial _mat;

	[Export] public float Aberration = 0.0026f;
	[Export] public float Sharpen = 0.25f;
	[Export] public float VignetteStrength = 0.18f;
	[Export] public float VignetteRadius = 1.05f;
	[Export] public float VignetteAdsBoost = 0.15f;
	[Export] public float GrainStrength = 0.07f;

	/// <summary>Mirrors the Settings toggles. 0 = effect off, the corresponding strength is forced to zero in _Process.</summary>
	public bool ChromaticAberrationEnabled = true;
	public bool SharpeningEnabled = true;
	public bool VignetteEnabled = true;
	public bool FilmGrainEnabled = true;
	/// <summary>Runtime value driven by LocalAnimation (0 = no ADS, 1 = full ADS). Same semantics as in PostProcessEffect.</summary>
	public float AdsBlend = 0f;

	/// <summary>Layer 35 — between viewmodel (10) and the lowest HUD element (HudCs2 on
	/// layer 40). Reads SCREEN_TEXTURE so the FX wraps 3D world + viewmodel (intended),
	/// while every HUD CanvasLayer renders on top of the FX (HudCs2=40, Crosshair=50,
	/// Scoreboard=90, NetGraph/DebugBar=100, LowHp=105, killfeed/hitmarker=110) — none
	/// of them get chromatic aberration / vignette / film grain applied.</summary>
	public override void _Ready()
	{
		Instance = this;
		Layer = 35;
		ProcessMode = ProcessModeEnum.Always;

		Shader shader = GD.Load<Shader>("res://maps/dust/post_canvas.gdshader");
		_mat = new ShaderMaterial { Shader = shader };

		_rect = new ColorRect
		{
			AnchorLeft = 0f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Color = new Color(1f, 1f, 1f, 1f),
			Material = _mat,
		};
		AddChild(_rect);
	}

	/// <summary>Frees the singleton reference on exit so a re-init after disconnect/reconnect rebinds cleanly.</summary>
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	/// <summary>Pushes the current settings + time into the shader uniforms every frame.</summary>
	public override void _Process(double delta)
	{
		if (_mat == null) return;
		_mat.SetShaderParameter("aberration", ChromaticAberrationEnabled ? Aberration : 0f);
		_mat.SetShaderParameter("sharpen", SharpeningEnabled ? Sharpen : 0f);
		_mat.SetShaderParameter("vignette_strength", VignetteEnabled ? VignetteStrength : 0f);
		_mat.SetShaderParameter("vignette_radius", VignetteRadius);
		_mat.SetShaderParameter("vignette_ads_boost", VignetteAdsBoost);
		_mat.SetShaderParameter("ads_blend", AdsBlend);
		_mat.SetShaderParameter("grain_strength", FilmGrainEnabled ? GrainStrength : 0f);
		_mat.SetShaderParameter("time_seconds", (float)(Time.GetTicksMsec() % 100000UL) / 1000.0f);
	}
}
