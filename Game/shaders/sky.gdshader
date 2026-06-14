shader_type sky;

group_uniforms Sky;

uniform bool use_directional_light = false;
uniform vec4 sun_color : source_color = vec4(1.0);
uniform float sun_energy = 15.0;
uniform vec4 cloud_color : source_color = vec4(0.296, 0.402, 0.521, 1.0);

uniform float cloud_density = 5.0;

uniform float cloud_depth = 2.0;

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

	float t = ray_plane_intersection(vec3(0.0, 1.0, 0.0), vec3(0.0, -1.0, 0.0), vec3(0.0), EYEDIR);
	if (t >= 0.0) {
		vec3 wind = vec3(wind_speed.x, 0.0, wind_speed.y) * TIME * 0.1;
		vec3 tiling = vec3(noise_tiling.x, 1.0, noise_tiling.y) * 0.2;
		vec3 cloud_pos = EYEDIR * t * tiling + wind;

		float height = sample_height(cloud_pos.xz);
		cloud_pos += EYEDIR * height * cloud_depth * 0.1;
		height = sample_height(cloud_pos.xz);

		float n_dot_v = dot(vec3(0.0, 1.0, 0.0), EYEDIR);
		sky += light_color * beers_law(cloud_density, 1.0 - height * n_dot_v);
	}
	vec3 horizon = cloud_color.rgb + light_color * beers_law(cloud_density, 1.0);
	vec3 ground = mix(horizon, ground_bottom_color.rgb, clamp(1.0 - pow(1.0 + EYEDIR.y, 1.0 / ground_curve), 0.0, 1.0));
	vec3 base = mix(ground, sky, step(0.0, EYEDIR.y));

	if (use_directional_light) {
		float sd = dot(EYEDIR, LIGHT0_DIRECTION);
		float disk = smoothstep(0.99988, 0.99998, sd);
		float halo = smoothstep(0.995, 0.99988, sd) * 0.12;
		base += LIGHT0_COLOR * (disk * 3.0 + halo);
	}
	COLOR = base * exposure;
}
