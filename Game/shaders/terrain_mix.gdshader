shader_type spatial;
render_mode cull_back, depth_draw_opaque, specular_schlick_ggx;

group_uniforms Layer0;
uniform sampler2D tex_color_0 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_normal_0 : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_roughness_0 : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_packed_nr_0 = true;
uniform float roughness_0_mul : hint_range(0.0, 2.0) = 1.0;
group_uniforms;

group_uniforms Normals;
uniform bool flip_normal_y = true;
group_uniforms;

group_uniforms Layer1;
uniform sampler2D tex_color_1 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_normal_1 : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_roughness_1 : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_packed_nr_1 = true;
uniform float roughness_1_mul : hint_range(0.0, 2.0) = 1.0;
group_uniforms;

group_uniforms Blend;

uniform sampler2D tex_blend_mask : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_packed_blend_alpha = false;
uniform bool invert_blend = false;
uniform float blend_v0 : hint_range(-10.0, 10.0, 0.01) = -0.5;
uniform float blend_v1 : hint_range(-10.0, 10.0, 0.01) = 0.0;
uniform float blend_softness : hint_range(0.001, 1.0) = 0.5;
uniform float blend_sharpness : hint_range(0.5, 16.0) = 4.0;

uniform bool use_color_alpha = false;
uniform float texture_alpha_0 : hint_range(0.0, 1.0) = 1.0;
uniform float texture_alpha_1 : hint_range(0.0, 1.0) = 0.0;
group_uniforms;

group_uniforms Detail;
uniform bool use_detail = false;
uniform sampler2D tex_detail : source_color, filter_linear_mipmap, repeat_enable;
uniform float detail_blend_factor : hint_range(0.0, 1.0) = 0.2;
uniform float detail_uv_scale = 4.0;
group_uniforms;

group_uniforms Tint;
uniform vec4 global_tint_0 : source_color = vec4(1.0);
uniform vec4 global_tint_1 : source_color = vec4(1.0);
uniform bool use_vertex_color_tint = true;
uniform vec4 model_tint : source_color = vec4(1.0);
uniform float model_tint_amount : hint_range(0.0, 1.0) = 1.0;

uniform sampler2D tex_tint_mask : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_tint_mask = false;
group_uniforms;

group_uniforms Emission;

uniform sampler2D tex_selfillum : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform vec4 selfillum_tint : source_color = vec4(1.0);
uniform float emission_strength : hint_range(0.0, 8.0) = 0.0;
group_uniforms;

group_uniforms Material;
uniform sampler2D tex_metalness : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float specular : hint_range(0.0, 1.0) = 0.5;
group_uniforms;

group_uniforms Alpha;
uniform bool use_alpha_test = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
group_uniforms;

group_uniforms Debug;

uniform int debug_view : hint_enum("Off", "Vertex Color", "Tint Mask", "Blend Factor") = 0;
group_uniforms;

group_uniforms UV;
uniform vec2 uv_scale = vec2(1.0, 1.0);
uniform vec2 uv_offset = vec2(0.0, 0.0);
group_uniforms;

float sigmoid(float x, float exponent) {
    float a = pow(x, exponent);
    float b = pow(1.0 - x, exponent);
    return a / (a + b);
}

float blend_modulate(float mask, float v0, float v1, float softness) {
    float from_min = min(v1 + softness, 1.0);
    float from_max = max(v0 - softness, 0.0);
    return smoothstep(from_min, from_max, mask);
}

vec3 apply_detail(vec3 color, vec2 uv) {
    if (!use_detail) return color;
    vec3 detail = texture(tex_detail, uv * detail_uv_scale).rgb;
    return mix(color, color * detail, detail_blend_factor);
}

vec3 apply_tint(vec3 original, vec3 global_tint, vec3 vertex_color, vec3 tint_mask) {
    vec3 mtint = use_vertex_color_tint ? vertex_color : model_tint.rgb;
    vec3 global_factor = mix(vec3(1.0), global_tint, tint_mask.g);
    vec3 model_factor  = mix(vec3(1.0), mtint, tint_mask.r);
    vec3 combined = global_factor * model_factor;
    vec3 tinted = mix(original, original * combined, tint_mask.r);
    return mix(original, tinted, model_tint_amount);
}

void fragment() {
    vec2 uv = UV * uv_scale + uv_offset;

    vec4 c0 = texture(tex_color_0, uv);
    vec4 nr0 = texture(tex_normal_0, uv);
    vec3 n0 = nr0.rgb;
    float r0 = (use_packed_nr_0 ? nr0.a : texture(tex_roughness_0, uv).r) * roughness_0_mul;

    vec4 c1 = texture(tex_color_1, uv);
    vec4 nr1 = texture(tex_normal_1, uv);
    float r1 = (use_packed_nr_1 ? nr1.a : texture(tex_roughness_1, uv).r) * roughness_1_mul;

    vec4 blend_sample = texture(tex_blend_mask, uv);
    float mask = blend_sample.g;

    float bm = blend_modulate(mask, blend_v0, blend_v1, blend_softness);

    float blend = sigmoid(bm, blend_sharpness);

    if (invert_blend) blend = 1.0 - blend;

    float tex_alpha_0 = use_color_alpha ? c0.a : texture_alpha_0;
    float tex_alpha_1 = use_color_alpha ? c1.a : texture_alpha_1;
    if (use_color_alpha) {

        float w0 = (1.0 - blend) * tex_alpha_0;
        float w1 = blend * tex_alpha_1;
        float wsum = w0 + w1;
        blend = wsum > 0.0001 ? (w1 / wsum) : 0.0;
    } else {

        blend *= mix(tex_alpha_0, tex_alpha_1, 0.5) * 2.0;
        blend = clamp(blend, 0.0, 1.0);
    }

    vec3 tint_mask = use_tint_mask ? texture(tex_tint_mask, uv).rgb : vec3(1.0);

    vec3 col0 = apply_tint(c0.rgb, global_tint_0.rgb, COLOR.rgb, tint_mask);
    vec3 col1 = apply_tint(c1.rgb, global_tint_1.rgb, COLOR.rgb, tint_mask);

    vec3 color = mix(col0, col1, blend);

    vec3 normal_tex = n0;
    float roughness_val = mix(r0, r1, blend);

    vec3 selfillum = color * selfillum_tint.rgb * texture(tex_selfillum, uv).rgb;

    color = apply_detail(color, uv);

    if (flip_normal_y) normal_tex.g = 1.0 - normal_tex.g;

    float metal_tex = texture(tex_metalness, uv).r;
    float metal_final = metal_tex * metallic;

    float alpha_test_val = use_packed_blend_alpha ? blend_sample.a : c0.a;
    if (use_alpha_test && alpha_test_val < alpha_cutoff) discard;

    ALBEDO = color;
    NORMAL_MAP = normal_tex;
    ROUGHNESS = clamp(roughness_val, 0.04, 1.0);
    METALLIC = metal_final;
    SPECULAR = specular;
    EMISSION = selfillum * emission_strength;

    if (debug_view == 1) {
        ALBEDO = COLOR.rgb;
        EMISSION = COLOR.rgb;
    } else if (debug_view == 2) {
        ALBEDO = tint_mask;
        EMISSION = tint_mask;
    } else if (debug_view == 3) {
        ALBEDO = vec3(blend);
        EMISSION = vec3(blend);
    }
}
