#version 450
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sourceImage;
layout(push_constant) uniform Settings { vec4 values; } settings;
vec3 decode(vec3 c) { return mix(c / 12.92, pow(max((c + .055) / 1.055, vec3(0)), vec3(2.4)), step(vec3(.04045), c)); }
vec3 encode(vec3 c) { return mix(c * 12.92, 1.055 * pow(max(c, vec3(0)), vec3(1.0 / 2.4)) - .055, step(vec3(.0031308), c)); }
//只转换 RGB，不夹紧 HDR，也不改写独立混合产生的 Alpha。
void main()
{
    if (settings.values.y > .5) { outColor = vec4(0); return; }
    vec4 c = texture(sourceImage, vUv);
    if (settings.values.x > .5) c.rgb = decode(c.rgb);
    else if (settings.values.x < -.5) c.rgb = encode(c.rgb);
    outColor = c;
}
