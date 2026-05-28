// CS:GO/CS2 Source 2 csgo_complex.vfx — SINGLE-LAYER Material — Godot 4 Port.
// EIN Material-Layer (kein BlendModulate). Pipeline aus dem Blender-Subgraph:
//   TextureColor → Detail-Overlay → ApplyTint → BSDF
//   + Normal / Roughness / Metalness / SelfIllum.
//
// Für 2-Layer-Blend-Materials → csgo_complex.gdshader (das ist die lightmappedgeneric-Variante).
shader_type spatial;
render_mode cull_back, depth_draw_opaque, specular_schlick_ggx;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  COLOR — Albedo                                                   ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Color;
uniform sampler2D tex_color : source_color, filter_linear_mipmap, repeat_enable;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  SURFACE — Normal / Roughness / Metalness                         ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Surface;
uniform sampler2D tex_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool use_packed_nr = true;        // Roughness aus tex_normal.a (Source-2-Pack)
uniform bool flip_normal_y = true;         // Source-2 DirectX-Normalmap → Godot OpenGL
uniform sampler2D tex_roughness : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float roughness_mul : hint_range(0.0, 2.0) = 1.0;
uniform sampler2D tex_metalness : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float specular : hint_range(0.0, 1.0) = 0.5;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  DETAIL — Overlay-Texture (F_DETAIL_TEXTURE + DetailMode)          ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Detail;
uniform bool use_detail = false;
uniform sampler2D tex_detail : source_color, filter_linear_mipmap, repeat_enable;
// Blender: g_tDetailMask ist als sRGB markiert → source_color (sonst dmask zu hell).
uniform sampler2D tex_detail_mask : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform int detail_mode : hint_enum("Off", "Multiply", "Overlay") = 1;
uniform float detail_blend_factor : hint_range(0.0, 1.0) = 0.2;  // g_flDetailBlendFactor
// Detail-Textur hat ihre EIGENE Kachelung (Source-2: separater UVTransform-Node) —
// unabhängig von der Color/Normal/Roughness-UV. vec2 → nicht-uniformes Tiling möglich.
uniform vec2 detail_uv_scale = vec2(4.0);
uniform vec2 detail_uv_offset = vec2(0.0);
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  TINT — ApplyTint (Global / Model / VertexColor, via TintMask)    ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Tint;
uniform sampler2D tex_tint_mask : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec4 global_tint : source_color = vec4(1.0);          // g_vColorTint
uniform vec4 model_tint : source_color = vec4(1.0);           // m_vColorTint (falls kein VertexColor)
uniform bool use_vertex_color_tint = true;                    // ModelTint aus Vertex-Color "TINT"
uniform float model_tint_amount : hint_range(0.0, 1.0) = 1.0; // g_flModelTintAmount
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  DEBUG — Diagnose-Views (0 = aus, normaler Render)                ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Debug;
// 1 = Vertex-Color (COLOR.rgb) — zeigt ob das Mesh überhaupt Vertex-Farben trägt.
// 2 = TintMask — zeigt ob/wo die Maske greift (weiss = getintet, schwarz = nicht).
uniform int debug_view : hint_enum("Off", "Vertex Color", "Tint Mask") = 0;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  EMISSION — Self-Illumination                                     ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Emission;
// Source-2 csgo_complex.vfx (Mix.007 → Mix.008): das ALBEDO leuchtet selbst.
// Emission = Albedo * selfillum_tint * tex_selfillum_mask * emission_strength.
// hint_default_black → ohne Maske leuchtet nichts (Source-2-Default).
uniform sampler2D tex_selfillum_mask : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform vec4 selfillum_tint : source_color = vec4(1.0);
uniform float emission_strength : hint_range(0.0, 8.0) = 0.0;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  ALPHA                                                            ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Alpha;
uniform bool use_alpha_test = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  UV — Source-2 UVTransform (Scale / Offset / Rotation / Center)   ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms UV;
uniform vec2 uv_scale = vec2(1.0, 1.0);                       // g_vTexCoordScale
uniform vec2 uv_offset = vec2(0.0, 0.0);                      // g_vTexCoordOffset
uniform float uv_rotation : hint_range(-180.0, 180.0) = 0.0;  // g_flTexCoordRotation (Grad)
uniform vec2 uv_center = vec2(0.5, 0.5);                      // g_vTexCoordCenter
group_uniforms;


// Source-2 Detail-Node — EXAKT aus dem Blender-Node-Graph (Detail-Group):
//   DetailMode 0 = kein Detail, 1 = Multiply, 2 = Overlay.
//   Der Graph kennt nur diese drei Modi — KEIN Mod2X.
vec3 detail_blend(vec3 base, vec3 detail, int mode) {
    if (mode == 1) return base * detail;          // Multiply
    if (mode == 2) {                              // Overlay
        vec3 lo = 2.0 * base * detail;
        vec3 hi = 1.0 - 2.0 * (1.0 - base) * (1.0 - detail);
        return mix(lo, hi, step(vec3(0.5), base));
    }
    return base;                                  // 0 = kein Detail
}

void fragment() {
    // === Source-2 UVTransform: um Center rotieren + skalieren + offset ===
    vec2 uv = UV - uv_center;
    float rot = radians(uv_rotation);
    float cs = cos(rot);
    float sn = sin(rot);
    uv = vec2(uv.x * cs - uv.y * sn, uv.x * sn + uv.y * cs);
    uv = uv * uv_scale + uv_center + uv_offset;

    // === Detail-UV: eigener Transform, von der ROHEN UV abgeleitet (nicht von der
    //     Color-UV) — entspricht dem separaten UVTransform-Node im Source-2-Graph. ===
    vec2 detail_uv = UV * detail_uv_scale + detail_uv_offset;

    // === TextureColor ===
    vec4 col = texture(tex_color, uv);
    vec3 color = col.rgb;

    // === Detail-Overlay (F_DETAIL_TEXTURE) ===
    if (use_detail) {
        vec3 detail = texture(tex_detail, detail_uv).rgb;
        float dmask = texture(tex_detail_mask, uv).r;   // DetailMask folgt der Basis-UV
        vec3 detailed = detail_blend(color, detail, detail_mode);
        color = mix(color, detailed, detail_blend_factor * dmask);
    }

    // === ApplyTint + Mix.010 — Source-2-Node-Graph (ApplyTint-Group + csgo_complex.vfx): ===
    //   global_factor = mix(1, GlobalTint, TintMask.GREEN)   [Mix.012]
    //   model_factor  = mix(1, ModelTint,  TintMask.RED)     [Mix.011]
    //   combined      = global_factor * model_factor         [Mix.013, Factor=1]
    //   tinted        = mix(Original, Original * combined, TintMask.RED)  [Mix.009]
    //   color         = mix(Original, tinted, g_flModelTintAmount)        [Mix.010]
    // → GlobalTint maskiert über GRÜN, ModelTint + Gate über ROT. Blau ungenutzt.
    // g_flModelTintAmount ist ein Master-Fade über den GESAMTEN Tint (Global UND Model),
    // NICHT nur den ModelTint — exakt wie Mix.010 im csgo_complex.vfx-Graph.
    // tex_tint_mask ist hint_default_white → ohne Maske r=g=1 (flächiger Tint).
    vec3 tint_mask = texture(tex_tint_mask, uv).rgb;
    vec3 mtint = use_vertex_color_tint ? COLOR.rgb : model_tint.rgb;
    vec3 global_factor = mix(vec3(1.0), global_tint.rgb, tint_mask.g);
    vec3 model_factor  = mix(vec3(1.0), mtint, tint_mask.r);
    vec3 tint_combined = global_factor * model_factor;
    vec3 tinted = mix(color, color * tint_combined, tint_mask.r);
    color = mix(color, tinted, model_tint_amount);

    // === Normal (Source-2 DirectX → Godot OpenGL Flip) ===
    vec4 nr = texture(tex_normal, uv);
    vec3 normal_tex = nr.rgb;
    if (flip_normal_y) normal_tex.g = 1.0 - normal_tex.g;

    // === Roughness / Metalness ===
    float rough = (use_packed_nr ? nr.a : texture(tex_roughness, uv).r) * roughness_mul;
    float metal = texture(tex_metalness, uv).r * metallic;

    // === SelfIllum (Source-2 csgo_complex.vfx: Mix.007 → Mix.008) ===
    // Emission Color = TextureColor * SelfIllumTint * TextureSelfIllumMask.
    // Mix.007.A ist der Gruppen-Input TextureColor → die rohe Albedo VOR Detail/Tint.
    vec3 selfillum = col.rgb * selfillum_tint.rgb * texture(tex_selfillum_mask, uv).rgb;

    // === Alpha-Test (nur für Drahtzäune/Decals — sonst voll opak) ===
    // Color-Alpha NICHT als Transparenz nutzen — Source-2 packt da andere Daten rein.
    if (use_alpha_test && col.a < alpha_cutoff) discard;

    // === Output ===
    ALBEDO = color;
    NORMAL_MAP = normal_tex;
    ROUGHNESS = clamp(rough, 0.04, 1.0);
    METALLIC = metal;
    SPECULAR = specular;
    EMISSION = selfillum * emission_strength;

    // === Debug-View: überschreibt Output zur Diagnose (EMISSION = selbstleuchtend sichtbar) ===
    if (debug_view == 1) {          // Vertex-Color: blau = Vertex-Farben da, weiss = keine/weiss
        ALBEDO = COLOR.rgb;
        EMISSION = COLOR.rgb;
    } else if (debug_view == 2) {   // TintMask: weiss = Maske greift, schwarz = kein Tint hier
        ALBEDO = tint_mask;
        EMISSION = tint_mask;
    }
}
