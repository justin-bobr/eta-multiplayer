// Dust mote / sand sparkle. Each particle is a tiny billboarded glint: a dim
// soft base + a sharp core that flickers + an occasional 4-point star flash.
// Per-particle twinkle phase comes from INSTANCE_ID so each mote sparkles
// independently. Bright peaks exceed 1.0 so the scene glow blooms them.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;

// Per-particle albedo is lerped between these two by the stable per-mote seed, so
// the field ranges white -> very light orange instead of one flat tint.
uniform vec3 dust_color_a : source_color = vec3(1.0, 1.0, 1.0);
uniform vec3 dust_color_b : source_color = vec3(1.0, 0.84, 0.58);
uniform float brightness : hint_range(0.0, 4.0) = 1.6;
uniform float sparkle : hint_range(0.0, 1.0) = 0.8;
uniform float twinkle_speed : hint_range(0.0, 10.0) = 3.5;

varying float v_seed;

float hash(float n) {
	return fract(sin(n * 12.9898) * 43758.5453);
}

void vertex() {
	// Particle billboard - camera-facing, keeping the per-particle scale.
	mat4 bb = mat4(
		INV_VIEW_MATRIX[0] * length(MODEL_MATRIX[0].xyz),
		INV_VIEW_MATRIX[1] * length(MODEL_MATRIX[1].xyz),
		INV_VIEW_MATRIX[2] * length(MODEL_MATRIX[2].xyz),
		MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = VIEW_MATRIX * bb;
	v_seed = hash(float(INSTANCE_ID));
}

void fragment() {
	vec2 uv = UV - vec2(0.5);
	float d = length(uv);

	// Per-particle twinkle phase.
	float tw01 = sin(TIME * twinkle_speed + v_seed * 6.2831853) * 0.5 + 0.5;

	// Dim soft base dot - always present.
	float base = smoothstep(0.5, 0.05, d);
	// Sharp bright core - flickers with the twinkle.
	float core = pow(smoothstep(0.28, 0.0, d), 4.0);
	// 4-point star glint - only flashes near twinkle peaks.
	vec2 a = abs(uv);
	float star = smoothstep(0.5, 0.0, a.x) * smoothstep(0.05, 0.0, a.y)
		+ smoothstep(0.5, 0.0, a.y) * smoothstep(0.05, 0.0, a.x);
	float flash = smoothstep(0.55, 1.0, tw01);

	float shape = base * 0.25
		+ core * (0.25 + 0.75 * tw01)
		+ star * flash * sparkle;

	vec3 dust_color = mix(dust_color_a, dust_color_b, v_seed);
	ALBEDO = dust_color * brightness * shape;
}
