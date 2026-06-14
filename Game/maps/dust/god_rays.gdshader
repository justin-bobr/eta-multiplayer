shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, depth_test_disabled, fog_disabled;

const int SAMPLES = 32;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;

uniform vec3 sun_direction = vec3(-0.287, 0.819, 0.497);

uniform vec3 ray_color : source_color = vec3(1.0, 0.9, 0.72);
uniform float ray_strength : hint_range(0.0, 4.0) = 0.6;
uniform float density : hint_range(0.1, 1.0) = 0.95;
uniform float decay : hint_range(0.8, 1.0) = 0.96;
uniform float weight : hint_range(0.0, 0.2) = 0.055;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float sample_sky(vec2 uv) {

	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
		return 0.0;
	}
	return texture(depth_texture, uv).r > 0.0 ? 0.0 : 1.0;
}

void fragment() {

	vec4 clip = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(normalize(sun_direction), 0.0));
	vec3 c = vec3(0.0);
	if (clip.w > 0.0001) {
		vec2 sun_uv = (clip.xy / clip.w) * 0.5 + 0.5;
		vec2 off = max(vec2(0.0), max(-sun_uv, sun_uv - 1.0));
		float vis = clamp(1.0 - length(off) / 0.5, 0.0, 1.0);
		if (vis > 0.0) {

			vec2 march = (SCREEN_UV - sun_uv) * density / float(SAMPLES);
			vec2 uv = SCREEN_UV;
			float illum = 1.0;
			float light = 0.0;
			for (int i = 0; i < SAMPLES; i++) {
				uv -= march;
				light += sample_sky(uv) * illum * weight;
				illum *= decay;
			}
			float falloff = clamp(1.0 - distance(SCREEN_UV, sun_uv), 0.0, 1.0);
			c = ray_color * light * ray_strength * vis * falloff;
		}
	}
	ALBEDO = c;
}
