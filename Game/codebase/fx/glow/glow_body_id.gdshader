shader_type spatial;
// Body-ID render pass. Uses blend_premul_alpha with ALPHA = MARKER (0.75) so the composite shader
// can discriminate body pixels from any opaque-world content that may leak into body_tex despite
// the env override (sky alpha = 1.0 gets rejected by the abs(α − 0.75) < 0.15 marker check).
// Pre-multiplies ALBEDO by MARKER so the colour survives the premul_alpha blend over the transparent
// SubViewport background:
//   src = (team * MARKER, MARKER), dst = (0,0,0,0) → out.rgb = team * MARKER, out.a = MARKER
// Composite divides by MARKER to recover the pixel-perfect team colour.
//
// depth_test_disabled keeps occluded body fragments stamping the silhouette behind walls — the
// wallhack visibility CS2 / Valorant teammates rely on. depth_draw_never leaves the SubViewport's
// depth attachment alone (we don't read it).
render_mode unshaded, depth_test_disabled, depth_draw_never, shadows_disabled, cull_back, blend_premul_alpha;

uniform vec4 team_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);

const float MARKER = 0.75;

void fragment() {
	ALBEDO = team_color.rgb * MARKER;
	ALPHA = MARKER;
}
