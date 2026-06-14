shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, depth_test_disabled, fog_disabled;

const int GHOSTS = 5;

uniform vec3 sun_direction = vec3(-0.287, 0.819, 0.497);

uniform vec3 flare_color : source_color = vec3(1.0, 0.86, 0.62);
uniform float flare_strength : hint_range(0.0, 3.0) = 0.6;
uniform float ghost_spacing : hint_range(0.05, 0.6) = 0.28;
uniform float halo_strength : hint_range(0.0, 2.0) = 0.5;
uniform float dirt_strength : hint_range(0.0, 2.0) = 0.6;
uniform sampler2D dirt_texture : hint_default_black;
uniform sampler2D depth_texture : hint_depth_texture;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	vec4 clip = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(normalize(sun_direction), 0.0));
	vec3 c = vec3(0.0);
	if (clip.w > 0.0001) {
		vec2 sun_uv = (clip.xy / clip.w) * 0.5 + 0.5;
		vec2 off = max(vec2(0.0), max(-sun_uv, sun_uv - 1.0));
		float vis = clamp(1.0 - length(off) / 0.4, 0.0, 1.0);

		vec2 cuv = clamp(sun_uv, vec2(0.0), vec2(1.0));
		float s = step(texture(depth_texture, cuv).r, 0.0);
		s += step(texture(depth_texture, clamp(cuv + vec2(0.025, 0.0), vec2(0.0), vec2(1.0))).r, 0.0);
		s += step(texture(depth_texture, clamp(cuv - vec2(0.025, 0.0), vec2(0.0), vec2(1.0))).r, 0.0);
		s += step(texture(depth_texture, clamp(cuv + vec2(0.0, 0.025), vec2(0.0), vec2(1.0))).r, 0.0);
		s += step(texture(depth_texture, clamp(cuv - vec2(0.0, 0.025), vec2(0.0), vec2(1.0))).r, 0.0);
		float sun_clear = s / 5.0;

		if (vis * sun_clear > 0.0) {
			float aspect = VIEWPORT_SIZE.x / VIEWPORT_SIZE.y;
			vec2 p = vec2(SCREEN_UV.x * aspect, SCREEN_UV.y);
			vec2 sun = vec2(sun_uv.x * aspect, sun_uv.y);
			vec2 center = vec2(0.5 * aspect, 0.5);
			vec2 ghost_vec = center - sun;

			for (int i = 1; i <= GHOSTS; i++) {
				vec2 gp = sun + ghost_vec * (ghost_spacing * float(i));
				float size = 0.05 + 0.02 * float(i);
				float blob = pow(clamp(1.0 - distance(p, gp) / size, 0.0, 1.0), 2.0);
				vec3 tint = mix(flare_color, flare_color.bgr, fract(float(i) * 0.5));
				c += blob * tint * 0.4;
			}
			float halo = smoothstep(0.05, 0.0, abs(distance(p, center) - 0.28));
			c += halo * flare_color * halo_strength * 0.3;
			float core = pow(clamp(1.0 - distance(p, sun) / 0.18, 0.0, 1.0), 3.0);
			c += core * flare_color;
			float sun_glow = pow(clamp(1.0 - distance(p, sun) / 0.9, 0.0, 1.0), 2.0);
			c += texture(dirt_texture, SCREEN_UV).rgb * dirt_strength * sun_glow * flare_color;
			c *= flare_strength * vis * sun_clear;
		}
	}
	ALBEDO = c;
}
