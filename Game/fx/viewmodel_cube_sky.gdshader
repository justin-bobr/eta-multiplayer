// Custom sky shader that samples a 6-face cubemap stored as 6 separate ViewportTextures.
// Used in the viewmodel viewport's environment to provide REAL world reflections on the
// gun — the 6 ViewportTextures are rendered each frame (round-robin) from 6 cameras at the
// player's position facing each cube direction. See WorldCaptureRig.cs.
//
// The viewport's own_world_3d isolates the gun from the world's reflection probes / GI;
// this shader bridges that gap by feeding the world's actual rendered content back into
// the viewmodel's environment radiance map. End result: gun metal reflects the deck, walls,
// tunnel ceilings — whatever is around the player at runtime.
//
// Cube face convention (right-handed, matches Godot ReflectionProbe layout):
//   +X = looking right       UV from (-Z, -Y)
//   -X = looking left        UV from (+Z, -Y)
//   +Y = looking up          UV from (+X, +Z)
//   -Y = looking down        UV from (+X, -Z)
//   +Z = looking back        UV from (-X, -Y)
//   -Z = looking forward     UV from (+X, -Y)
//
// intensity uniform allows the sky / radiance output to be scaled — useful when the captured
// content is dim and you want stronger reflections without re-tuning gun materials.
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
