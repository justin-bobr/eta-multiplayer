shader_type canvas_item;

render_mode blend_premul_alpha, unshaded;

uniform sampler2D body_tex : filter_linear, repeat_disable;
uniform sampler2D text_tex : filter_linear, repeat_disable;
uniform float outline_thickness : hint_range(1.0, 8.0) = 3.0;
uniform float inner_fade_thickness : hint_range(2.0, 32.0) = 14.0;
uniform float inner_fade_intensity : hint_range(0.0, 2.0) = 1.0;
uniform float outline_intensity : hint_range(0.5, 4.0) = 1.4;

const int OUTLINE_SAMPLE_COUNT = 8;
const int INNER_RING_COUNT = 4;
const int INNER_DIRECTION_COUNT = 8;
const float BODY_MARKER = 0.75;
const float BODY_MARKER_TOLERANCE = 0.15;
const float TEXT_MODULATE_CAP = 0.5;
const float TEXT_ALPHA_MIN = 0.02;
const float TEXT_ALPHA_MAX = 0.7;

bool is_body_marker(float a) {
	return abs(a - BODY_MARKER) < BODY_MARKER_TOLERANCE;
}

bool is_text_marker(float a) {
	return a > TEXT_ALPHA_MIN && a < TEXT_ALPHA_MAX;
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec4 body_here = texture(body_tex, uv);
	vec4 text_here = texture(text_tex, uv);
	vec2 px = 1.0 / vec2(textureSize(body_tex, 0));
	float two_pi = 6.2831853;

	bool body_at_self = is_body_marker(body_here.a);

	bool any_body_neighbor = false;
	vec3 outline_color = vec3(0.0);
	float inv_outline_n = 1.0 / float(OUTLINE_SAMPLE_COUNT);
	for (int i = 0; i < OUTLINE_SAMPLE_COUNT; i++) {
		float a = float(i) * two_pi * inv_outline_n;
		vec2 offs = vec2(cos(a), sin(a)) * outline_thickness * px;
		vec4 s = texture(body_tex, uv + offs);
		if (is_body_marker(s.a)) {
			any_body_neighbor = true;
			outline_color = s.rgb / BODY_MARKER;
		}
	}
	bool is_outside_edge = any_body_neighbor && !body_at_self;

	float inner_alpha = 0.0;
	vec3 inner_color = vec3(0.0);
	if (body_at_self) {
		float blur_sum = 0.0;
		float total_samples = float(INNER_RING_COUNT * INNER_DIRECTION_COUNT);
		float inv_dir_n = 1.0 / float(INNER_DIRECTION_COUNT);
		for (int r = 1; r <= INNER_RING_COUNT; r++) {
			float radius_px = float(r) * inner_fade_thickness / float(INNER_RING_COUNT);
			for (int d = 0; d < INNER_DIRECTION_COUNT; d++) {
				float a = float(d) * two_pi * inv_dir_n;
				vec2 offs = vec2(cos(a), sin(a)) * radius_px * px;
				vec4 s = texture(body_tex, uv + offs);
				if (is_body_marker(s.a)) blur_sum += 1.0;
			}
		}
		float blur_avg = blur_sum / total_samples;
		float edge_closeness = 1.0 - blur_avg;
		inner_alpha = clamp(edge_closeness * inner_fade_intensity, 0.0, 1.0);
		inner_color = (body_here.rgb / BODY_MARKER) * outline_intensity;
	}

	bool is_text = is_text_marker(text_here.a);
	bool has_inner_glow = inner_alpha > 0.005;

	if (!is_outside_edge && !has_inner_glow && !is_text) {
		discard;
	}

	vec3 rgb = vec3(0.0);
	float a = 0.0;

	if (has_inner_glow) {
		rgb = inner_color * inner_alpha;
		a = inner_alpha;
	}
	if (is_outside_edge) {
		rgb = outline_color * outline_intensity;
		a = 1.0;
	}
	if (is_text) {
		float glyph = text_here.a / TEXT_MODULATE_CAP;
		rgb = (text_here.rgb / TEXT_MODULATE_CAP) * glyph;
		a = glyph;
	}

	COLOR = vec4(rgb, a);
}
