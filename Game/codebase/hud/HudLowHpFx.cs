using Godot;

/// <summary>
/// Low-HP red vignette pulse when HP (from LastSelfSnap) drops below <see cref="WarnHpThreshold"/>.
/// Pulse intensifies toward 0 HP and fades once HP recovers above the threshold.
/// </summary>
public partial class HudLowHpFx : Control
{
	/// <summary>HP threshold below which the effect activates (30% of max).</summary>
	public const int WarnHpThreshold = 30;
	private const float PulseFreq = 1.6f;
	private const float MaxAlpha = 0.65f;

	private ColorRect _vignetteRect;
	private ShaderMaterial _shaderMat;
	private float _time;
	private float _lastAppliedStrength = -1f;
	private static readonly StringName _strengthParam = "strength";

	public override void _Ready()
	{
		AnchorLeft = 0f; AnchorTop = 0f; AnchorRight = 1f; AnchorBottom = 1f;
		MouseFilter = MouseFilterEnum.Ignore;

		_shaderMat = new ShaderMaterial { Shader = BuildShader() };
		_vignetteRect = new ColorRect
		{
			AnchorLeft = 0f, AnchorTop = 0f, AnchorRight = 1f, AnchorBottom = 1f,
			MouseFilter = MouseFilterEnum.Ignore,
			Material = _shaderMat,
		};
		AddChild(_vignetteRect);

		_shaderMat.SetShaderParameter(_strengthParam, 0f);
	}

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("HudLowHpFx._Process");
		var snap = NetMain.Instance?.Client?.LastSelfSnap;
		float hp = snap.HasValue ? snap.Value.Hp : 100f;

		if (hp >= WarnHpThreshold)
		{
			if (_lastAppliedStrength != 0f)
			{
				_shaderMat.SetShaderParameter(_strengthParam, 0f);
				_lastAppliedStrength = 0f;
			}
			return;
		}

		_time += (float)delta;
		float baseStrength = Mathf.Lerp(MaxAlpha, MaxAlpha * 0.4f, hp / WarnHpThreshold);
		float pulseHz = PulseFreq * (1f + (1f - Mathf.Clamp(hp / WarnHpThreshold, 0f, 1f)) * 0.8f);
		float pulse = 0.85f + 0.15f * Mathf.Sin(_time * Mathf.Tau * pulseHz);
		float strength = baseStrength * pulse;

		_shaderMat.SetShaderParameter(_strengthParam, strength);
		_lastAppliedStrength = strength;
	}

	private static Shader BuildShader()
	{
		var sh = new Shader();
		sh.Code = @"
shader_type canvas_item;
uniform float strength : hint_range(0.0, 1.0) = 0.0;

void fragment() {
    vec2 uv = SCREEN_UV - vec2(0.5);
    float d = length(uv) * 1.4142136;   // 0 at center, 1 at corners
    float ring = smoothstep(0.45, 1.0, d);
    vec3 col = vec3(0.85, 0.05, 0.05);
    COLOR = vec4(col, ring * strength);
}
";
		return sh;
	}
}
