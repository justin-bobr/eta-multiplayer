shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, specular_schlick_ggx;

// ---------------------------------------------------------------------------
// AAA window-glass shader.
// Refracted background goes into EMISSION (so it does NOT get re-lit by the
// scene's sun/ambient), dirt goes into ALBEDO (so it DOES get lit normally),
// and the PBR pipeline handles fresnel + environment reflection automatically
// via METALLIC=0, SPECULAR, ROUGHNESS. Per-pixel blur is implemented through
// SCREEN_TEXTURE mipmaps — dirt locally adds roughness AND blur.
//
// Optional procedural layers:
//   - milkiness: whitens the refraction + boosts blur for frosted-glass look
//   - procedural dirt: voronoi splotches + fbm grime (toggle, no texture)
//   - procedural scratches: rotated thin-line layers; kick the normal so the
//     sun's specular catches them as glints (no texture)
// ---------------------------------------------------------------------------

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D glass_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D dirt_tex : source_color, hint_default_black, filter_linear_mipmap, repeat_enable;

group_uniforms refraction;
uniform float distortion_size : hint_range(0.0, 0.3) = 0.04;
uniform vec2 normal_tiling = vec2(1.0, 1.0);
uniform float normal_strength : hint_range(0.0, 2.0) = 1.0;

group_uniforms color;
uniform vec3 glass_tint : source_color = vec3(0.85, 0.95, 1.0);
uniform float tint_strength : hint_range(0.0, 1.0) = 0.2;
uniform float brightness : hint_range(0.0, 2.0) = 1.0;

group_uniforms blur;
// Mip-LOD bias for the screen sample. 0 = crystal clear, ~4 = frosted glass.
uniform float blur_amount : hint_range(0.0, 8.0) = 0.0;
// Milky / frosted: mixes a whitened tone into the refraction and also adds
// extra blur-LOD. milkiness=1 alone gives a believable frosted pane;
// combine with blur_amount > 1 for full milk-glass.
uniform float milkiness : hint_range(0.0, 1.0) = 0.0;
uniform vec3 milk_color : source_color = vec3(0.92, 0.94, 0.96);

group_uniforms surface;
uniform float roughness : hint_range(0.0, 1.0) = 0.04;
uniform float specular_amount : hint_range(0.0, 1.0) = 0.5;

group_uniforms dirt;
uniform float dirt_amount : hint_range(0.0, 1.0) = 0.0;
// false → samples dirt_tex; true → fully procedural (no texture needed).
uniform bool dirt_procedural = false;
uniform vec2 dirt_tiling = vec2(2.0, 2.0);
uniform vec3 dirt_color : source_color = vec3(0.32, 0.33, 0.30);
uniform float dirt_roughness : hint_range(0.0, 1.0) = 0.75;
uniform float dirt_blur_boost : hint_range(0.0, 6.0) = 3.0;
uniform float dirt_contrast : hint_range(0.5, 4.0) = 1.6;
uniform float dirt_floor : hint_range(0.0, 1.0) = 0.0;
// Procedural-only knobs.
uniform float dirt_proc_scale : hint_range(0.5, 30.0) = 6.0;
// Higher = more isolated splotches, lower = uniform grime film.
uniform float dirt_proc_clump : hint_range(0.0, 1.0) = 0.55;

group_uniforms scratches;
// Master mix for procedural scratches. 0 = none.
uniform float scratch_amount : hint_range(0.0, 1.0) = 0.0;
uniform float scratch_scale : hint_range(1.0, 60.0) = 18.0;
// Per-cell probability a scratch appears. ~0.04 = sparse, ~0.2 = dense.
uniform float scratch_density : hint_range(0.0, 0.3) = 0.04;
uniform float scratch_thickness : hint_range(0.0, 0.5) = 0.08;
// Scratches push the normal sideways, so the sun's spec catches them as glints.
uniform float scratch_normal_bump : hint_range(0.0, 1.0) = 0.6;
// Baseline brightness on the scratch line itself — makes them slightly visible
// even when no light directly hits them. 0 = only spec-driven glints.
uniform float scratch_glint : hint_range(0.0, 2.0) = 0.4;

// --- helpers ---

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

vec2 hash22(vec2 p) {
	float n = hash21(p);
	return vec2(n, hash21(p + 17.7));
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	float s = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		s += a * vnoise(p);
		p *= 2.07;
		a *= 0.5;
	}
	return s;
}

// Worley distance — organic round splotches.
float worley(vec2 p) {
	vec2 ip = floor(p);
	vec2 fp = fract(p);
	float d = 1.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 g = vec2(float(x), float(y));
			vec2 o = hash22(ip + g);
			d = min(d, length(g + o - fp));
		}
	}
	return d;
}

float proc_dirt(vec2 uv) {
	float spots = 1.0 - clamp(worley(uv * dirt_proc_scale), 0.0, 1.0);
	float grime = fbm(uv * dirt_proc_scale * 0.7);
	float combined = spots * mix(0.5, 1.0, grime);
	return smoothstep(dirt_proc_clump, 1.0, combined);
}

// Scratches: 3 rotated thin-line layers. Per cell we hash-gate whether a line
// exists; lines run horizontally inside the rotated cell. Returns (mask, perpX, perpY)
// where perp is the cross-line direction in UV space — used to kick the normal.
vec3 proc_scratches(vec2 uv) {
	float total = 0.0;
	vec2 perp_sum = vec2(0.0);
	for (int i = 0; i < 3; i++) {
		float a = float(i) * 1.047 + 0.7;
		float cs = cos(a);
		float sn = sin(a);
		mat2 R = mat2(vec2(cs, -sn), vec2(sn, cs));
		vec2 ruv = R * uv * vec2(scratch_scale, scratch_scale * 8.0);
		vec2 cell = floor(ruv);
		vec2 f = fract(ruv);
		float gate = step(hash21(cell + float(i) * 11.3), scratch_density);
		float line = smoothstep(scratch_thickness, 0.0, abs(f.y - 0.5));
		float jit = step(0.15, hash21(cell + float(i) * 7.7));
		float mask = gate * line * jit;
		float side = sign(f.y - 0.5);
		vec2 perp = vec2(-sn, cs);
		perp_sum += perp * mask * side;
		total = max(total, mask);
	}
	return vec3(clamp(total, 0.0, 1.0), perp_sum);
}

void fragment() {
	vec3 n_sample = texture(glass_normal, UV * normal_tiling).rgb;
	vec3 n_tangent = n_sample * 2.0 - 1.0;

	float scratch_mask = 0.0;
	if (scratch_amount > 0.0) {
		vec3 scr = proc_scratches(UV);
		scratch_mask = scr.x * scratch_amount;
		n_tangent.xy += scr.yz * scratch_mask * scratch_normal_bump;
	}
	NORMAL_MAP = normalize(n_tangent) * 0.5 + 0.5;
	NORMAL_MAP_DEPTH = normal_strength;

	vec2 nxy = n_sample.rg * 2.0 - 1.0;
	vec2 refr_uv = SCREEN_UV + nxy * distortion_size;

	float dirt_raw = 0.0;
	if (dirt_amount > 0.0) {
		dirt_raw = dirt_procedural ? proc_dirt(UV) : texture(dirt_tex, UV * dirt_tiling).r;
	}
	float dirt_mask = clamp(pow(dirt_raw, dirt_contrast) + dirt_floor, 0.0, 1.0);
	float dirt = dirt_mask * dirt_amount;

	float lod = blur_amount + dirt * dirt_blur_boost + milkiness * 3.0;
	vec3 scene = textureLod(SCREEN_TEXTURE, refr_uv, lod).rgb;

	vec3 tinted = mix(scene, scene * glass_tint, tint_strength) * brightness;
	float scene_lum = dot(tinted, vec3(0.299, 0.587, 0.114));
	vec3 milky = mix(tinted, milk_color * mix(scene_lum, 1.0, 0.5), milkiness);

	EMISSION = milky * (1.0 - dirt) + vec3(scratch_glint) * scratch_mask;
	ALBEDO = dirt_color * dirt;

	METALLIC = 0.0;
	float r = mix(roughness, dirt_roughness, dirt);
	r = mix(r, dirt_roughness, scratch_mask * 0.7);
	r = max(r, milkiness * 0.6);
	ROUGHNESS = r;
	SPECULAR = specular_amount;

	ALPHA = 1.0;
}
