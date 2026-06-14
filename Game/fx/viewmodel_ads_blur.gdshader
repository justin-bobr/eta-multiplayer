shader_type canvas_item;

render_mode unshaded, blend_premul_alpha;

uniform float ads_blend : hint_range(0.0, 1.0) = 0.0;
uniform float max_blur_pixels : hint_range(0.0, 8.0) = 3.0;
uniform vec2 focus_center = vec2(0.5, 0.55);
uniform float focus_radius = 0.10;
uniform float blur_falloff = 0.30;

uniform float sharpen_strength : hint_range(0.0, 2.0) = 0.5;

void fragment() {
	vec2 uv = UV;
	float dist = length(uv - focus_center);
	float blur_t = smoothstep(focus_radius, focus_radius + blur_falloff, dist);
	float blur = blur_t * ads_blend * max_blur_pixels;

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
