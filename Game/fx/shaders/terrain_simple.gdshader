shader_type spatial;
render_mode cull_back, depth_draw_opaque, specular_schlick_ggx;

group_uniforms Color;
uniform sampler2D tex_color : source_color, filter_linear_mipmap, repeat_enable;
group_uniforms;

group_uniforms Surface;
uniform sampler2D tex_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool use_packed_nr = true;
uniform bool flip_normal_y = true;
uniform sampler2D tex_roughness : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float roughness_mul : hint_range(0.0, 2.0) = 1.0;
uniform sampler2D tex_metalness : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float specular : hint_range(0.0, 1.0) = 0.5;
group_uniforms;

group_uniforms Detail;
uniform bool use_detail = false;
uniform sampler2D tex_detail : source_color, filter_linear_mipmap, repeat_enable;

uniform sampler2D tex_detail_mask : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform int detail_mode : hint_enum("Off", "Multiply", "Overlay") = 1;
uniform float detail_blend_factor : hint_range(0.0, 1.0) = 0.2;

uniform vec2 detail_uv_scale = vec2(4.0);
uniform vec2 detail_uv_offset = vec2(0.0);
group_uniforms;

group_uniforms Tint;
uniform sampler2D tex_tint_mask : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec4 global_tint : source_color = vec4(1.0);
uniform vec4 model_tint : source_color = vec4(1.0);
uniform bool use_vertex_color_tint = true;
uniform float model_tint_amount : hint_range(0.0, 1.0) = 1.0;
group_uniforms;

group_uniforms Debug;

uniform int debug_view : hint_enum("Off", "Vertex Color", "Tint Mask") = 0;
group_uniforms;

group_uniforms Emission;

uniform sampler2D tex_selfillum_mask : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform vec4 selfillum_tint : source_color = vec4(1.0);
uniform float emission_strength : hint_range(0.0, 8.0) = 0.0;
group_uniforms;

group_uniforms Alpha;
uniform bool use_alpha_test = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
group_uniforms;

group_uniforms UV;
uniform vec2 uv_scale = vec2(1.0, 1.0);
uniform vec2 uv_offset = vec2(0.0, 0.0);
uniform float uv_rotation : hint_range(-180.0, 180.0) = 0.0;
uniform vec2 uv_center = vec2(0.5, 0.5);
group_uniforms;

vec3 detail_blend(vec3 base, vec3 detail, int mode) {
    if (mode == 1) return base * detail;
    if (mode == 2) {
        vec3 lo = 2.0 * base * detail;
        vec3 hi = 1.0 - 2.0 * (1.0 - base) * (1.0 - detail);
        return mix(lo, hi, step(vec3(0.5), base));
    }
    return base;
}

void fragment() {

    vec2 uv = UV - uv_center;
    float rot = radians(uv_rotation);
    float cs = cos(rot);
    float sn = sin(rot);
    uv = vec2(uv.x * cs - uv.y * sn, uv.x * sn + uv.y * cs);
    uv = uv * uv_scale + uv_center + uv_offset;

    vec2 detail_uv = UV * detail_uv_scale + detail_uv_offset;

    vec4 col = texture(tex_color, uv);
    vec3 color = col.rgb;

    if (use_detail) {
        vec3 detail = texture(tex_detail, detail_uv).rgb;
        float dmask = texture(tex_detail_mask, uv).r;
        vec3 detailed = detail_blend(color, detail, detail_mode);
        color = mix(color, detailed, detail_blend_factor * dmask);
    }

    vec3 tint_mask = texture(tex_tint_mask, uv).rgb;
    vec3 mtint = use_vertex_color_tint ? COLOR.rgb : model_tint.rgb;
    vec3 global_factor = mix(vec3(1.0), global_tint.rgb, tint_mask.g);
    vec3 model_factor  = mix(vec3(1.0), mtint, tint_mask.r);
    vec3 tint_combined = global_factor * model_factor;
    vec3 tinted = mix(color, color * tint_combined, tint_mask.r);
    color = mix(color, tinted, model_tint_amount);

    vec4 nr = texture(tex_normal, uv);
    vec3 normal_tex = nr.rgb;
    if (flip_normal_y) normal_tex.g = 1.0 - normal_tex.g;

    float rough = (use_packed_nr ? nr.a : texture(tex_roughness, uv).r) * roughness_mul;
    float metal = texture(tex_metalness, uv).r * metallic;

    vec3 selfillum = col.rgb * selfillum_tint.rgb * texture(tex_selfillum_mask, uv).rgb;

    if (use_alpha_test && col.a < alpha_cutoff) discard;

    ALBEDO = color;
    NORMAL_MAP = normal_tex;
    ROUGHNESS = clamp(rough, 0.04, 1.0);
    METALLIC = metal;
    SPECULAR = specular;
    EMISSION = selfillum * emission_strength;

    if (debug_view == 1) {
        ALBEDO = COLOR.rgb;
        EMISSION = COLOR.rgb;
    } else if (debug_view == 2) {
        ALBEDO = tint_mask;
        EMISSION = tint_mask;
    }
}
