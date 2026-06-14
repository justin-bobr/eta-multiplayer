shader_type sky;

uniform sampler2D face_px : repeat_disable, filter_linear;
uniform sampler2D face_nx : repeat_disable, filter_linear;
uniform sampler2D face_py : repeat_disable, filter_linear;
uniform sampler2D face_ny : repeat_disable, filter_linear;
uniform sampler2D face_pz : repeat_disable, filter_linear;
uniform sampler2D face_nz : repeat_disable, filter_linear;
uniform float intensity : hint_range(0.0, 4.0) = 1.0;

void sky() {
	vec3 d = EYEDIR;
	vec3 a = abs(d);
	vec3 col;
	if (a.x >= a.y && a.x >= a.z) {
		if (d.x > 0.0) {
			vec2 uv = vec2(-d.z, -d.y) / d.x * 0.5 + 0.5;
			col = texture(face_px, uv).rgb;
		} else {
			vec2 uv = vec2(d.z, -d.y) / -d.x * 0.5 + 0.5;
			col = texture(face_nx, uv).rgb;
		}
	} else if (a.y >= a.z) {
		if (d.y > 0.0) {
			vec2 uv = vec2(d.x, d.z) / d.y * 0.5 + 0.5;
			col = texture(face_py, uv).rgb;
		} else {
			vec2 uv = vec2(d.x, -d.z) / -d.y * 0.5 + 0.5;
			col = texture(face_ny, uv).rgb;
		}
	} else {
		if (d.z > 0.0) {
			vec2 uv = vec2(-d.x, -d.y) / d.z * 0.5 + 0.5;
			col = texture(face_pz, uv).rgb;
		} else {
			vec2 uv = vec2(d.x, -d.y) / -d.z * 0.5 + 0.5;
			col = texture(face_nz, uv).rgb;
		}
	}
	COLOR = col * intensity;
}
