shader_type spatial;
// Lighting is ON (no `unshaded`, no `ambient_light_disabled`) so the smoke picks up the scene's
// ambient + directional light — bright in sunlight, darker in shadow, tinted by colored lights.
// Shadows are off because shadow-mapping a transparent billboard produces visible square edges.
// Fog left on so distant smoke blends with the atmospheric haze like the rest of the scene.
render_mode blend_mix,depth_draw_never,cull_disabled,
specular_disabled,shadows_disabled;

uniform sampler2D texture_albedo : source_color,filter_linear_mipmap,repeat_enable;

uniform int particles_anim_h_frames;
uniform int particles_anim_v_frames;
//uniform bool particles_anim_loop;

// Godot 4.6 removed the DEPTH_TEXTURE builtin — must be declared as a hint_depth_texture uniform.
// Sampler is auto-wired by the engine; no manual binding needed. Nearest filter because depth
// textures have no mipmaps in the Forward+ pipeline.
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

// Soft-particle fade distance (metres). When the billboard's view-space depth is closer than
// this much to the scene's depth at the same pixel, alpha fades to zero — eliminates the hard
// cut-off line where the billboard quad intersects walls/floors. 0.5m matches the typical
// muzzle-smoke puff size; raise for larger smoke clouds.
uniform float soft_fade_distance : hint_range(0.05, 2.0) = 0.5;

// Baseline-brightness via EMISSION — guarantees the smoke is visible even when no light hits it
// (e.g. indoor shadow with no ambient probe). 0 = pure lighting (can go fully black), 1 = always
// at full albedo regardless of light. 0.4 is a good "lit but never invisible" default.
uniform float emission_baseline : hint_range(0.0, 1.0) = 0.4;

void vertex() {
	mat4 mat_world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]) ,normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	mat_world = mat_world * mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * mat_world;
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	
	float h_frames = float(particles_anim_h_frames);
	float v_frames = float(particles_anim_v_frames);
	float particle_total_frames = float(particles_anim_h_frames * particles_anim_v_frames);
	float particle_frame = floor(INSTANCE_CUSTOM.z * float(particle_total_frames));
	
	particle_frame = clamp(particle_frame, 0.0, particle_total_frames - 1.0);
	// We don't want to cycle for particle animation frames, only offset, so the condition is removed.
	//if (!particles_anim_loop) {
		//particle_frame = clamp(particle_frame, 0.0, particle_total_frames - 1.0);
	//} else {
		//particle_frame = mod(particle_frame, particle_total_frames);
	//}
	
	UV /= vec2(h_frames, v_frames);
	UV += vec2(mod(particle_frame, h_frames) / h_frames, floor((particle_frame + 0.5) / h_frames) / v_frames);
}

void fragment() {
	vec4 albedo_tex = texture(texture_albedo,UV);
	// blend_mix = standard alpha-blend: pixel = ALBEDO * ALPHA + scene * (1 - ALPHA). ALBEDO is the
	// straight color (no premultiplied-alpha math), ALPHA controls opacity. This makes the smoke
	// look like semi-opaque gray puffs that OCCLUDE the wall instead of additively brightening it.
	ALBEDO = albedo_tex.rgb * COLOR.rgb;
	float base_alpha = albedo_tex.a * COLOR.a;

	// Baseline brightness so the smoke is visible even in unlit interior shadow. Full lighting
	// adds on top of this, so muzzle smoke in direct sunlight is brighter than smoke in a basement.
	EMISSION = ALBEDO * emission_baseline;

	// Soft-particle fade: sample the scene's depth at this pixel, convert to view-space metres,
	// and fade the billboard out as it approaches the scene surface. VERTEX is view-space in
	// fragment for shader_type spatial, so -VERTEX.z is the particle's own view-space depth (m).
	float scene_z_raw = texture(depth_tex, SCREEN_UV).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, scene_z_raw, 1.0);
	vec4 scene_view = INV_PROJECTION_MATRIX * ndc;
	float scene_depth_linear = -scene_view.z / scene_view.w;
	float particle_depth_linear = -VERTEX.z;
	float soft_fade = clamp((scene_depth_linear - particle_depth_linear) / soft_fade_distance, 0.0, 1.0);

	ALPHA = base_alpha * soft_fade;
}
