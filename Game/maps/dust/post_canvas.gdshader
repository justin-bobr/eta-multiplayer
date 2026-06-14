shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear_mipmap;

uniform float aberration : hint_range(0.0, 0.02) = 0.0026;
uniform float sharpen : hint_range(0.0, 2.0) = 0.25;
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.18;
uniform float vignette_radius : hint_range(0.2, 1.5) = 1.05;
uniform float vignette_ads_boost : hint_range(0.0, 0.5) = 0.15;
uniform float ads_blend = 0.0;
uniform float grain_strength : hint_range(0.0, 0.3) = 0.07;
uniform float time_seconds = 0.0;

float hash12(vec2 v) {
	return fract(sin(dot(v, vec2(127.1, 311.7))) * 43758.5453);
}

float vnoise(vec2 v) {
	vec2 i = floor(v);
	vec2 f = fract(v);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(hash12(i), hash12(i + vec2(1.0, 0.0)), f.x),
		mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), f.x),
		f.y);
}

vec3 grain_aaa(vec3 col, vec2 px_coord, float t, float strength) {
	float gt = floor(t * 14.0);
	float gtf = fract(t * 14.0);
	vec2 sf = px_coord / 1.5;
	vec2 sc = px_coord / 3.5;

	vec3 n;
	for (int c = 0; c < 3; c++) {
		vec2 chOff = vec2(float(c) * 23.7, float(c) * 41.1);
		vec2 tOff0 = vec2(gt * 0.77, gt * 0.43);
		vec2 tOff1 = vec2((gt + 1.0) * 0.77, (gt + 1.0) * 0.43);
		float fine = mix(vnoise(sf + tOff0 + chOff), vnoise(sf + tOff1 + chOff), gtf);
		float coarse = mix(vnoise(sc + tOff0 * 0.5 + chOff), vnoise(sc + tOff1 * 0.5 + chOff), gtf);
		n[c] = (fine * 0.7 + coarse * 0.3) - 0.5;
	}

	float luma = dot(col, vec3(0.299, 0.587, 0.114));
	float w = 4.0 * luma * (1.0 - luma);
	w *= smoothstep(0.05, 0.18, luma);
	w *= 1.0 - smoothstep(0.85, 1.0, luma);

	return col + n * strength * w * 0.8;
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 dir = uv - vec2(0.5);
	float r2 = dot(dir, dir);

	vec3 col;
	if (aberration > 0.0) {
		vec2 ca_offset = dir * aberration * (r2 * 4.0);
		col.r = texture(screen_tex, uv - ca_offset).r;
		col.g = texture(screen_tex, uv).g;
		col.b = texture(screen_tex, uv + ca_offset).b;
	} else {
		col = texture(screen_tex, uv).rgb;
	}

	if (sharpen > 0.0) {
		vec2 px = 1.0 / vec2(textureSize(screen_tex, 0));
		vec3 c_l = texture(screen_tex, uv - vec2(px.x, 0.0)).rgb;
		vec3 c_r = texture(screen_tex, uv + vec2(px.x, 0.0)).rgb;
		vec3 c_t = texture(screen_tex, uv - vec2(0.0, px.y)).rgb;
		vec3 c_b = texture(screen_tex, uv + vec2(0.0, px.y)).rgb;
		vec3 blur = (c_l + c_r + c_t + c_b) * 0.25;
		col = mix(col, col + (col - blur), sharpen);
	}

	float vig_amt = vignette_strength + vignette_ads_boost * ads_blend;
	float vig = smoothstep(0.5, vignette_radius, length(dir) * 1.4);
	col *= 1.0 - vig * vig_amt;

	if (grain_strength > 0.0) {
		vec2 px = SCREEN_UV * vec2(textureSize(screen_tex, 0));
		col = grain_aaa(col, px, time_seconds, grain_strength);
	}

	COLOR = vec4(max(col, vec3(0.0)), 1.0);
}
