shader_type canvas_item;
// Valorant / L4D-style team-glow composite. Two SubViewport textures (kept in sync by
// GlowViewportSync):
//   body_tex — solid team-coloured silhouette of teammate bodies. body_id writes ALPHA = 0.75
//              (BODY_MARKER) via premul_alpha so opaque-world leaks (α=1) are discarded by the
//              marker check; transparent_bg leaves α=0 outside.
//   text_tex — Label3D nameplates with Modulate.a = TEXT_MODULATE_CAP, so font glyphs land in
//              alpha range (0 .. TEXT_MODULATE_CAP). Sky / opaque leaks (α=1) above the cap.
//
// Output per pixel via blend_premul_alpha:
//   • Pixel OUTSIDE body, body neighbour within outline_thickness  → OPAQUE outline rim (α=1)
//   • Pixel INSIDE body, alpha = (BODY_MARKER − blurred_body)      → ALPHA-BLENDED inner fade
//                                                                   (close to edge ≈ alpha 1, deep
//                                                                   interior ≈ alpha 0, lit body
//                                                                   shows through unchanged)
//   • Text pixel                                                  → ALPHA-BLEND label
//   • None of the above                                           → DISCARD, scene shows through
//
// Inner-fade math (Daniel Ilett / L4D technique adapted to Godot):
//   blurred_body ≈ fraction of nearby pixels that are also body, in a disk of inner_fade_thickness
//   radius around this pixel. Computed by sampling 4 rings × 8 directions = 32 disk samples.
//   At deep body centre, all 32 samples land inside body → blurred ≈ BODY_MARKER → (BODY_MARKER −
//   blurred) ≈ 0 → transparent (deep interior shows lit body).
//   Right at the body edge, ~16 of 32 samples land outside → blurred ≈ BODY_MARKER × 0.5 →
//   (BODY_MARKER − blurred) ≈ 0.5 → mid-alpha glow rim.
//   The sharp outline_thickness rim outside the body is a separate path (8-sample radial dilation).
//
// blend_premul_alpha lets one shader express opaque-replace (α=1), alpha-blend (0<α<1), and
// additive (α=0) per pixel — formula: out = src.rgb + (1 − src.a) × dst.
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

	// Sharp outline rim: 8-sample radial dilation at outline_thickness pixels. Pixels OUTSIDE the
	// body that find a body sample within the ring become the opaque outline band.
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

	// Inner fade: disk-blur the body marker, then alpha = (BODY_MARKER − blurred) gives a smooth
	// gradient from 0 (deep inside, blurred ≈ BODY_MARKER) to BODY_MARKER (right at edge, blurred ≈ 0).
	// Only computed for body pixels — outside pixels use the outline path above.
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
