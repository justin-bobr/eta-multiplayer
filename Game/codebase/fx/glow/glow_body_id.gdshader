shader_type spatial;

render_mode unshaded, depth_test_disabled, depth_draw_never, shadows_disabled, cull_back, blend_premul_alpha;

uniform vec4 team_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);

const float MARKER = 0.75;

void fragment() {
	ALBEDO = team_color.rgb * MARKER;
	ALPHA = MARKER;
}
