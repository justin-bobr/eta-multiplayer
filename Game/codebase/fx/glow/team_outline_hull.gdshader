shader_type spatial;

render_mode unshaded, cull_front, depth_test_disabled, depth_draw_never, shadows_disabled, blend_mix;

uniform vec4 team_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);
uniform float outline_width_base : hint_range(0.0, 10.0) = 0.004;
uniform float alpha : hint_range(0.0, 1.0) = 1.0;
uniform float edge_interior_clamp : hint_range(0.0, 2.0) = 0.10;
uniform float edge_extension_full : hint_range(-10.0, 0.0) = -3.00;
uniform float self_clip_distance : hint_range(0.05, 5.0) = 0.50;

uniform float depth_bias : hint_range(0.0, 0.5) = 0.05;
uniform sampler2D depth_tex : hint_depth_texture, repeat_disable, filter_nearest;

void vertex() {

	VERTEX += NORMAL * outline_width_base;
}

void fragment() {
	float scene_ndc = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, scene_ndc, 1.0);
	vec4 view_pos4 = INV_PROJECTION_MATRIX * ndc;
	float scene_view_z = view_pos4.z / view_pos4.w;

	if (scene_view_z > -self_clip_distance && VERTEX.z < scene_view_z - 1.0) {
		scene_view_z = -1e9;
	}

	float depth_diff = scene_view_z - (VERTEX.z - depth_bias);
	float edge_gate = smoothstep(edge_interior_clamp, edge_extension_full, depth_diff);

	ALBEDO = team_color.rgb;
	ALPHA = team_color.a * alpha * edge_gate;
}
