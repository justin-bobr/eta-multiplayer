shader_type spatial;
// Inner rim — thin, FULL opacity, always on (no distance ramp). Renders only at the silhouette
// rim extension via explicit depth-gate (scene-vs-fragment view-space comparison): inside the
// lit body's silhouette → suppressed, outside (over wall/sky/floor) → rendered.
render_mode unshaded, cull_front, depth_test_disabled, depth_draw_never, shadows_disabled, blend_mix;

uniform vec4 team_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);
uniform float outline_width_base : hint_range(0.0, 10.0) = 0.004;
uniform float alpha : hint_range(0.0, 1.0) = 1.0;
uniform float edge_interior_clamp : hint_range(0.0, 2.0) = 0.10;
uniform float edge_extension_full : hint_range(-10.0, 0.0) = -3.00;
uniform float self_clip_distance : hint_range(0.05, 5.0) = 0.50;
// Push fragment depth slightly deeper before comparing to scene depth. The merged silhouette
// mesh frequently sits a few mm to a few cm CLOSER to camera than the lit body materials due
// to skin-merge offset (different source meshes had different Skin resources, merge uses the
// first one and the rest skin slightly forward). Without bias, depth_diff is slightly negative
// at interior pixels → edge_gate gives 10-20% alpha → looks like an inward glow into the body.
// 5cm bias is enough to push virtually behind the lit body for typical skin offsets.
uniform float depth_bias : hint_range(0.0, 0.5) = 0.05;
uniform sampler2D depth_tex : hint_depth_texture, repeat_disable, filter_nearest;

void vertex() {
	// World-space constant push: outline scales with the body in screen space (smaller in pixels
	// at distance, same relative size to the body). The earlier `* view_dist` multiplier kept the
	// outline at constant screen pixels, which made the rim look disproportionately thick on far-
	// away puppets.
	VERTEX += NORMAL * outline_width_base;
}

void fragment() {
	float scene_ndc = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, scene_ndc, 1.0);
	vec4 view_pos4 = INV_PROJECTION_MATRIX * ndc;
	float scene_view_z = view_pos4.z / view_pos4.w;
	// Self-clip discriminator: local TPS body wraps the FPS camera so scene_z is tiny while the
	// remote puppet's shell is meters away (big frag-scene gap → local body). Standing close to
	// a remote teammate also has tiny scene_z, but frag-scene gap is also tiny — must NOT self-
	// clip there, else the puppet interior fills green at close range.
	if (scene_view_z > -self_clip_distance && VERTEX.z < scene_view_z - 1.0) {
		scene_view_z = -1e9;
	}
	// Bias fragment depth slightly deeper before comparison — see depth_bias uniform doc.
	float depth_diff = scene_view_z - (VERTEX.z - depth_bias);
	float edge_gate = smoothstep(edge_interior_clamp, edge_extension_full, depth_diff);

	ALBEDO = team_color.rgb;
	ALPHA = team_color.a * alpha * edge_gate;
}
