// Full-screen cloud-shadow overlay. Projects each visible surface point along
// the sun ray onto a cloud plane and samples noise there - same math as
// sky.gdshader. Self-contained: no script needed (sun direction is static).
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, fog_disabled;

// Direction the sunlight travels. Static - matches the map's DirectionalLight3D.
uniform vec3 sun_travel_dir = vec3(0.287, -0.819, -0.497);
uniform float cloud_height = 20.0;
uniform vec2 noise_tiling = vec2(1.0);
uniform vec2 wind_speed = vec2(0.1, 0.1);

uniform sampler2D cloud_noise : repeat_enable, filter_linear;
uniform float cloud_sag : hint_range(0.5, 4.0) = 1.5;
uniform float coverage : hint_range(0.0, 1.0) = 0.6;
uniform float softness : hint_range(0.01, 0.6) = 0.3;
uniform float shadow_strength : hint_range(0.0, 1.0) = 0.55;
uniform vec3 shadow_tint : source_color = vec3(0.0);
uniform float surface_falloff : hint_range(0.05, 1.0) = 0.4;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform sampler2D normal_roughness_texture : hint_normal_roughness_texture, filter_nearest;
// Screen-Texture: Sun-Lit-Proxy. Pixel die schon dunkel sind (Eigenschatten von Häusern)
// kriegen keinen Cloud-Shadow drauf — verhindert "doppelt abdunkeln". Surface-normal-Check
// alleine reicht nicht weil flacher Boden im Häuser-Schatten dieselbe Normal hat wie sonniger.
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform float shadow_brightness_floor : hint_range(0.0, 0.5) = 0.08;   // unterhalb keine cloud
uniform float shadow_brightness_full : hint_range(0.05, 1.0) = 0.35;   // oberhalb voll cloud

// Aktive Rauch-Wolken: echte 3D-Dichte-Textur + Welt-AABB. cloud_shadows sampelt denselben
// Sichtstrahl durch die Dichte und unterdrueckt den Schatten EXAKT dort, wo Rauch ist.
uniform sampler3D smoke_tex_0 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_1 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_2 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_3 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_4 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_5 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_6 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_7 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_8 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_9 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_10 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_11 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_12 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_13 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_14 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_15 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_16 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_17 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_18 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_19 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_20 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_21 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_22 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_23 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_24 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_25 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_26 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_27 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_28 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_29 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_30 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_31 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_32 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_33 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_34 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_35 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_36 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_37 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_38 : repeat_disable, hint_default_black, filter_linear;
uniform sampler3D smoke_tex_39 : repeat_disable, hint_default_black, filter_linear;
uniform vec3 smoke_min_0; uniform vec3 smoke_size_0 = vec3(1.0);
uniform vec3 smoke_min_1; uniform vec3 smoke_size_1 = vec3(1.0);
uniform vec3 smoke_min_2; uniform vec3 smoke_size_2 = vec3(1.0);
uniform vec3 smoke_min_3; uniform vec3 smoke_size_3 = vec3(1.0);
uniform vec3 smoke_min_4; uniform vec3 smoke_size_4 = vec3(1.0);
uniform vec3 smoke_min_5; uniform vec3 smoke_size_5 = vec3(1.0);
uniform vec3 smoke_min_6; uniform vec3 smoke_size_6 = vec3(1.0);
uniform vec3 smoke_min_7; uniform vec3 smoke_size_7 = vec3(1.0);
uniform vec3 smoke_min_8; uniform vec3 smoke_size_8 = vec3(1.0);
uniform vec3 smoke_min_9; uniform vec3 smoke_size_9 = vec3(1.0);
uniform vec3 smoke_min_10; uniform vec3 smoke_size_10 = vec3(1.0);
uniform vec3 smoke_min_11; uniform vec3 smoke_size_11 = vec3(1.0);
uniform vec3 smoke_min_12; uniform vec3 smoke_size_12 = vec3(1.0);
uniform vec3 smoke_min_13; uniform vec3 smoke_size_13 = vec3(1.0);
uniform vec3 smoke_min_14; uniform vec3 smoke_size_14 = vec3(1.0);
uniform vec3 smoke_min_15; uniform vec3 smoke_size_15 = vec3(1.0);
uniform vec3 smoke_min_16; uniform vec3 smoke_size_16 = vec3(1.0);
uniform vec3 smoke_min_17; uniform vec3 smoke_size_17 = vec3(1.0);
uniform vec3 smoke_min_18; uniform vec3 smoke_size_18 = vec3(1.0);
uniform vec3 smoke_min_19; uniform vec3 smoke_size_19 = vec3(1.0);
uniform vec3 smoke_min_20; uniform vec3 smoke_size_20 = vec3(1.0);
uniform vec3 smoke_min_21; uniform vec3 smoke_size_21 = vec3(1.0);
uniform vec3 smoke_min_22; uniform vec3 smoke_size_22 = vec3(1.0);
uniform vec3 smoke_min_23; uniform vec3 smoke_size_23 = vec3(1.0);
uniform vec3 smoke_min_24; uniform vec3 smoke_size_24 = vec3(1.0);
uniform vec3 smoke_min_25; uniform vec3 smoke_size_25 = vec3(1.0);
uniform vec3 smoke_min_26; uniform vec3 smoke_size_26 = vec3(1.0);
uniform vec3 smoke_min_27; uniform vec3 smoke_size_27 = vec3(1.0);
uniform vec3 smoke_min_28; uniform vec3 smoke_size_28 = vec3(1.0);
uniform vec3 smoke_min_29; uniform vec3 smoke_size_29 = vec3(1.0);
uniform vec3 smoke_min_30; uniform vec3 smoke_size_30 = vec3(1.0);
uniform vec3 smoke_min_31; uniform vec3 smoke_size_31 = vec3(1.0);
uniform vec3 smoke_min_32; uniform vec3 smoke_size_32 = vec3(1.0);
uniform vec3 smoke_min_33; uniform vec3 smoke_size_33 = vec3(1.0);
uniform vec3 smoke_min_34; uniform vec3 smoke_size_34 = vec3(1.0);
uniform vec3 smoke_min_35; uniform vec3 smoke_size_35 = vec3(1.0);
uniform vec3 smoke_min_36; uniform vec3 smoke_size_36 = vec3(1.0);
uniform vec3 smoke_min_37; uniform vec3 smoke_size_37 = vec3(1.0);
uniform vec3 smoke_min_38; uniform vec3 smoke_size_38 = vec3(1.0);
uniform vec3 smoke_min_39; uniform vec3 smoke_size_39 = vec3(1.0);
uniform float smoke_density_mul = 60.0;
uniform int smoke_count = 0;

// Distance-Falloff: ab max_distance Metern Kamera→Pixel beginnt der Shadow auszublenden, ab
// max_distance + falloff_range ist er 0. Spart Pixel-Shader-Cost auf weit entfernten Flächen.
// Settings.CloudShadowDistance steuert max_distance live.
uniform float max_distance : hint_range(20.0, 500.0) = 150.0;
uniform float falloff_range : hint_range(5.0, 100.0) = 40.0;

// Rauch-Deckkraft (0..1) entlang Kamera->Flaeche — gleiche Beer-Lambert-Integration wie der
// Fog-Renderer: integriert die ECHTE Dichte, geclippt auf die Smoke-AABB. Wo der Rauch opak
// ist, kommt 1 raus → Wolkenschatten dort voll unterdrueckt.
float smoke_opacity(sampler3D tex, vec3 mn, vec3 sz, vec3 ro, vec3 seg) {
	vec3 invd = 1.0 / seg;
	vec3 ta = (mn - ro) * invd;
	vec3 tb = (mn + sz - ro) * invd;
	float tn = max(max(min(ta.x, tb.x), min(ta.y, tb.y)), max(min(ta.z, tb.z), 0.0));
	float tf = min(min(max(ta.x, tb.x), max(ta.y, tb.y)), min(max(ta.z, tb.z), 1.0));
	if (tf <= tn) return 0.0;
	float od = 0.0;
	for (int s = 0; s < 10; s++) {
		vec3 uvw = (ro + seg * mix(tn, tf, (float(s) + 0.5) * 0.1) - mn) / sz;
		od += texture(tex, uvw).r;
	}
	od *= (tf - tn) * length(seg) * 0.1;          // Sample-Summe -> Dichte·Meter
	return 1.0 - exp(-od * smoke_density_mul);
}

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	float depth = texture(depth_texture, SCREEN_UV).r;
	float cloud = 0.0;
	if (depth > 0.0) {
		// Reconstruct world position from depth.
		vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, depth);
		vec4 view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
		view.xyz /= view.w;
		vec3 world = (INV_VIEW_MATRIX * vec4(view.xyz, 1.0)).xyz;

		// Distance-Falloff: ferne Pixel skippen den teuren Shader-Body (nur dist_fade > 0 macht Arbeit).
		// Godot fragment() erlaubt kein early-return → guard via outer if-Block.
		vec3 cam_pos = INV_VIEW_MATRIX[3].xyz;
		float dist_to_cam = length(world - cam_pos);
		float dist_fade = 1.0 - smoothstep(max_distance, max_distance + falloff_range, dist_to_cam);
		if (dist_fade > 0.0) {

		// Project up to the cloud layer along the sun ray, sample noise.
		vec3 dir = normalize(sun_travel_dir);
		float dy = min(dir.y, -0.05);
		float t = (world.y - cloud_height) / dy;
		vec2 cloud_xz = world.xz - dir.xz * t;
		vec2 uv = (cloud_xz / cloud_height) * 0.2 * noise_tiling + wind_speed * TIME * 0.1;

		float n = pow(clamp(texture(cloud_noise, uv).r, 0.0, 1.0), cloud_sag);
		cloud = smoothstep(1.0 - coverage - softness, 1.0 - coverage + softness, n);

		// Weight by sun-facing orientation IF the normal buffer is usable.
		// Echt-physikalisch: Cloud blockiert nur SONNEN-Strahlen — Bereiche die der Sonne
		// abgewandt sind sind eh schon im Eigenschatten, da bringt eine Wolke visuell nichts.
		// Vorher nur world_normal.y gecheckt → flache Wand-Oberseiten im Häuser-Schatten
		// wurden doppelt abgedunkelt. dot(normal, -sun_dir) macht das physikalisch korrekt.
		// Wenn Normal-Buffer nicht usable, Shadow überall anwenden damit er sichtbar bleibt.
		vec3 nv = texture(normal_roughness_texture, SCREEN_UV).xyz * 2.0 - 1.0;
		float nlen = length(nv);
		if (nlen > 0.5 && nlen < 1.7) {
			vec3 world_normal = normalize((INV_VIEW_MATRIX * vec4(nv, 0.0)).xyz);
			float sun_facing = max(0.0, dot(world_normal, -normalize(sun_travel_dir)));
			cloud *= smoothstep(0.0, surface_falloff, sun_facing);
		}

		// Sun-Lit-Proxy via Screen-Brightness: schatten-Pixel sind eh dunkel im Final-Color,
		// für die hat ein zusätzlicher Cloud-Schatten visuell keinen Sinn (verstärkt nur den
		// Eindruck des "doppelten Abdunkelns"). Luminance < floor → Cloud weg, > full → voll.
		float screen_lum = dot(texture(screen_tex, SCREEN_UV).rgb, vec3(0.299, 0.587, 0.114));
		cloud *= smoothstep(shadow_brightness_floor, shadow_brightness_full, screen_lum);

		// Rauch exakt ausmaskieren: Deckkraft entlang Kamera->Flaeche durch die echte
		// Dichte-Textur jeder Wolke. Wo Rauch davor liegt, Wolkenschatten unterdruecken.
		// Gesamten 40er-Block in einen einzigen Branch wickeln — bei smoke_count=0 (Standard)
		// skippt die GPU alles, statt 40× zu predikate'n.
		vec3 cam = cam_pos;
		vec3 seg = world - cam;
		float smoke = 0.0;
		if (smoke_count > 0) {
		smoke = max(smoke, smoke_opacity(smoke_tex_0, smoke_min_0, smoke_size_0, cam, seg));
		if (smoke_count > 1) smoke = max(smoke, smoke_opacity(smoke_tex_1, smoke_min_1, smoke_size_1, cam, seg));
		if (smoke_count > 2) smoke = max(smoke, smoke_opacity(smoke_tex_2, smoke_min_2, smoke_size_2, cam, seg));
		if (smoke_count > 3) smoke = max(smoke, smoke_opacity(smoke_tex_3, smoke_min_3, smoke_size_3, cam, seg));
		if (smoke_count > 4) smoke = max(smoke, smoke_opacity(smoke_tex_4, smoke_min_4, smoke_size_4, cam, seg));
		if (smoke_count > 5) smoke = max(smoke, smoke_opacity(smoke_tex_5, smoke_min_5, smoke_size_5, cam, seg));
		if (smoke_count > 6) smoke = max(smoke, smoke_opacity(smoke_tex_6, smoke_min_6, smoke_size_6, cam, seg));
		if (smoke_count > 7) smoke = max(smoke, smoke_opacity(smoke_tex_7, smoke_min_7, smoke_size_7, cam, seg));
		if (smoke_count > 8) smoke = max(smoke, smoke_opacity(smoke_tex_8, smoke_min_8, smoke_size_8, cam, seg));
		if (smoke_count > 9) smoke = max(smoke, smoke_opacity(smoke_tex_9, smoke_min_9, smoke_size_9, cam, seg));
		if (smoke_count > 10) smoke = max(smoke, smoke_opacity(smoke_tex_10, smoke_min_10, smoke_size_10, cam, seg));
		if (smoke_count > 11) smoke = max(smoke, smoke_opacity(smoke_tex_11, smoke_min_11, smoke_size_11, cam, seg));
		if (smoke_count > 12) smoke = max(smoke, smoke_opacity(smoke_tex_12, smoke_min_12, smoke_size_12, cam, seg));
		if (smoke_count > 13) smoke = max(smoke, smoke_opacity(smoke_tex_13, smoke_min_13, smoke_size_13, cam, seg));
		if (smoke_count > 14) smoke = max(smoke, smoke_opacity(smoke_tex_14, smoke_min_14, smoke_size_14, cam, seg));
		if (smoke_count > 15) smoke = max(smoke, smoke_opacity(smoke_tex_15, smoke_min_15, smoke_size_15, cam, seg));
		if (smoke_count > 16) smoke = max(smoke, smoke_opacity(smoke_tex_16, smoke_min_16, smoke_size_16, cam, seg));
		if (smoke_count > 17) smoke = max(smoke, smoke_opacity(smoke_tex_17, smoke_min_17, smoke_size_17, cam, seg));
		if (smoke_count > 18) smoke = max(smoke, smoke_opacity(smoke_tex_18, smoke_min_18, smoke_size_18, cam, seg));
		if (smoke_count > 19) smoke = max(smoke, smoke_opacity(smoke_tex_19, smoke_min_19, smoke_size_19, cam, seg));
		if (smoke_count > 20) smoke = max(smoke, smoke_opacity(smoke_tex_20, smoke_min_20, smoke_size_20, cam, seg));
		if (smoke_count > 21) smoke = max(smoke, smoke_opacity(smoke_tex_21, smoke_min_21, smoke_size_21, cam, seg));
		if (smoke_count > 22) smoke = max(smoke, smoke_opacity(smoke_tex_22, smoke_min_22, smoke_size_22, cam, seg));
		if (smoke_count > 23) smoke = max(smoke, smoke_opacity(smoke_tex_23, smoke_min_23, smoke_size_23, cam, seg));
		if (smoke_count > 24) smoke = max(smoke, smoke_opacity(smoke_tex_24, smoke_min_24, smoke_size_24, cam, seg));
		if (smoke_count > 25) smoke = max(smoke, smoke_opacity(smoke_tex_25, smoke_min_25, smoke_size_25, cam, seg));
		if (smoke_count > 26) smoke = max(smoke, smoke_opacity(smoke_tex_26, smoke_min_26, smoke_size_26, cam, seg));
		if (smoke_count > 27) smoke = max(smoke, smoke_opacity(smoke_tex_27, smoke_min_27, smoke_size_27, cam, seg));
		if (smoke_count > 28) smoke = max(smoke, smoke_opacity(smoke_tex_28, smoke_min_28, smoke_size_28, cam, seg));
		if (smoke_count > 29) smoke = max(smoke, smoke_opacity(smoke_tex_29, smoke_min_29, smoke_size_29, cam, seg));
		if (smoke_count > 30) smoke = max(smoke, smoke_opacity(smoke_tex_30, smoke_min_30, smoke_size_30, cam, seg));
		if (smoke_count > 31) smoke = max(smoke, smoke_opacity(smoke_tex_31, smoke_min_31, smoke_size_31, cam, seg));
		if (smoke_count > 32) smoke = max(smoke, smoke_opacity(smoke_tex_32, smoke_min_32, smoke_size_32, cam, seg));
		if (smoke_count > 33) smoke = max(smoke, smoke_opacity(smoke_tex_33, smoke_min_33, smoke_size_33, cam, seg));
		if (smoke_count > 34) smoke = max(smoke, smoke_opacity(smoke_tex_34, smoke_min_34, smoke_size_34, cam, seg));
		if (smoke_count > 35) smoke = max(smoke, smoke_opacity(smoke_tex_35, smoke_min_35, smoke_size_35, cam, seg));
		if (smoke_count > 36) smoke = max(smoke, smoke_opacity(smoke_tex_36, smoke_min_36, smoke_size_36, cam, seg));
		if (smoke_count > 37) smoke = max(smoke, smoke_opacity(smoke_tex_37, smoke_min_37, smoke_size_37, cam, seg));
		if (smoke_count > 38) smoke = max(smoke, smoke_opacity(smoke_tex_38, smoke_min_38, smoke_size_38, cam, seg));
		if (smoke_count > 39) smoke = max(smoke, smoke_opacity(smoke_tex_39, smoke_min_39, smoke_size_39, cam, seg));
		}   // end if (smoke_count > 0) block
		cloud *= 1.0 - smoke;
		cloud *= dist_fade;

		}   // end if (dist_fade > 0.0) block — entfernt Pixel-Shader-Cost für ferne Pixel
	}
	ALBEDO = shadow_tint;
	ALPHA = shadow_strength * cloud;
}
