// AAA-ADS-Vignette — dunkler Rand der beim Zielen einblendet. Verstärkt das "Tunnel-Vision"-
// Gefühl beim ADS subtil. CoD-Standard-Effekt.
//
// SETUP: ShaderMaterial mit diesem Shader an einem ColorRect (FullRect) auf einem CanvasLayer
// der ÜBER dem viewmodel_layer (= layer > 10) und ÜBER der Welt liegt. ads_blend wird vom
// LocalAnimation.cs jeden Frame gesetzt.
//
// Wirkt nur am BILDSCHIRM-Rand → Zentrum bleibt 100% transparent → beeinträchtigt das
// Iron-Sight nicht. Skaliert mit ads_blend für smooth Blend-In.

shader_type canvas_item;
render_mode unshaded;

uniform float ads_blend : hint_range(0.0, 1.0) = 0.0;
uniform float vignette_inner : hint_range(0.0, 1.0) = 0.30;    // Radius (UV) wo Vignette beginnt
uniform float vignette_outer : hint_range(0.0, 1.5) = 0.85;    // Radius wo Vignette voll dunkel
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.55; // max alpha am Rand bei vollem ADS
uniform vec3 vignette_color : source_color = vec3(0.0, 0.0, 0.0);

void fragment() {
	vec2 uv = UV;
	// Anisotrope Distanz: x zähle stärker als y, damit Vignette breite Aspect-Ratios mitmacht.
	vec2 centered = (uv - vec2(0.5)) * vec2(1.0, 0.85);
	float dist = length(centered) * 2.0;   // 0..~1.15 von Mitte zum Eck
	float v = smoothstep(vignette_inner, vignette_outer, dist);
	float alpha = v * vignette_strength * ads_blend;
	COLOR = vec4(vignette_color, alpha);
}
