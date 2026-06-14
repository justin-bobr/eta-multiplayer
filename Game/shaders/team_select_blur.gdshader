shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform float mip_level : hint_range(0.0, 8.0) = 5.0;
uniform vec4 tint : source_color = vec4(0.0, 0.0, 0.0, 0.35);

void fragment() {
	vec3 blurred = textureLod(screen_tex, SCREEN_UV, mip_level).rgb;
	COLOR = vec4(mix(blurred, tint.rgb, tint.a), 1.0);
}
