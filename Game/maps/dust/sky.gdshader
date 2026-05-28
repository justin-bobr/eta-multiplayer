shader_type sky;

group_uniforms Sky;
/**
 * Use directional light for sun color
*/
uniform bool use_directional_light = false;
uniform vec4 sun_color : source_color = vec4(1.0);
uniform float sun_energy = 15.0;
uniform vec4 cloud_color : source_color = vec4(0.296, 0.402, 0.521, 1.0);
/**
 * Determines how much light the clouds absorb
*/
uniform float cloud_density = 5.0;
/**
 * Depth of the parallax effect
*/
uniform float cloud_depth = 2.0;
/**
 * Higher values give the clouds a heavier appearance
*/
uniform float cloud_sag = 2.0;
uniform sampler2D noise_texture;
uniform vec2 noise_tiling = vec2(1.0);
uniform vec2 wind_speed = vec2(0.5);
group_uniforms;

group_uniforms Ground;
uniform vec4 ground_bottom_color : source_color = vec4(0.2, 0.169, 0.133, 1.0);
uniform float ground_curve : hint_range(0, 1) = 0.04;
group_uniforms;

uniform float exposure : hint_range(0, 128) = 1.0;

// https://www.scratchapixel.com/lessons/3d-basic-rendering/minimal-ray-tracer-rendering-simple-shapes/ray-plane-and-ray-disk-intersection.html
float ray_plane_intersection(vec3 origin, vec3 normal, vec3 ray_start, vec3 ray_dir) {
	return dot(origin - ray_start, normal) / dot(ray_dir, normal);
}

float sample_height(vec2 uv) {
	float height = texture(noise_texture, uv).r;
	return pow(height, cloud_sag);
}

float beers_law(float absorption, float dist) {
	return exp(-absorption * dist);
}

void sky() {
	vec3 light_color = vec3(0.0);
	if (use_directional_light)
		light_color = LIGHT0_COLOR * LIGHT0_ENERGY * dot(LIGHT0_DIRECTION, vec3(0.0, 1.0, 0.0));
	else
		light_color = sun_color.rgb * sun_energy;
	
	vec3 sky = cloud_color.rgb;
	
	// Find view ray intersection with cloud layer
	float t = ray_plane_intersection(vec3(0.0, 1.0, 0.0), vec3(0.0, -1.0, 0.0), vec3(0.0), EYEDIR);
	if (t >= 0.0) {
		vec3 wind = vec3(wind_speed.x, 0.0, wind_speed.y) * TIME * 0.1;
		vec3 tiling = vec3(noise_tiling.x, 1.0, noise_tiling.y) * 0.2;
		vec3 cloud_pos = EYEDIR * t * tiling + wind;

		// Parallax mapping
		float height = sample_height(cloud_pos.xz);
		cloud_pos += EYEDIR * height * cloud_depth * 0.1;
		height = sample_height(cloud_pos.xz);
		
		// Attenuate light by view angle to simulate occlusion
		float n_dot_v = dot(vec3(0.0, 1.0, 0.0), EYEDIR);
		sky += light_color * beers_law(cloud_density, 1.0 - height * n_dot_v);
	}
	vec3 horizon = cloud_color.rgb + light_color * beers_law(cloud_density, 1.0);
	vec3 ground = mix(horizon, ground_bottom_color.rgb, clamp(1.0 - pow(1.0 + EYEDIR.y, 1.0 / ground_curve), 0.0, 1.0));
	vec3 base = mix(ground, sky, step(0.0, EYEDIR.y));

	// Sun disk + scatter halo. Activated when use_directional_light is true so the
	// disk follows the actual DirectionalLight rotation. The disk is rendered far
	// brighter than the sky (×8.0) so the environment Glow pass blooms it into a
	// visible halo — matches the BO7/MW look of a hot sun-spot with soft radiance.
	//   * disk: tight 0.999..1.0 (~3px equivalent on screen)
	//   * halo: 0.97..0.999 wider scatter (atmospheric forward-scattering)
	// Without use_directional_light, no sun is drawn (sky stays clean).
	// Calibrated to look like a DISTANT sun (BO7 reference):
	//   * disk: 0.99988..0.99998 ≈ 0.28°..0.81° apex (real sun ≈ 0.53°)
	//   * halo: 0.995..0.99988 ≈ 0.86°..2.8° very tight forward-scatter
	// Disk × 3.0 (was 5.0) means less HDR overshoot → Glow blooms into a smaller
	// visual halo, sun looks farther away. Halo × 0.12 keeps the atmospheric ring
	// subtle. Combined with viewmodel env's Sky reflection, this gives sharp sun
	// glints on metal without engulfing 30% of the upper hemisphere.
	if (use_directional_light) {
		float sd = dot(EYEDIR, LIGHT0_DIRECTION);
		float disk = smoothstep(0.99988, 0.99998, sd);
		float halo = smoothstep(0.995, 0.99988, sd) * 0.12;
		base += LIGHT0_COLOR * (disk * 3.0 + halo);
	}
	COLOR = base * exposure;
}
