using Godot;

namespace Vantix.Fx;

/// <summary>
/// Canvas-stage post-process layer; FSR2-compatible counterpart to <see cref="PostProcessEffect"/>.
/// Runs after FSR2/TAA upscaling (Canvas stage) so it doesn't corrupt FSR2's input; reads SCREEN_TEXTURE.
/// Supports chromatic aberration, sharpening, vignette, film grain. No depth/velocity here, so motion
/// blur falls back to Environment.MotionBlurEnabled when FSR2 is active.
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

	/// <summary>Mirror the Settings toggles; when false the matching strength is forced to zero in _Process.</summary>
	public bool ChromaticAberrationEnabled = true;
	public bool SharpeningEnabled = true;
	public bool VignetteEnabled = true;
	public bool FilmGrainEnabled = true;
	/// <summary>Runtime value driven by LocalAnimation: 0 = no ADS, 1 = full ADS.</summary>
	public float AdsBlend = 0f;

	/// <summary>Builds the full-screen ColorRect. Layer 35 sits above the viewmodel (10) and below all HUD (>=40),
	/// so the FX wraps world + viewmodel but never the HUD.</summary>
	public override void _Ready()
	{
		Instance = this;
		Layer = 35;
		ProcessMode = ProcessModeEnum.Always;

		Shader shader = GD.Load<Shader>("res://shaders/post_canvas.gdshader");
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

	// Cached parameter names to avoid a StringName allocation per SetShaderParameter call each frame.
	private static readonly StringName _pAberration = "aberration";
	private static readonly StringName _pSharpen = "sharpen";
	private static readonly StringName _pVignetteStrength = "vignette_strength";
	private static readonly StringName _pVignetteRadius = "vignette_radius";
	private static readonly StringName _pVignetteAdsBoost = "vignette_ads_boost";
	private static readonly StringName _pAdsBlend = "ads_blend";
	private static readonly StringName _pGrainStrength = "grain_strength";
	private static readonly StringName _pTimeSeconds = "time_seconds";

	/// <summary>Pushes the current settings and time into the shader uniforms every frame.</summary>
	public override void _Process(double delta)
	{
		if (_mat == null) return;
		using var _prof = MiniProfiler.SampleClient("PostCanvasFx._Process");
		_mat.SetShaderParameter(_pAberration, ChromaticAberrationEnabled ? Aberration : 0f);
		_mat.SetShaderParameter(_pSharpen, SharpeningEnabled ? Sharpen : 0f);
		_mat.SetShaderParameter(_pVignetteStrength, VignetteEnabled ? VignetteStrength : 0f);
		_mat.SetShaderParameter(_pVignetteRadius, VignetteRadius);
		_mat.SetShaderParameter(_pVignetteAdsBoost, VignetteAdsBoost);
		_mat.SetShaderParameter(_pAdsBlend, AdsBlend);
		_mat.SetShaderParameter(_pGrainStrength, FilmGrainEnabled ? GrainStrength : 0f);
		_mat.SetShaderParameter(_pTimeSeconds, (float)(Time.GetTicksMsec() % 100000UL) / 1000.0f);
	}
}
