// CS:GO/CS2 Source 2 csgo_foliage.vfx — Godot 4 Port.
// Vegetation / Blattwerk: Albedo + Alpha-Cutout (Blattform) + Normal + gepackte Roughness.
// Doppelseitig (CS:GO F_RENDER_BACKFACES) — Blätter von beiden Seiten sichtbar.
//
// PACKED-NORMAL (Source-2-Convention):
//   Normal + Roughness in EINER Textur:  RGB = Normal, A = Roughness.
//   ⚠ WICHTIG: Die Normal-Textur darf in Godot NICHT als "Normal Map" importiert werden —
//   der Normal-Map-Import komprimiert zu RGTC/BC5 (nur 2 Kanäle) und WIRFT DEN ALPHA WEG.
//   Import-Tab → Mode auf "VRAM Uncompressed" oder "Lossless", "Normal Map" auf Disabled,
//   damit der Roughness-Alpha erhalten bleibt. (Alternativ: use_packed_nr=false + Rough-Textur.)
shader_type spatial;
// cull_disabled = doppelseitig. alpha_to_coverage = anti-aliased Cutout-Kanten via MSAA.
// ⚠ Wirkt nur mit aktivem MSAA: Projekteinstellungen → Rendering → Anti Aliasing →
//   MSAA 3D auf 2x/4x/8x stellen. Ohne MSAA bleibt es ein harter (aber sauberer) Scissor.
render_mode cull_disabled, depth_draw_opaque, alpha_to_coverage, specular_schlick_ggx;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  COLOR — Albedo + Blatt-Alpha                                     ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Color;
uniform sampler2D tex_color : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 color_tint : source_color = vec4(1.0);   // csgo_foliage.vfx "Tint" (Default weiss)
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  SURFACE — Normal / Roughness / Metalness                         ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Surface;
uniform sampler2D tex_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool use_packed_nr = true;          // Roughness aus tex_normal.a lesen (Source-2-Pack)
uniform bool flip_normal_y = true;          // Source-2 DirectX-Normalmap → Godot OpenGL (Y flippen)
uniform sampler2D tex_roughness : hint_default_white, filter_linear_mipmap, repeat_enable;  // nur falls use_packed_nr=false
uniform float roughness_mul : hint_range(0.0, 2.0) = 1.0;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float specular : hint_range(0.0, 1.0) = 0.5;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  ALPHA — Blatt-Cutout (Alpha-Scissor)                             ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Alpha;
// g_tColor.Alpha → csgo_foliage.vfx.Alpha → in Godot "Alpha Scissor" (Default-Threshold 0.5).
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  TRANSLUCENCY — Licht durch die Blätter (optional, Default aus)    ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms Translucency;
uniform bool use_translucency = false;       // weiches Durchscheinen im Gegenlicht
uniform vec4 translucency_tint : source_color = vec4(0.30, 0.45, 0.15, 1.0);  // typisches Blattgrün
uniform float translucency_strength : hint_range(0.0, 1.0) = 0.5;
group_uniforms;


// ╔═══════════════════════════════════════════════════════════════════╗
// ║  UV — Tiling                                                      ║
// ╚═══════════════════════════════════════════════════════════════════╝
group_uniforms UV;
uniform vec2 uv_scale = vec2(1.0, 1.0);
uniform vec2 uv_offset = vec2(0.0, 0.0);
group_uniforms;


void fragment() {
    vec2 uv = UV * uv_scale + uv_offset;

    // === Color + Blatt-Alpha (g_tColor) ===
    vec4 c = texture(tex_color, uv);

    // === Normal + gepackte Roughness (g_tNormal: RGB=Normal, A=Roughness) ===
    vec4 nr = texture(tex_normal, uv);
    vec3 normal_tex = nr.rgb;
    // Source-2-Normalmaps sind DirectX (Y-down) → Godot OpenGL (Y-up)
    if (flip_normal_y) normal_tex.g = 1.0 - normal_tex.g;
    float rough = (use_packed_nr ? nr.a : texture(tex_roughness, uv).r) * roughness_mul;

    // === Doppelseitig: Rückseiten-Normale spiegeln, sonst ist die Beleuchtung invertiert ===
    // NORMAL_MAP wird von Godot NACH fragment() auf NORMAL/TANGENT/BINORMAL angewandt.
    if (!FRONT_FACING) NORMAL = -NORMAL;

    // === Output ===
    ALBEDO = c.rgb * color_tint.rgb;
    NORMAL_MAP = normal_tex;
    ROUGHNESS = clamp(rough, 0.04, 1.0);
    METALLIC = metallic;
    SPECULAR = specular;

    // === Alpha-Cutout mit anti-aliased Kante (gegen "verpixelte"/vernetzte Blätter) ===
    // Hartes discard franst aus; gemipmaptes Alpha dünnt in der Distanz zu einem
    // Netz-Muster aus. Fix: die Alpha-Kante per Screenspace-Ableitung (fwidth) auf
    // ~1 Pixel Breite normieren → alpha_to_coverage macht daraus glatte MSAA-Coverage.
    // Nach dem Remap liegt die Kante fest bei 0.5 (egal welcher alpha_cutoff).
    ALPHA = clamp((c.a - alpha_cutoff) / max(fwidth(c.a), 0.0001) + 0.5, 0.0, 1.0);
    ALPHA_SCISSOR_THRESHOLD = 0.5;

    // === Translucency: Blätter im Gegenlicht leuchten weich durch ===
    if (use_translucency) {
        BACKLIGHT = translucency_tint.rgb * translucency_strength;
    }
}
