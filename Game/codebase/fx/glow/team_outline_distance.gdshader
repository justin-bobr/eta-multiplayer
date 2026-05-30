shader_type spatial;
// Outer rim — wider band around the silhouette at LOWER opacity than team_outline_hull's inner
// rim, no distance ramp. Combined with the hull pass: outer rim renders the wider band at e.g.
// 35% alpha, hull pass renders a tighter inner band at 100% alpha on top → CS:GO-style "inner
// bright + outer soft fade" silhouette glow. Vertex push scales with view_dist for constant
// screen-pixel thickness; you can crank outline_width_base to make the outer halo bigger.
render_mode unshaded, cull_front, depth_test_disabled, depth_draw_never, shadows_disabled, blend_mix;

uniform vec4 team_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);
uniform float outline_width_base : hint_range(0.0, 30.0) = 0.012;
uniform float alpha : hint_range(0.0, 1.0) = 0.35;
uniform float edge_interior_clamp : hint_range(0.0, 2.0) = 0.10;
uniform float edge_extension_full : hint_range(-10.0, 0.0) = -3.00;
uniform float self_clip_distance : hint_range(0.05, 5.0) = 0.50;
// Bias frag depth deeper before scene-comparison — skin-merge offset puts the silhouette mesh
// a few mm to cm CLOSER to camera than the lit body, which otherwise produces small negative
// depth_diff at interior → leaky ~10-20% alpha → inward glow. 5cm bias virtually pushes shell
// behind the lit body for typical offsets.
uniform float depth_bias : hint_range(0.0, 0.5) = 0.05;
uniform sampler2D depth_tex : hint_depth_texture, repeat_disable, filter_nearest;

void vertex() {
	// World-space constant push (no view_dist multiplier) — outline scales naturally with the
	// body in screen space, same width relative to body at any distance.
	VERTEX += NORMAL * outline_width_base;
}

void fragment() {
	float scene_ndc = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, scene_ndc, 1.0);
	vec4 view_pos4 = INV_PROJECTION_MATRIX * ndc;
	float scene_view_z = view_pos4.z / view_pos4.w;
	// Self-clip only if frag is meters behind scene — that's the local TPS body wrapping the
	// FPS camera. For close remote teammate (frag ≈ scene, both near), don't self-clip.
	if (scene_view_z > -self_clip_distance && VERTEX.z < scene_view_z - 1.0) {
		scene_view_z = -1e9;
	}
	float depth_diff = scene_view_z - (VERTEX.z - depth_bias);
	float edge_gate = smoothstep(edge_interior_clamp, edge_extension_full, depth_diff);

	ALBEDO = team_color.rgb;
	ALPHA = team_color.a * alpha * edge_gate;
}
