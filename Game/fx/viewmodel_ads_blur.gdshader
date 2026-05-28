// AAA-Style ADS-Near-DOF — Pseudo-Depth-of-Field für Viewmodel via 2D-Gaussian-Blur.
//
// Hintergrund: Godot 4 SubViewports ignorieren CameraAttributes-DOF wenn transparent_bg
// aktiv ist (Depth-Buffer wird fürs Compositing genutzt, nicht für Post-FX). Daher hier
// ein 2D-Pseudo-DOF: alles außer einer schmalen "Focus-Zone" (= Iron-Sight in der
// Bildmitte) wird beim ADS gaussian-geblurrt.
//
// Aktiviert über `ads_blend` (0=aus, 1=voll ADS). Update vom LocalAnimation.cs.
//
// SETUP: ShaderMaterial mit diesem Shader am SubViewportContainer (viewmodel_container)
// als `material`. Shader operiert auf der Viewport-Texture (= reine Viewmodel-Frame,
// alpha-transparent außen), nicht auf der composited Welt.

shader_type canvas_item;
render_mode unshaded;

uniform float ads_blend : hint_range(0.0, 1.0) = 0.0;     // 0..1 — vom Script gefüttert
uniform float max_blur_pixels : hint_range(0.0, 8.0) = 3.0;  // wie stark der Rand-Blur wird
uniform vec2 focus_center = vec2(0.5, 0.55);              // Bildschirm-UV der Iron-Sight (Mitte, leicht unter Zentrum)
uniform float focus_radius = 0.10;                        // schmaler Bereich um Iron-Sight bleibt scharf
uniform float blur_falloff = 0.30;                        // ab focus_radius+falloff voller Blur

void fragment() {
	vec2 uv = UV;
	float dist = length(uv - focus_center);
	float blur_t = smoothstep(focus_radius, focus_radius + blur_falloff, dist);
	float blur = blur_t * ads_blend * max_blur_pixels;

	// 9-Tap Gaussian (3x3 Kernel). Reicht für subtilen Near-DOF-Look.
	// Bei blur~0 ist `px` quasi 0 → alle 9 Samples lesen denselben Pixel → kein Effekt aber auch
	// keine "early return" möglich (Godot Shader verbietet `return` in fragment).
	vec2 px = TEXTURE_PIXEL_SIZE * blur;
	vec4 sum = vec4(0.0);
	sum += texture(TEXTURE, uv + vec2(-px.x, -px.y)) * 1.0;
	sum += texture(TEXTURE, uv + vec2( 0.0, -px.y)) * 2.0;
	sum += texture(TEXTURE, uv + vec2( px.x, -px.y)) * 1.0;
	sum += texture(TEXTURE, uv + vec2(-px.x,  0.0)) * 2.0;
	sum += texture(TEXTURE, uv)                      * 4.0;
	sum += texture(TEXTURE, uv + vec2( px.x,  0.0)) * 2.0;
	sum += texture(TEXTURE, uv + vec2(-px.x,  px.y)) * 1.0;
	sum += texture(TEXTURE, uv + vec2( 0.0,  px.y)) * 2.0;
	sum += texture(TEXTURE, uv + vec2( px.x,  px.y)) * 1.0;
	COLOR = sum / 16.0;
}
