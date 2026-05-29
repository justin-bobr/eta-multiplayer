shader_type spatial;
render_mode blend_add,depth_draw_never,cull_disabled,
diffuse_toon,specular_disabled,unshaded,shadows_disabled,
ambient_light_disabled,fog_disabled;
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
	ALBEDO = albedo_tex.rgb * COLOR.rgb * albedo_tex.a;
	float base_alpha = albedo_tex.a * COLOR.a;

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
