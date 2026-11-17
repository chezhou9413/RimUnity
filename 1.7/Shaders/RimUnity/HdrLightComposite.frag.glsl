#version 450
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D snapshotImage;
layout(set = 0, binding = 1) uniform sampler2D lightImage;
vec3 decode(vec3 c) { return mix(c / 12.92, pow(max((c + .055) / 1.055, vec3(0)), vec3(2.4)), step(vec3(.04045), c)); }
vec3 encode(vec3 c) { return mix(c * 12.92, 1.055 * pow(max(c, vec3(0)), vec3(1.0 / 2.4)) - .055, step(vec3(.0031308), c)); }
//无光像素丢弃，保留宿主场景原值；有光时在线性空间做覆盖率合成后再编码。
void main()
{
    vec4 light = texture(lightImage, vUv);
    if (light.a <= 0.0 && max(light.r, max(light.g, light.b)) <= 0.0) discard;
    vec3 scene = decode(texture(snapshotImage, vUv).rgb);
    outColor = vec4(clamp(encode(scene * (1.0 - light.a) + light.rgb), 0.0, 1.0), 1.0);
}
