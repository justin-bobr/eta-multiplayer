shader_type spatial;

render_mode blend_mix,depth_draw_never,cull_disabled,
specular_disabled,shadows_disabled;

uniform sampler2D texture_albedo : source_color,filter_linear_mipmap,repeat_enable;

uniform int particles_anim_h_frames;
uniform int particles_anim_v_frames;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

uniform float soft_fade_distance : hint_range(0.05, 2.0) = 0.5;

uniform float emission_baseline : hint_range(0.0, 1.0) = 0.85;

uniform vec3 albedo_tint : source_color = vec3(1.5, 1.5, 1.5);

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

	UV /= vec2(h_frames, v_frames);
	UV += vec2(mod(particle_frame, h_frames) / h_frames, floor((particle_frame + 0.5) / h_frames) / v_frames);
}

void fragment() {
	vec4 albedo_tex = texture(texture_albedo,UV);

	ALBEDO = albedo_tex.rgb * COLOR.rgb * albedo_tint;
	float base_alpha = albedo_tex.a * COLOR.a;

	EMISSION = ALBEDO * emission_baseline;

	float scene_z_raw = texture(depth_tex, SCREEN_UV).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, scene_z_raw, 1.0);
	vec4 scene_view = INV_PROJECTION_MATRIX * ndc;
	float scene_depth_linear = -scene_view.z / scene_view.w;
	float particle_depth_linear = -VERTEX.z;
	float soft_fade = clamp((scene_depth_linear - particle_depth_linear) / soft_fade_distance, 0.0, 1.0);

	ALPHA = base_alpha * soft_fade;
}
