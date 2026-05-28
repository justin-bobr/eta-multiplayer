// Mech Gunfire — Muzzleflash Shader (FPS-Billboard-Variante)
// Source: godotshaders.com/shader/mech-gunfire-effect-muzzleflash-shader/
// License: CC0 (Public Domain). Derivative für FPS: Billboard-Vertex-Block aus dem
// passenden Smoke-Shader übernommen damit der Flash im First-Person-View die Kamera ansieht.
//
// Uniforms:
//   texture_albedo            — Flash-Sprite (PNG/TGA mit Alpha, optional Sprite-Sheet)
//   particles_anim_h_frames   — Sprite-Sheet Spalten
//   particles_anim_v_frames   — Sprite-Sheet Reihen
//
// Verwendung:
//   GpuParticles3D mit ShaderMaterial. AnimOffsetMin/Max in ProcessMaterial setzen
//   damit jeder Partikel ein zufälliges Frame aus dem Sheet zeigt. AngleMin/Max
//   in ProcessMaterial setzt INSTANCE_CUSTOM.x → Rotation pro Partikel.

shader_type spatial;
render_mode blend_add,depth_draw_opaque,cull_disabled,
diffuse_toon,specular_disabled,unshaded,shadows_disabled,
ambient_light_disabled,fog_disabled;
uniform sampler2D texture_albedo : source_color,filter_linear_mipmap,repeat_enable;

uniform int particles_anim_h_frames;
uniform int particles_anim_v_frames;

void vertex() {
	// Billboard + per-particle rotation (Vertex-Block aus dem CC0 Smoke-Shader).
	mat4 mat_world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]) ,normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	mat_world = mat_world * mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * mat_world;
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);

	// Sprite-Sheet UV-Picking (Original-Flash-Code, unverändert).
	float h_frames = float(particles_anim_h_frames);
	float v_frames = float(particles_anim_v_frames);
	float particle_total_frames = float(particles_anim_h_frames * particles_anim_v_frames);
	float particle_frame = floor(INSTANCE_CUSTOM.z * float(particle_total_frames));

	particle_frame = clamp(particle_frame, 0.0, particle_total_frames - 1.0);

	UV /= vec2(h_frames, v_frames);
	UV += vec2(mod(particle_frame, h_frames) / h_frames, floor((particle_frame + 0.5) / h_frames) / v_frames);
}

float inverse_fresnel(float amount, vec3 normal, vec3 view){
	return pow(clamp(dot(normalize(normal), normalize(view)), 0.0, 1.0), amount);
}

void fragment() {
	vec4 albedo_tex = texture(texture_albedo,UV);
	ALBEDO = albedo_tex.rgb * COLOR.rgb * albedo_tex.a * 4.0;
	ALPHA = (albedo_tex.a * inverse_fresnel(1.5, NORMAL, VIEW) * COLOR.a);
	ALPHA_SCISSOR_THRESHOLD = 0.001;
}
