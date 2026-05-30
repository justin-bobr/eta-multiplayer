shader_type canvas_item;

// Background defocus for the team-selection menu. Uses the screen back-buffer's mipmap chain
// instead of a manual sample grid — textureLod with mip ≥3 gives a perfectly smooth Gaussian-
// equivalent blur in a single tap, with no aliasing/tiling artefacts that a sparse 7×7 grid
// produced at the same visual strength. Cheap too: one texture fetch per pixel.
//
// `mip_level` 3 ≈ light defocus, 5 ≈ heavy CS2-menu blur, 6 ≈ extreme.
// `tint` is an opaque-alpha colour mixed on top — usually black with ~30% alpha to dim the BG.

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform float mip_level : hint_range(0.0, 8.0) = 5.0;
uniform vec4 tint : source_color = vec4(0.0, 0.0, 0.0, 0.35);

void fragment() {
	vec3 blurred = textureLod(screen_tex, SCREEN_UV, mip_level).rgb;
	COLOR = vec4(mix(blurred, tint.rgb, tint.a), 1.0);
}
