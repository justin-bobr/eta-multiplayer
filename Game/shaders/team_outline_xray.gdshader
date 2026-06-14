shader_type spatial;

render_mode unshaded, cull_back, depth_test_disabled, depth_draw_never, shadows_disabled, blend_mix;

uniform vec4 team_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);
uniform float alpha : hint_range(0.0, 1.0) = 0.85;
uniform float fresnel_power : hint_range(0.5, 16.0) = 3.0;
uniform float depth_tolerance : hint_range(0.05, 3.0) = 0.50;
uniform float self_clip_distance : hint_range(0.05, 5.0) = 0.50;
uniform sampler2D depth_tex : hint_depth_texture, repeat_disable, filter_nearest;

void fragment() {
	float scene_ndc = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, scene_ndc, 1.0);
	vec4 view_pos4 = INV_PROJECTION_MATRIX * ndc;
	float scene_view_z = view_pos4.z / view_pos4.w;
	if (scene_view_z > -self_clip_distance && VERTEX.z < scene_view_z - 1.0) {
		scene_view_z = -1e9;
	}
	float depth_diff = scene_view_z - VERTEX.z;

	float occlusion = smoothstep(depth_tolerance * 0.5, depth_tolerance * 2.0, depth_diff);

	float ndv = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float rim = pow(1.0 - ndv, fresnel_power);

	ALBEDO = team_color.rgb;
	ALPHA = team_color.a * alpha * occlusion * rim;
}
