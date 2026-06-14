shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled,
unshaded, shadows_disabled, ambient_light_disabled, fog_disabled, specular_disabled;

uniform sampler2D texture_albedo : source_color, filter_linear_mipmap, repeat_disable;
uniform int particles_anim_h_frames;
uniform int particles_anim_v_frames;
uniform float energy = 4.0;
uniform float saturation = 1.0;
uniform vec3 tint : source_color = vec3(1.0);

void vertex() {
	mat4 mat_world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	mat_world = mat_world * mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * mat_world;
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);

	float h_frames = float(particles_anim_h_frames);
	float v_frames = float(particles_anim_v_frames);
	float total = float(particles_anim_h_frames * particles_anim_v_frames);
	float frame = clamp(floor(INSTANCE_CUSTOM.z * total), 0.0, total - 1.0);
	UV /= vec2(h_frames, v_frames);
	UV += vec2(mod(frame, h_frames) / h_frames, floor((frame + 0.5) / h_frames) / v_frames);
}

void fragment() {
	vec3 tex = texture(texture_albedo, UV).rgb;
	float clip = max(tex.r, max(tex.g, tex.b));
	float grey = dot(tex, vec3(0.299, 0.587, 0.114));
	vec3 col = mix(vec3(grey), tex, saturation) * tint;
	col = min(col, vec3(1.0));
	ALBEDO = col * COLOR.rgb * energy;
	ALPHA = clamp(clip, 0.0, 1.0) * COLOR.a;
	ALPHA_SCISSOR_THRESHOLD = 0.02;
}
