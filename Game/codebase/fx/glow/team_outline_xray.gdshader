shader_type spatial;
// Through-wall RIM (not fill) — renders the silhouette's grazing-angle edge ONLY when the
// puppet is occluded by world geometry. cull_back front-faces + fresnel-style ndv gives a soft
// rim at silhouette tangent (NORMAL ⊥ VIEW) and is suppressed at face-on surfaces (NORMAL ≈
// VIEW); the occlusion gate masks the rim to wall-behind-puppet pixels only. Result: visible-
// side puppet shows the outline_hull rim + halo only; behind-wall puppet shows just an outline
// (CS:GO / TF2 wallhack look — no solid fill).
//
// Triangle artifacts at component normal discontinuities (vest-shirt seam, pouch perimeter) are
// possible in the rim through walls, since fresnel uses per-fragment NORMAL. For wallhack use
// case this is acceptable — the rim is dim and only shown when occluded, so brief artifacts
// during camera motion don't stand out.
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
	// depth_diff > 0 = scene is closer to camera than fragment = wall in front of puppet at this
	// pixel. Smoothstep band over 2× tolerance keeps the alpha transition soft.
	float occlusion = smoothstep(depth_tolerance * 0.5, depth_tolerance * 2.0, depth_diff);

	// Fresnel rim: 1 at silhouette tangent grazing angles, 0 at face-on surfaces.
	float ndv = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float rim = pow(1.0 - ndv, fresnel_power);

	ALBEDO = team_color.rgb;
	ALPHA = team_color.a * alpha * occlusion * rim;
}
