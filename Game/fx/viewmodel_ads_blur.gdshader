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
// blend_premul_alpha is required: transparent_bg viewports output premultiplied alpha
// (Godot 4.3+). The default blend_mix multiplies edge RGB by alpha a second time,
// producing dark jagged fringes around the weapon silhouette against bright walls.
// The gaussian taps stay in premultiplied space, so the sum can be written out as-is.
render_mode unshaded, blend_premul_alpha;

uniform float ads_blend : hint_range(0.0, 1.0) = 0.0;     // 0..1 — vom Script gefüttert
uniform float max_blur_pixels : hint_range(0.0, 8.0) = 3.0;  // wie stark der Rand-Blur wird
uniform vec2 focus_center = vec2(0.5, 0.55);              // Bildschirm-UV der Iron-Sight (Mitte, leicht unter Zentrum)
uniform float focus_radius = 0.10;                        // schmaler Bereich um Iron-Sight bleibt scharf
uniform float blur_falloff = 0.30;                        // ab focus_radius+falloff voller Blur
// Luma unsharp mask for the weapon. The viewmodel never goes through an FSR upscaler, so on
// the FSR presets the world gets RCAS-sharpened while the weapon would stay soft — this is the
// single sharpening point for the viewmodel on every pipeline (driven from LocalPlayer.View,
// 0 when the user's Sharpening toggle is off).
uniform float sharpen_strength : hint_range(0.0, 2.0) = 0.5;

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
	vec4 col = sum / 16.0;

	// Sharpen in straight-alpha space: premultiplied RGB of transparent neighbours is black,
	// so a naive unsharp mask would darken every silhouette edge. The neighbour average is
	// alpha-weighted instead, the result is scaled back by center alpha (stays premultiplied)
	// and damped both at low-coverage edge pixels (avg neighbour alpha) and inside the active
	// ADS blur zone (sharpening would fight the pseudo-DOF).
	if (sharpen_strength > 0.0) {
		vec2 spx = TEXTURE_PIXEL_SIZE;
		vec4 n0 = texture(TEXTURE, uv + vec2( spx.x, 0.0));
		vec4 n1 = texture(TEXTURE, uv + vec2(-spx.x, 0.0));
		vec4 n2 = texture(TEXTURE, uv + vec2(0.0,  spx.y));
		vec4 n3 = texture(TEXTURE, uv + vec2(0.0, -spx.y));
		float wsum = n0.a + n1.a + n2.a + n3.a;
		vec3 blur_straight = (n0.rgb + n1.rgb + n2.rgb + n3.rgb) / max(wsum, 1e-4);
		vec3 center_straight = col.rgb / max(col.a, 1e-4);
		const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);
		float hp = dot(center_straight - blur_straight, LUMA) * sharpen_strength;
		hp *= wsum * 0.25;
		hp *= 1.0 - min(blur, 1.0);
		col.rgb += clamp(hp, -0.5, 0.5) * col.a;
	}

	COLOR = col;
}
