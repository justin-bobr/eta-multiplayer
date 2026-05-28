shader_type spatial;
render_mode world_vertex_coords, cull_disabled, depth_draw_always;

uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap_anisotropic, repeat_disable;
group_uniforms colours;
uniform vec3 absorptioncolour : source_color;
uniform float absorptionmultiplier = 1.0;

group_uniforms material;
uniform float AirIOR = 1.0;
uniform float IOR = 1.33;

group_uniforms textures;
uniform vec2 sampler1speed = vec2(0.02, 0.0);
uniform vec2 sampler2speed = vec2(0.0, 0.02);
uniform float samplermix : hint_range(0.0, 1.0, 0.1) = 0.5;
uniform vec2 samplerscale = vec2(0.1);
uniform sampler2D normal1tex : filter_linear_mipmap_anisotropic, hint_normal;
uniform sampler2D normal2tex : filter_linear_mipmap_anisotropic, hint_normal;
uniform float normalstrength : hint_range(0.0, 5.0, 0.01) = 1.0;
uniform sampler2D height1tex : filter_linear_mipmap_anisotropic;
uniform sampler2D height2tex : filter_linear_mipmap_anisotropic;
uniform float heightstrength : hint_range(0.0, 5.0, 0.01) = 0.12;
uniform sampler2D edge1tex : filter_linear_mipmap_anisotropic;
uniform sampler2D edge2tex : filter_linear_mipmap_anisotropic;

varying vec2 position;
varying vec3 wposition;

group_uniforms refraction;
uniform int refraction_type : hint_enum("None", "Simple", "Traced") = 1;
uniform float simple_refraction_amount = 0.1;
uniform float traced_refraction_far_clip = 50.0;
uniform int traced_refraction_steps : hint_range(64, 1024, 16) = 512;

group_uniforms edge;

uniform float edge_size : hint_range(0.01, 0.5, 0.01) = 0.1;
uniform bool foam_or_fade = false;

uniform sampler2D DEPTH_TEXTURE : hint_depth_texture, filter_linear_mipmap, repeat_disable;

group_uniforms screen_space_reflection;

uniform int ssr_type : hint_enum("None", "Simple", "Traced") = 2;
uniform float traced_ssr_far_clip = 50.0;
uniform int traced_ssr_steps : hint_range(64, 1024, 16) = 512;
uniform float ssr_screen_fade : hint_range(0.01, 0.5, 0.01) = 0.05;

float schlickfresnel(float ior1, float ior2, vec3 view, vec3 norm) {
	float incident = dot(view, norm);
	float reflectance = ((ior2 - ior1)/(ior2 + ior1)) * ((ior2 - ior1)/(ior2 + ior1));
	float fresnelincident = reflectance + (1.0 - reflectance) * pow(1.0 - cos(incident), 5.0);
	return clamp(fresnelincident / incident, 0.0, 1.0);
}

void vertex() {
	position = VERTEX.xz;
	UV = VERTEX.xz * samplerscale + (sampler1speed * TIME);
	UV2 = VERTEX.xz * samplerscale + (sampler2speed * TIME);
	float height = mix(texture(height1tex, UV),texture(height2tex, UV2),samplermix).x;
	VERTEX.y += (height - 0.5) * heightstrength;
	wposition = VERTEX;
	// Called for every vertex the material is visible on.
}

float snells_window(vec3 normal, vec3 view, float ior) {
	float cos_theta = dot(normal, view);
	return step(sqrt(1.0 - cos_theta * cos_theta) * ior, 1.0);
}

float linear_depth(float nonlinear_depth, mat4 inv_projection_matrix) {
	#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
		nonlinear_depth = nonlinear_depth * 2.0 - 1.0;
	#endif
	return 1.0 / (nonlinear_depth * inv_projection_matrix[2].w + inv_projection_matrix[3].w);
}

float nonlinear_depth(float linear_depth, mat4 inv_projection_matrix) {
	#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
		linear_depth = linear_depth * 0.5 + 0.5;
	#endif
	return (1.0 / linear_depth - inv_projection_matrix[3].w) / inv_projection_matrix[2].w;
}

vec2 view2uv(vec3 position_view_space, mat4 proj_m)
{
	vec4 position_clip_space = proj_m * vec4(position_view_space.xyz, 1.0);
	vec2 position_ndc = position_clip_space.xy / position_clip_space.w;
	return position_ndc.xy * 0.5 + 0.5;
}

float remap(float x, float min1, float max1, float min2, float max2) {
	return ((x - min1) / (max1 - min1) + min2) * (max2 - min2);
}
float remap1(float x, float min1, float max1) {
	return (x - min1) / (max1 - min1);
}

float edge_fade(vec2 uv, float size) {
	float x1 = clamp(remap1(uv.x, 0.0, size), 0.0, 1.0);
	float x2 = clamp(remap1(uv.x, 1.0, 1.0 - size), 0.0, 1.0);
	float y1 = clamp(remap1(uv.y, 0.0, size), 0.0, 1.0);
	float y2 = clamp(remap1(uv.y, 1.0, 1.0 - size), 0.0, 1.0);
	return x1*x2*y1*y2;
}

void refraction(vec3 normal, vec3 vertex, vec2 normmap, bool reverse, mat4 projection_matrix, vec3 tangent, vec3 binormal, vec2 screen_uv, vec2 viewport_size, inout vec3 emission) {
	vec3 view = normalize(-vertex);
	mat4 inv_proj_mat = inverse(projection_matrix);
	if (refraction_type == 2) {
		
		vec3 refracted = refract(view, normal, AirIOR / -IOR);
		if (reverse) {
			refracted = refract(view, normal, -IOR / AirIOR);
		}
		vec3 pos = vertex;
		float dist = 0.0;
		int curstep = 0;
		bool finished = false;
		vec2 uv;
		float currentdepth;
		float lastdepth1 = -vertex.z;
			
		while (curstep < traced_refraction_steps) {
			float step_scale = float(curstep + 1) / float(traced_refraction_steps);
			float step_dist = step_scale * step_scale * traced_refraction_far_clip;
			pos += refracted * step_dist;
			dist += step_dist;
			curstep += 1;
			float lastdepth2 = lastdepth1;
			float lastdepth1 = currentdepth;
			currentdepth = -pos.z;
			uv = view2uv(pos, projection_matrix);
			float testdepth = linear_depth(texture(DEPTH_TEXTURE, uv).r, inv_proj_mat);
			if (testdepth < lastdepth2) {
				break;
			}
			if (testdepth < currentdepth) {
				finished = true;
				break;
			}
		}
		
		
		
		
		
		
		float lineardepth = linear_depth(texture(DEPTH_TEXTURE, uv).r, inv_proj_mat);
		//float selfdepth = 1.0/(1.0 + 2.0 * distance(wposition, CAMERA_POSITION_WORLD));
		//vec3 newvolcolour = mix(volumecolour, vec3(1.0), clamp(1.0 / (depth_diff * 1.0), 0.0, 1.0));
		
		vec3 newvolcolour = vec3(1.0);
		if (reverse) {
		} else {
			newvolcolour = pow(absorptioncolour, vec3(dist * absorptionmultiplier));
		}
		emission = newvolcolour * texture(screen, uv).rgb;
	} else if (refraction_type == 1) {
		
		float lineardepth = linear_depth(texture(DEPTH_TEXTURE, screen_uv).r, inv_proj_mat);
		float selfdepth = -vertex.z;
		float depth_diff = lineardepth - selfdepth;
		float x_mult = viewport_size.y / viewport_size.x;
		vec3 tanx = binormal * (normmap.x - 0.5) * normalstrength * x_mult;
		vec3 tany = tangent * (normmap.y - 0.5) * normalstrength;
		vec2 refracted_uv = screen_uv + (tanx + tany).xy * simple_refraction_amount * depth_diff / lineardepth;
		float newdepth = linear_depth(texture(DEPTH_TEXTURE, refracted_uv).r, inv_proj_mat);
		if (newdepth >= selfdepth) {
			depth_diff = newdepth - selfdepth;
		}
		//float selfdepth = 1.0/(1.0 + 2.0 * distance(wposition, CAMERA_POSITION_WORLD));
		//vec3 newvolcolour = mix(volumecolour, vec3(1.0), clamp(1.0 / (depth_diff * 1.0), 0.0, 1.0));
		vec3 newvolcolour;
		if (reverse) {
			newvolcolour = vec3(1.0);
		} else {
			newvolcolour = pow(absorptioncolour, vec3(depth_diff * absorptionmultiplier));
		}
		emission = newvolcolour * texture(screen, refracted_uv).rgb;

		if (newdepth < selfdepth) {
			emission = newvolcolour * texture(screen, screen_uv).rgb;
		}

	} else {
		float lineardepth = linear_depth(texture(DEPTH_TEXTURE, screen_uv).r, inv_proj_mat);
		float selfdepth = -vertex.z;
		float depth_diff = lineardepth - selfdepth;
		vec3 newvolcolour;
		if (reverse) {
			newvolcolour = vec3(1.0);
		} else {
			newvolcolour = pow(absorptioncolour, vec3(depth_diff * absorptionmultiplier));
		}
		emission = newvolcolour * texture(screen, screen_uv).rgb;
	}
}

void reflection(vec3 normal, vec3 vertex, mat4 projection_matrix, inout vec3 emission, inout float specular) {
	vec3 view = normalize(-vertex);
	mat4 inv_proj_mat = inverse(projection_matrix);
	if (ssr_type == 2) {
		vec3 reflected = -reflect(view, normal);
		vec3 pos = vertex;
		int curstep = 0;
		bool finished = false;
		vec2 uv;
		float currentdepth;
		float lastdepth1 = -vertex.z;
		while (curstep < traced_ssr_steps) {
			float step_scale = float(curstep + 1) / float(traced_ssr_steps);
			float step_dist = step_scale * step_scale * traced_ssr_far_clip;
			pos += reflected * step_dist;
			curstep += 1;
			float lastdepth2 = lastdepth1;
			float lastdepth1 = currentdepth;
			currentdepth = -pos.z;
			uv = view2uv(pos, projection_matrix);
			if (!(uv.x < 1.0 && uv.y < 1.0 && uv.x > 0.0 && uv.y > 0.0)) {
				break;
			}
			float testdepth = linear_depth(texture(DEPTH_TEXTURE, uv).r, inv_proj_mat);
			if (testdepth < lastdepth2) {
				break;
			}
			if (testdepth < currentdepth) {
				finished = true;
				break;
			}
		}
		if (finished && currentdepth < traced_ssr_far_clip * 0.99) {
			specular *= 1.0 - edge_fade(uv, ssr_screen_fade);
			emission += texture(screen, uv).xyz * schlickfresnel(1.0, 1.33, view, normal) * edge_fade(uv, ssr_screen_fade);
		}
	} else if (ssr_type == 1) {
		vec3 reflected = -reflect(view, normal);
		if (reflected.z < 0.0) {
			vec2 uv = view2uv(reflected, projection_matrix);
			float testdepth = texture(DEPTH_TEXTURE, uv).r;
			
			if (testdepth > 0.0) {
				specular *= 1.0 - edge_fade(uv, ssr_screen_fade);
				emission += texture(screen, uv).xyz * schlickfresnel(1.0, 1.33, view, normal) * edge_fade(uv, ssr_screen_fade);
			}
		}
	}
	
}


void fragment() {

	vec3 onorm = NORMAL;

	vec2 normmap = mix(texture(normal1tex, UV),texture(normal2tex, UV2),samplermix).xy;
	NORMAL += TANGENT * (normmap.x - 0.5) * normalstrength;
	NORMAL += BINORMAL * (normmap.y - 0.5) * normalstrength;

	vec3 wnorm = (vec4(NORMAL, 0.0) * VIEW_MATRIX).xyz;
	vec3 wview = (vec4(VIEW, 0.0) * VIEW_MATRIX).xyz;
	ROUGHNESS = 0.05;
	METALLIC = 0.0;
	ALBEDO = vec3(0.0);
	if (FRONT_FACING) {
		float fres = clamp(schlickfresnel(AirIOR, IOR, VIEW, NORMAL), 0.0, 1.0);

		float lineardepth = linear_depth(texture(DEPTH_TEXTURE, SCREEN_UV).r, INV_PROJECTION_MATRIX);
		float selfdepth = -VERTEX.z;
		float depth_diff = lineardepth - selfdepth;
		// REFRACTION
		
		refraction(NORMAL, VERTEX, normmap, false, PROJECTION_MATRIX, TANGENT, BINORMAL, SCREEN_UV, VIEWPORT_SIZE, EMISSION);
		EMISSION *= 1.0 - fres;
		// SSR
		reflection(NORMAL, VERTEX, PROJECTION_MATRIX, EMISSION, SPECULAR);
		// EDGE EFFECT

		float distfromedge = depth_diff * dot(normalize(NORMAL), normalize(-VERTEX)) / VIEW.z;
		if (distfromedge < edge_size) {
			distfromedge /= edge_size;
			if (foam_or_fade) {
				ALPHA = distfromedge;
			} else {
				float edgetex = mix(texture(edge1tex, UV).r, texture(edge2tex, UV2).r, samplermix);
				if (edgetex > distfromedge) {
					ALBEDO = vec3(1.0);
					ROUGHNESS = 1.0;
					METALLIC = 1.0;
					EMISSION = vec3(0.0);
					NORMAL = onorm;
				}
			}
		} else {
			ALPHA = 1.0;
		}

	} else {

		// SNELLS WINDOW
		float window = dot(refract(VIEW, NORMAL, IOR / AirIOR), -NORMAL);

		if (window >= 1.00) {
			SPECULAR = 0.0;
			refraction(NORMAL, VERTEX, normmap, true, PROJECTION_MATRIX, TANGENT, BINORMAL, SCREEN_UV, VIEWPORT_SIZE, EMISSION);
		} else if (window <= 0.0) {
			// SSR

			reflection(NORMAL, VERTEX, PROJECTION_MATRIX, EMISSION, SPECULAR);

		} else {
			SPECULAR *= clamp(1.0 - window, 0.0, 1.0);

			refraction(NORMAL, VERTEX, normmap, true, PROJECTION_MATRIX, TANGENT, BINORMAL, SCREEN_UV, VIEWPORT_SIZE, EMISSION);
			EMISSION *= window;
			// SSR

			reflection(NORMAL, VERTEX, PROJECTION_MATRIX, EMISSION, SPECULAR);
		}

	}
	ALPHA = clamp(ALPHA, 0.0, 1.0);
}