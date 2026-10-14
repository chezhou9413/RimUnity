#version 450
layout(location = 0) in vec2 uv;
layout(location = 1) in vec4 color;
layout(location = 0) out vec4 outColor;
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _FaceColor; // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _OutlineColor; // @color @default = vec4(0.0, 0.0, 0.0, 1.0)
    float _OutlineWidth; // @default = 0.0
    float _FaceDilate; // @default = 0.0
    float _UiMaskCount; // @default = 0.0
    float _UiRectMaskCount; // @default = 0.0
} material;
layout(set = 2, binding = 1) uniform sampler2D _MainTex;
//直接读取 TMP 的有符号距离图集，用屏幕导数抗锯齿并计算描边。
layout(std430, set = 2, binding = 2) readonly buffer _UiMasks { vec4 rows[]; } masks;
layout(set = 2, binding = 3) uniform sampler2D _UiMask0;
layout(set = 2, binding = 4) uniform sampler2D _UiMask1;
layout(set = 2, binding = 5) uniform sampler2D _UiMask2;
layout(set = 2, binding = 6) uniform sampler2D _UiMask3;
layout(set = 2, binding = 7) uniform sampler2D _UiMask4;
layout(set = 2, binding = 8) uniform sampler2D _UiMask5;
layout(set = 2, binding = 9) uniform sampler2D _UiMask6;
layout(set = 2, binding = 10) uniform sampler2D _UiMask7;
//GUI 没有 Stencil 附件，按每层 Mask 的字形透明覆盖率裁切。
float slicedCoordinate(float p, float first, float last, float uvFirst, float uvLast)
{
    if (p < first) return p / max(first, 0.000001) * uvFirst;
    if (p > 1.0 - last) return 1.0 - (1.0 - p) / max(last, 0.000001) * uvLast;
    return uvFirst + (p - first) / max(1.0 - first - last, 0.000001) * (1.0 - uvFirst - uvLast);
}
void applyUiMasks()
{
    for (int i = 0; i < int(material._UiMaskCount); ++i)
    {
        vec3 p = vec3(gl_FragCoord.xy, 1.0);
        vec2 q = vec2(dot(masks.rows[i * 7].xyz, p), dot(masks.rows[i * 7 + 1].xyz, p)) / dot(masks.rows[i * 7 + 2].xyz, p);
        if (q.x < 0.0 || q.y < 0.0 || q.x > 1.0 || q.y > 1.0) discard;
        vec4 d = masks.rows[i * 7 + 4], b = masks.rows[i * 7 + 5];
        vec4 options = masks.rows[i * 7 + 6];
        if (options.x > 0.5)
        {
            if (options.y < 0.5 && q.x > d.x && q.x < 1.0 - d.z && q.y > d.y && q.y < 1.0 - d.w) discard;
            q.x = slicedCoordinate(q.x, d.x, d.z, b.x, b.z);
            q.y = slicedCoordinate(q.y, d.y, d.w, b.y, b.w);
        }
        vec4 r = masks.rows[i * 7 + 3];
        vec2 t = q * r.zw + r.xy; t.y = 1.0 - t.y;
        float a = 1.0;
        if (i == 0) a = texture(_UiMask0, t).a;
        else if (i == 1) a = texture(_UiMask1, t).a;
        else if (i == 2) a = texture(_UiMask2, t).a;
        else if (i == 3) a = texture(_UiMask3, t).a;
        else if (i == 4) a = texture(_UiMask4, t).a;
        else if (i == 5) a = texture(_UiMask5, t).a;
        else if (i == 6) a = texture(_UiMask6, t).a;
        else if (i == 7) a = texture(_UiMask7, t).a;
        if (a < 0.001) discard;
    }
}
//矩形裁切和羽化使用与 Mask 相同的屏幕到局部投影约定。
layout(std430, set = 2, binding = 11) readonly buffer _UiRectMasks { vec4 rows[]; } rectangles;
float rectCoverage()
{
    float coverage = 1.0;
    for (int i = 0; i < int(material._UiRectMaskCount); ++i)
    {
        vec3 p = vec3(gl_FragCoord.xy, 1.0);
        vec2 q = vec2(dot(rectangles.rows[i * 4].xyz, p), dot(rectangles.rows[i * 4 + 1].xyz, p)) / dot(rectangles.rows[i * 4 + 2].xyz, p);
        if (q.x < 0.0 || q.y < 0.0 || q.x > 1.0 || q.y > 1.0) discard;
        vec2 edge = min(q, 1.0 - q);
        vec2 softness = rectangles.rows[i * 4 + 3].xy;
        vec2 alpha = clamp(edge / max(softness, vec2(0.000001)), 0.0, 1.0);
        coverage *= alpha.x * alpha.y;
    }
    return coverage;
}
void main()
{
    applyUiMasks();
    float distance = texture(_MainTex, vec2(uv.x, 1.0 - uv.y)).a;
    float aa = max(fwidth(distance), 0.0001);
    float edge = 0.5 - material._FaceDilate * 0.5;
    float face = smoothstep(edge - aa, edge + aa, distance);
    float outer = smoothstep(edge - material._OutlineWidth * 0.5 - aa, edge - material._OutlineWidth * 0.5 + aa, distance);
    vec4 faceColor = material._FaceColor * color;
    vec4 outline = material._OutlineColor; outline.a *= color.a;
    float alpha = faceColor.a * face + outline.a * (outer - face);
    vec3 rgb = (faceColor.rgb * faceColor.a * face + outline.rgb * outline.a * (outer - face)) / max(alpha, 0.0001);
    outColor = vec4(rgb, alpha * rectCoverage());
}
