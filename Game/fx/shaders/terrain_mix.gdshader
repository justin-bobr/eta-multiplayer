// CS:GO/CS2 Source 2 csgo_complex.vfx + csgo_lightmappedgeneric.vfx — Godot 4 Port.
// Multi-Layer PBR-Blend (2 Layer) mit BlendModulate-Mask (Sigmoid + Softness/Sharpness),
// Detail-Overlay, Global/Model-Tint, Self-Illumination.
//
// PACKED-TEXTURE-SUPPORT (Source-2-Convention):
//   • Normal+Roughness in einer PNG:  RGB = Normal, A = Roughness   → Toggle: use_packed_nr_0/1
//   • BlendMask + Layer-Alpha in einer PNG:  R = BlendMask, A = Alpha → Toggle: use_packed_blend_alpha
shader_type spatial;
render_mode cull_back, depth_draw_opaque, specular_schlick_ggx;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  LAYER 0 — Base                                                   ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Layer0;
uniform sampler2D tex_color_0 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_normal_0 : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_roughness_0 : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_packed_nr_0 = true;           // Roughness aus tex_normal_0.a lesen (Source-2-Pack)
uniform float roughness_0_mul : hint_range(0.0, 2.0) = 1.0;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  NORMALS — Konvention (Source 2 = DirectX, Godot = OpenGL)         ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Normals;
uniform bool flip_normal_y = true;     // Source-2-Normalmaps sind DirectX-Y-down → für Godot Green-Channel flippen
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  LAYER 1 — Blend-over                                             ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Layer1;
uniform sampler2D tex_color_1 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_normal_1 : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_roughness_1 : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_packed_nr_1 = true;           // Roughness aus tex_normal_1.a lesen
uniform float roughness_1_mul : hint_range(0.0, 2.0) = 1.0;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  BLEND — Source-2 BlendModulate (Mask + V0/V1 + Sigmoid)          ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Blend;
// WICHTIG: in Source 2 / Blender ist g_tBlendModulation als sRGB markiert — die Maske
// MUSS sRGB→linear dekodiert werden, sonst ist mask zu hell → es wird zu viel Layer 1
// reingeblendet (dunkle, streifige Wand statt sauberem Blend).
uniform sampler2D tex_blend_mask : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_packed_blend_alpha = false;    // tex_blend_mask.r = Blend, .a = Layer0-Alpha
uniform bool invert_blend = false;              // dreht Blend-Richtung um (Layer0 <-> Layer1) falls vertauscht
uniform float blend_v0 : hint_range(-10.0, 10.0, 0.01) = -0.5;
uniform float blend_v1 : hint_range(-10.0, 10.0, 0.01) = 0.0;
uniform float blend_softness : hint_range(0.001, 1.0) = 0.5;
uniform float blend_sharpness : hint_range(0.5, 16.0) = 4.0;
// use_color_alpha=true → TextureAlpha pro Layer aus dem Alpha-Kanal der Color-Textur
// (Blender: g_tColor.Alpha → TextureAlpha0). false → feste Uniform-Werte unten.
uniform bool use_color_alpha = false;
uniform float texture_alpha_0 : hint_range(0.0, 1.0) = 1.0;   // Fallback wenn use_color_alpha=false
uniform float texture_alpha_1 : hint_range(0.0, 1.0) = 0.0;   // Fallback wenn use_color_alpha=false
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  DETAIL — Overlay-Detail-Texture (F_DETAIL_TEXTURE)               ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Detail;
uniform bool use_detail = false;
uniform sampler2D tex_detail : source_color, filter_linear_mipmap, repeat_enable;
uniform float detail_blend_factor : hint_range(0.0, 1.0) = 0.2;
uniform float detail_uv_scale = 4.0;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  TINT — Source-2 Global/Model-Tint                                ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Tint;
uniform vec4 global_tint_0 : source_color = vec4(1.0);
uniform vec4 global_tint_1 : source_color = vec4(1.0);
uniform bool use_vertex_color_tint = true;     // wenn true: ModelTint kommt aus Vertex-Color (Source-2 "TINT"-Attribute), sonst aus model_tint Uniform
uniform vec4 model_tint : source_color = vec4(1.0);
uniform float model_tint_amount : hint_range(0.0, 1.0) = 1.0;
// Source-2 TextureTintMask: maskiert WO getintet wird (z.B. nur lackierte Stellen).
// use_tint_mask=false → mask=1.0 überall → flächiger Tint (altes Verhalten, abwärtskompatibel).
uniform sampler2D tex_tint_mask : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_tint_mask = false;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  EMISSION — Self-Illumination                                     ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Emission;
// Source-2 csgo_complex.vfx (Mix.007 → Mix.008): das ALBEDO leuchtet selbst.
// tex_selfillum ist die SelfIllumMask (TextureSelfIllumMask) — KEINE eigene
// Emissions-Textur. Emission = Albedo * selfillum_tint * Maske * emission_strength.
// hint_default_black → ohne Maske leuchtet nichts (Source-2-Default).
uniform sampler2D tex_selfillum : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform vec4 selfillum_tint : source_color = vec4(1.0);
uniform float emission_strength : hint_range(0.0, 8.0) = 0.0;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  MATERIAL — Metallic + Specular                                   ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Material;
uniform sampler2D tex_metalness : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float specular : hint_range(0.0, 1.0) = 0.5;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  ALPHA — Alpha-Test (Drahtzäune, Decals)                          ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Alpha;
uniform bool use_alpha_test = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  DEBUG — Diagnose-Views (0 = aus, normaler Render)                ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Debug;
// 1 = Vertex-Color (COLOR.rgb) — zeigt ob das Mesh überhaupt Vertex-Farben trägt.
// 2 = TintMask — zeigt ob/wo die Maske greift (weiss = getintet, schwarz = nicht).
// 3 = Blend-Faktor — zeigt die Layer-Blend-Maske (weiss = Layer1, schwarz = Layer0).
uniform int debug_view : hint_enum("Off", "Vertex Color", "Tint Mask", "Blend Factor") = 0;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  UV — Tiling                                                      ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms UV;
uniform vec2 uv_scale = vec2(1.0, 1.0);
uniform vec2 uv_offset = vec2(0.0, 0.0);
group_uniforms;


// Source 2 Sigmoid: symmetric contrast curve — x^k / (x^k + (1-x)^k)
// Subgraph: Subtract(1-x) → Power → Power → Add → Divide.
//   • k = 1.0  → linear
//   • k > 1.0  → S-Kurve, schärfer je höher
// Exponent = Sharpness DIREKT (Source-2-Trace), kein 42.9-Multiplikator.
float sigmoid(float x, float exponent) {
    float a = pow(x, exponent);
    float b = pow(1.0 - x, exponent);
    return a / (a + b);
}

// Source-2 BlendModulate-Node — EXAKT aus dem Node-Graph-Dump:
//   From Min = min(V1 + Softness, 1.0)   ← Minimum(Add(V1,Softness), 1)
//   From Max = max(V0 - Softness, 0.0)   ← Maximum(Subtract(V0,Softness), 0)
//   result   = Map Range "Smooth Step" (Value=mask, To 0..1) = smoothstep(FromMin, FromMax, mask)
// WICHTIG: From Min ist die V1-Grenze, From Max die V0-Grenze — im Graph genau so verdrahtet.
// Bei V1+S > V0-S ist das ein invertierter Smoothstep — gewollt. mask = GREEN-Channel.
float blend_modulate(float mask, float v0, float v1, float softness) {
    float from_min = min(v1 + softness, 1.0);
    float from_max = max(v0 - softness, 0.0);
    return smoothstep(from_min, from_max, mask);
}

// Source-2 Detail-Node: Detail-Map-Overlay. Der Detail-Graph nutzt PLAIN MULTIPLY (color*detail),
// KEIN Mod2X (×2). Läuft NACH dem Blend-Mix auf der geblendeten Farbe.
// (Hinweis: der volle Detail-Graph hat 3 Modi 0=aus/1=Multiply/2=Overlay + DetailMask —
//  hier vereinfacht auf Multiply. Für volle Modi: detail_mode/detail_mask-Uniforms nötig.)
vec3 apply_detail(vec3 color, vec2 uv) {
    if (!use_detail) return color;
    vec3 detail = texture(tex_detail, uv * detail_uv_scale).rgb;
    return mix(color, color * detail, detail_blend_factor);
}

// Source-2 ApplyTint-Node + Mix.010 — aus dem Node-Graph-Dump (ApplyTint-Group):
//   global_factor = mix(1, GlobalTint, TintMask.GREEN)   [Mix.012]
//   model_factor  = mix(1, ModelTint,  TintMask.RED)     [Mix.011]
//   combined      = global_factor * model_factor         [Mix.013, Factor=1]
//   tinted        = mix(Original, Original * combined, TintMask.RED)  [Mix.009]
//   result        = mix(Original, tinted, g_flModelTintAmount)        [Mix.010]
// → GlobalTint maskiert über GRÜN, ModelTint + Gate über ROT. Blau ungenutzt.
// g_flModelTintAmount ist ein Master-Fade über den GESAMTEN Tint (Global UND Model),
// als Lerp ungetintet↔getintet — exakt wie Mix.010 (csgo_complex.vfx), NICHT nur ModelTint.
// Läuft PRO LAYER (eigener GlobalTint); der Lerp ist linear → per-Layer == post-Blend.
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

    // === Sample Layer 0 ===
    vec4 c0 = texture(tex_color_0, uv);
    vec4 nr0 = texture(tex_normal_0, uv);
    vec3 n0 = nr0.rgb;
    float r0 = (use_packed_nr_0 ? nr0.a : texture(tex_roughness_0, uv).r) * roughness_0_mul;

    // === Sample Layer 1 ===
    // tex_normal_1 wird nur für die gepackte Roughness (nr1.a) gesampelt — die
    // Normale selbst kommt 1:1 wie im Blender-Graph ausschliesslich aus Layer 0.
    vec4 c1 = texture(tex_color_1, uv);
    vec4 nr1 = texture(tex_normal_1, uv);
    float r1 = (use_packed_nr_1 ? nr1.a : texture(tex_roughness_1, uv).r) * roughness_1_mul;

    // === BlendModulate (Source-2-replikat) → Sigmoid (Exponent = Sharpness direkt) ===
    vec4 blend_sample = texture(tex_blend_mask, uv);
    float mask = blend_sample.g;   // Source-2: GREEN-Channel ist die Blend-Mask
    // BlendModulate-Node: mapped mask via V0/V1 + Softness-Widening
    float bm = blend_modulate(mask, blend_v0, blend_v1, blend_softness);
    // Sigmoid: Base = BlendModulate-Output, Exponent = Sharpness direkt (Source-2-Trace)
    float blend = sigmoid(bm, blend_sharpness);
    // Optional: Blend-Richtung umdrehen (falls Layer0/Layer1 vertauscht wirken)
    if (invert_blend) blend = 1.0 - blend;

    // === Per-Layer Alpha-Masks dazumultiplizieren ===
    // use_color_alpha: TextureAlpha aus dem Color-Alpha-Kanal (Blender: g_tColor.Alpha → TextureAlpha).
    float tex_alpha_0 = use_color_alpha ? c0.a : texture_alpha_0;
    float tex_alpha_1 = use_color_alpha ? c1.a : texture_alpha_1;
    blend *= mix(tex_alpha_0, tex_alpha_1, 0.5) * 2.0;
    blend = clamp(blend, 0.0, 1.0);

    // === TintMask sampeln (Source-2 TextureTintMask) — RGB, ApplyTint nutzt R + G ===
    // use_tint_mask=false → vec3(1) → voller Tint überall (kein Maskeneffekt).
    vec3 tint_mask = use_tint_mask ? texture(tex_tint_mask, uv).rgb : vec3(1.0);

    // === ApplyTint PRO LAYER (Source-2: 2× ApplyTint vor dem Blend-Mix) ===
    // Jeder Layer wird mit seinem eigenen GlobalTint maskiert getintet, DANN geblendet.
    vec3 col0 = apply_tint(c0.rgb, global_tint_0.rgb, COLOR.rgb, tint_mask);
    vec3 col1 = apply_tint(c1.rgb, global_tint_1.rgb, COLOR.rgb, tint_mask);

    // === Mix Layer-Outputs ===
    vec3 color = mix(col0, col1, blend);
    // Normale 1:1 wie Blender: Group.002.TextureNormal hängt direkt an
    // [Group Input].TextureNormal0 — Layer 1 wird NICHT in die Normale geblendet
    // (der Mix.001-Node Normal0↔Normal1 ist im Blender-Graph unverdrahtet).
    vec3 normal_tex = n0;
    float roughness_val = mix(r0, r1, blend);

    // === Self-Illum (Source-2 csgo_complex.vfx: Mix.007 → Mix.008) ===
    // Emission Color = TextureColor * SelfIllumTint * TextureSelfIllumMask.
    // Mix.007.A ist der Gruppen-Input TextureColor → die geblendete Albedo VOR dem
    // Detail-Overlay. Daher hier mit `color` vor apply_detail() rechnen.
    vec3 selfillum = color * selfillum_tint.rgb * texture(tex_selfillum, uv).rgb;

    // === Detail-Overlay (csgo_complex.vfx Detail-Node — NACH dem Blend-Mix) ===
    color = apply_detail(color, uv);

    // Normal-Konvention: Source-2 (DirectX, Y-down) → Godot (OpenGL, Y-up)
    if (flip_normal_y) normal_tex.g = 1.0 - normal_tex.g;


    // === Metalness ===
    float metal_tex = texture(tex_metalness, uv).r;
    float metal_final = metal_tex * metallic;

    // === Alpha-Test (nur für Drahtzäune/Decals — sonst voll opak) ===
    // Color-Alpha NICHT als Transparenz nutzen — Source-2 packt da andere Daten rein.
    float alpha_test_val = use_packed_blend_alpha ? blend_sample.a : c0.a;
    if (use_alpha_test && alpha_test_val < alpha_cutoff) discard;

    // === Output ===
    ALBEDO = color;
    NORMAL_MAP = normal_tex;
    ROUGHNESS = clamp(roughness_val, 0.04, 1.0);
    METALLIC = metal_final;
    SPECULAR = specular;
    EMISSION = selfillum * emission_strength;

    // === Debug-View: überschreibt Output zur Diagnose (EMISSION = selbstleuchtend sichtbar) ===
    if (debug_view == 1) {          // Vertex-Color: zeigt ob das Mesh Vertex-Farben hat
        ALBEDO = COLOR.rgb;
        EMISSION = COLOR.rgb;
    } else if (debug_view == 2) {   // TintMask: weiss = Maske greift, schwarz = kein Tint hier
        ALBEDO = tint_mask;
        EMISSION = tint_mask;
    } else if (debug_view == 3) {   // Blend-Faktor: weiss = Layer1, schwarz = Layer0
        ALBEDO = vec3(blend);
        EMISSION = vec3(blend);
    }
}
