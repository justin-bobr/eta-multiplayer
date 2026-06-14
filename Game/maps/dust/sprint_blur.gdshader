shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform float sprint : hint_range(0.0, 1.0) = 0.0;
uniform float strength : hint_range(0.0, 0.06) = 0.032;

void fragment() {
	vec2 uv = SCREEN_UV;

	float ex = abs(uv.x - 0.5) * 2.0;
	float ey = abs(uv.y - 0.5) * 2.0;
	float edge = clamp(max(ex * 1.1, ey * 0.55), 0.0, 1.0);
	edge = edge * edge;
	float amt = edge * sprint;

	vec3 c;
	if (amt < 0.001) {

		c = texture(screen_tex, uv).rgb;
	} else {

		vec2 dir = uv - vec2(0.5);
		c = vec3(0.0);
		for (int i = 0; i < 8; i++) {
			float t = float(i) / 7.0;
			c += texture(screen_tex, uv - dir * t * strength * amt).rgb;
		}
		c /= 8.0;
	}

	COLOR = vec4(c, 1.0);
}
