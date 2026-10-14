#version 450
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sceneImage;
layout(set = 0, binding = 1) uniform sampler2D bloomImage;
layout(push_constant) uniform Settings { vec4 values; } settings;
vec3 encode(vec3 c) { return mix(c * 12.92, 1.055 * pow(max(c, vec3(0)), vec3(1.0 / 2.4)) - .055, step(vec3(.0031308), c)); }
//线性场景与 Bloom 相加之后才编码，最终显示附件负责限制屏幕范围。
void main()
{
    vec4 scene = texture(sceneImage, vUv);
    vec3 color = encode(scene.rgb + texture(bloomImage, vUv).rgb * settings.values.x);
    float luma = dot(color, vec3(.22, .707, .071));
    outColor = vec4(clamp(mix(vec3(luma), color, settings.values.y), 0.0, 1.0), 1.0);
}
