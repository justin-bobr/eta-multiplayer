#[compute]
#version 450

// Post-process compute pass for the PostProcessEffect CompositorEffect.
// Two passes via the `mode` push constant:
//   mode 0 -> point-copy the colour buffer into a temp image.
//   mode 1 -> heat haze + chromatic aberration + motion blur + sharpening +
//             vignette + grain, writing temp -> colour buffer.
//
// The source is always bound as a LINEAR-sampled texture (binding 0) and the
// destination always as a storage image (binding 1). No texture is ever bound
// twice, so there is no read/write or layout conflict. Pass 1 therefore samples
// the scene bilinearly - CA / motion blur / sharpen never quantise into steps.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// binding 0: source colour, linear-sampled.  pass 0 = scene buffer, pass 1 = temp.
layout(set = 0, binding = 0) uniform sampler2D src_color;
// binding 1: destination storage image.      pass 0 = temp, pass 1 = scene buffer.
layout(rgba16f, set = 0, binding = 1) uniform writeonly image2D dst_image;
layout(set = 0, binding = 2) uniform sampler2D depth_tex;
layout(set = 0, binding = 3) uniform sampler2D velocity_tex;

layout(push_constant, std430) uniform Params {
	mat4 inv_view_proj;
	vec3 camera_pos;
	float motion_blur;       // screen-space velocity multiplier (0 = off)
	vec2 raster_size;
	float aberration;
	float sharpen;
	float haze_strength;
	float grain_strength;
	float haze_start;        // metres
	float haze_end;          // metres
	float time;
	float mode;
	float vignette_strength;
	float vignette_radius;
} p;

// Bilinear scene-colour fetch (pass 1). src_color is bound through a LINEAR +
// clamp-to-edge sampler, so sub-pixel offsets interpolate in hardware.
vec3 load_uv(vec2 uv) {
	return texture(src_color, uv).rgb;
}

float hash12(vec2 v) {
	return fract(sin(dot(v, vec2(127.1, 311.7))) * 43758.5453);
}

// Interleaved gradient noise - smooth screen-space dither (Jimenez, CoD-style).
float ign(vec2 q) {
	return fract(52.9829189 * fract(dot(q, vec2(0.06711056, 0.00583715))));
}

// Smooth value noise (Perlin-ish) - used by heat haze and the AAA grain pass.
float vnoise(vec2 v) {
	vec2 i = floor(v);
	vec2 f = fract(v);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(hash12(i), hash12(i + vec2(1.0, 0.0)), f.x),
		mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), f.x),
		f.y);
}

// AAA film grain — Modern Warfare 2 / Black Ops 7 look:
//   * Two-octave value noise sampled in PIXEL space (resolution-independent grain size)
//     - fine octave at 1.5 px/cell, coarse at 3.5 px/cell (clumping)
//   * Per-channel R/G/B offsets so grain has visible chroma like 35mm colour film stock
//   * Midtone-biased: parabolic peak at luma=0.5, fade-out 0->0.18 (shadows) and
//     0.85->1.0 (highlights) — matches silver-halide density curve of real film
//   * 14 Hz time-step with interpolation between consecutive steps → reads as soft
//     drift instead of either TV-static flicker or visible stutter
// Identical implementation to post_canvas.gdshader so canvas/compositor paths match.
vec3 grain_aaa(vec3 col, vec2 px_coord, float t, float strength) {
	float gt = floor(t * 14.0);
	float gtf = fract(t * 14.0);
	vec2 sf = px_coord / 1.5;
	vec2 sc = px_coord / 3.5;
	vec3 n;
	for (int c = 0; c < 3; c++) {
		vec2 chOff = vec2(float(c) * 23.7, float(c) * 41.1);
		vec2 tOff0 = vec2(gt * 0.77, gt * 0.43);
		vec2 tOff1 = vec2((gt + 1.0) * 0.77, (gt + 1.0) * 0.43);
		float fine = mix(vnoise(sf + tOff0 + chOff), vnoise(sf + tOff1 + chOff), gtf);
		float coarse = mix(vnoise(sc + tOff0 * 0.5 + chOff), vnoise(sc + tOff1 * 0.5 + chOff), gtf);
		n[c] = (fine * 0.7 + coarse * 0.3) - 0.5;
	}
	float luma = dot(col, vec3(0.299, 0.587, 0.114));
	float w = 4.0 * luma * (1.0 - luma);
	w *= smoothstep(0.05, 0.18, luma);
	w *= 1.0 - smoothstep(0.85, 1.0, luma);
	// 0.8 = calibrated for value-noise amplitude (bounded -0.5..0.5). The previous
	// 2.5 was overshoot — produced JPEG-banding-like chunky noise on midtones.
	return col + n * strength * w * 0.8;
}

void main() {
	ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
	if (coord.x >= int(p.raster_size.x) || coord.y >= int(p.raster_size.y)) {
		return;
	}

	// Pass 0: plain point copy (colour buffer -> temp).
	if (p.mode < 0.5) {
		imageStore(dst_image, coord, texelFetch(src_color, coord, 0));
		return;
	}

	// Pass 1: effects (temp -> colour buffer).
	vec2 puv = (vec2(coord) + 0.5) / p.raster_size;
	vec4 src = texture(src_color, puv);

	// Reconstruct world distance from depth (for the heat haze gate).
	float depth = texture(depth_tex, puv).r;
	bool has_geo = depth > 0.0;
	float dist = 0.0;
	if (has_geo) {
		vec4 w = p.inv_view_proj * vec4(puv * 2.0 - 1.0, depth, 1.0);
		dist = distance(w.xyz / w.w, p.camera_pos);
	}

	// --- Heat haze: subtle desert shimmer on distant geometry only.
	// Two octaves of value noise (second is domain-warped by the first) instead
	// of pure sines - the old sin/sin pattern read as regular "rolling waves",
	// real heat haze is high-frequency and irregular. Vertical-biased + slight
	// upward drift = hot-air-rises feel. Noise is sampled in pixel-space cells
	// so cell size is resolution-stable.
	vec2 uv = puv;
	if (has_geo) {
		float gate = smoothstep(p.haze_start, p.haze_end, dist);
		gate *= gate;  // ease-in - fades in over distance instead of snapping on
		if (gate > 0.001) {
			float t = p.time;
			vec2 cell = p.raster_size / vec2(32.0, 18.0);  // elongated layers
			vec2 ns = puv * cell;
			ns.y -= t * 1.4;  // UV-y flipped vs world -> subtract = upward drift
			float n1 = vnoise(ns) - 0.5;
			float n2 = vnoise(ns * 2.3 + vec2(n1 * 1.8, t * 0.5)) - 0.5;
			// Vertical wobble dominates (what reads as "heat shimmer"),
			// horizontal stays small so distant edges don't slosh sideways.
			vec2 wob = vec2(n2 * 0.35, n1 + n2 * 0.5);
			vec2 hazed = puv + wob * p.haze_strength * gate;
			// Reject the distortion if it would pull in NEARER geometry (the
			// viewmodel). Reverse-Z: nearer = larger depth value. Haze stays
			// strictly on far objects - never smears the gun in the cam.
			if (texture(depth_tex, hazed).r < depth + 0.05) {
				uv = hazed;
			}
		}
	}

	// --- Chromatic aberration: edge-weighted spectral fringe ---
	// Real lens CA is ~zero in the centre and ramps up quadratically toward the
	// edges, as a smooth spectral smear - not a hard 3-channel ghost. load_uv is
	// bilinear, so the fringe never quantises into steps.
	vec2 cdir = uv - vec2(0.5);
	vec2 ca = cdir * length(cdir) * p.aberration;   // r^2 edge weighting
	vec3 col;
	if (p.aberration > 0.0) {
		vec3 csum = vec3(0.0);
		vec3 wsum = vec3(0.0);
		const int CA_N = 7;
		for (int i = 0; i < CA_N; i++) {
			float t = float(i) / float(CA_N - 1);
			vec3 cw = vec3(
				clamp(1.0 - t * 2.0, 0.0, 1.0),       // red   -> outward side
				1.0 - abs(t - 0.5) * 2.0,             // green -> centre
				clamp(t * 2.0 - 1.0, 0.0, 1.0));      // blue  -> inward side
			csum += load_uv(uv + mix(ca, -ca, t)) * cw;
			wsum += cw;
		}
		col = csum / max(wsum, vec3(0.0001));
	} else {
		col = load_uv(uv);
	}

	// --- Motion blur: reconstruction-style smear along the screen velocity ---
	// The velocity buffer (TAA motion vectors) encodes BOTH camera and per-object
	// motion. Jittered multi-tap sampling + per-tap velocity weighting keeps the
	// streak smooth and stops static background from bleeding onto moving objects.
	if (p.motion_blur > 0.0) {
		vec2 vel = texture(velocity_tex, uv).rg * p.motion_blur;
		float vlen = length(vel);
		if (vlen > 0.12) {
			vel *= 0.12 / vlen;  // clamp smear length - avoids huge streaks
		}
		if (vlen > 0.0008) {  // skip effectively-static pixels
			const int MB_TAPS = 16;
			float jitter = ign(vec2(coord)) - 0.5;  // smooth dither, no banding
			vec3 acc = vec3(0.0);
			float msum = 0.0;
			for (int i = 0; i < MB_TAPS; i++) {
				float t = (float(i) + jitter) / float(MB_TAPS - 1) - 0.5;
				vec2 suv = uv + vel * t;
				float tap_v = length(texture(velocity_tex, suv).rg * p.motion_blur);
				float mw = mix(0.1, 1.0, clamp(tap_v / vlen, 0.0, 1.0));
				acc += load_uv(suv) * mw;
				msum += mw;
			}
			col = acc / max(msum, 0.0001);
		}
	}

	// --- Sharpening: unsharp mask from 4 neighbours ---
	vec2 px = 1.0 / p.raster_size;
	vec3 blur = load_uv(uv + vec2(px.x, 0.0)) + load_uv(uv - vec2(px.x, 0.0))
		+ load_uv(uv + vec2(0.0, px.y)) + load_uv(uv - vec2(0.0, px.y));
	col += (col - blur * 0.25) * p.sharpen;

	// --- Vignette ---
	float vig = 1.0 - smoothstep(p.vignette_radius * 0.4, p.vignette_radius, length(puv - vec2(0.5)));
	col *= mix(1.0, vig, p.vignette_strength);

	// --- Film grain (AAA): see grain_aaa() for design. The old 3-mode picker
	// (Simple/FilmGrain/FilmGrain2) is collapsed into a single calibrated AAA pass —
	// the GrainMode export in PostProcessEffect.cs is now decorative; all values
	// produce the same grain. Pixel coord is the raw fragment coord so grain size
	// stays absolute (1.5 px) regardless of render scale.
	if (p.grain_strength > 0.0) {
		col = grain_aaa(col, vec2(coord) + 0.5, p.time, p.grain_strength);
	}

	imageStore(dst_image, coord, vec4(max(col, vec3(0.0)), src.a));
}
