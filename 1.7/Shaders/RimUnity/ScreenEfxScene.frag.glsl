#version 450
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sceneImage;
layout(set = 0, binding = 1) uniform sampler2D bloomImage;
layout(push_constant) uniform Settings { vec4 values; } settings;
vec3 decode(vec3 c) { return mix(c / 12.92, pow(max((c + .055) / 1.055, vec3(0)), vec3(2.4)), step(vec3(.04045), c)); }
vec3 encode(vec3 c) { return mix(c * 12.92, 1.055 * pow(max(c, vec3(0)), vec3(1.0 / 2.4)) - .055, step(vec3(.0031308), c)); }
//Same grading and Bloom math as the existing final composite, before custom clips.
void main()
{
    vec3 original = textureLod(sceneImage, vUv, 0.0).rgb;
    vec3 added = settings.values.x > 0.0 ? texture(bloomImage, vUv).rgb * settings.values.x : vec3(0);
    float saturation = settings.values.z;
    vec3 color;
    if (settings.values.w > .5) {
        color = encode(original + added);
        color = clamp(mix(vec3(dot(color, vec3(.22, .707, .071))), color, saturation), 0.0, 1.0);
    } else {
        color = clamp(mix(vec3(dot(original, vec3(.22, .707, .071))), original, saturation), 0.0, 1.0);
        vec3 delta = settings.values.y > .5 ? encode(decode(max(original, vec3(0))) + added) - original : added;
        color += mix(vec3(dot(delta, vec3(.2126, .7152, .0722))), delta, saturation);
    }
    outColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}
