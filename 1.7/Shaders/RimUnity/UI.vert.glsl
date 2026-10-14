#version 450
layout(location = 0) in vec2 inPosition; // @vertex Position required
layout(location = 1) in vec2 inUv; // @vertex TexCoord0 required
layout(location = 2) in vec4 inColor; // @vertex Color required
layout(set = 0, binding = 1) uniform Frame { vec4 sunDirection; mat4 worldToCamera; mat4 viewProj; } frame;
layout(location = 0) out vec2 uv;
layout(location = 1) out vec4 color;
//宿主和 Slang 的实际帧块均为行主序；转置后再用行向量，保留平移和齐次坐标。
void main() { gl_Position = vec4(inPosition, 0.0, 1.0) * transpose(frame.viewProj); uv = inUv; color = inColor; }
