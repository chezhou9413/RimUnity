using System;

namespace RimUnity;

//在 Oklab 空间内插值颜色，输入与输出遵循渐变的颜色编码。
internal static class GradientColorInterpolation
{
    internal static Color Blend(Color a, Color b, float t, ColorSpace space)
    {
        bool linear = space == ColorSpace.Linear;
        Vector3 value = Vector3.Lerp(ToLab(linear ? a : a.linear), ToLab(linear ? b : b.linear), t);
        Color result = FromLab(value);
        return linear ? result : result.gamma;
    }

    //线性 RGB 转为锥体响应，再经立方根映射到感知亮度和色度。
    private static Vector3 ToLab(Color c)
    {
        float l = MathF.Cbrt(.4122214708f * c.r + .5363325363f * c.g + .0514459929f * c.b);
        float m = MathF.Cbrt(.2119034982f * c.r + .6806995451f * c.g + .1073969566f * c.b);
        float s = MathF.Cbrt(.0883024619f * c.r + .2817188376f * c.g + .6299787005f * c.b);
        return new(.2104542553f * l + .793617785f * m - .0040720468f * s,
            1.9779984951f * l - 2.428592205f * m + .4505937099f * s,
            .0259040371f * l + .7827717662f * m - .808675766f * s);
    }

    //逆变换保留 HDR 与有符号分量，透明度由独立关键点计算。
    private static Color FromLab(Vector3 c)
    {
        float l = c.x + .3963377774f * c.y + .2158037573f * c.z;
        float m = c.x - .1055613458f * c.y - .0638541728f * c.z;
        float s = c.x - .0894841775f * c.y - 1.291485548f * c.z;
        l *= l * l; m *= m * m; s *= s * s;
        return new(4.0767416621f * l - 3.3077115913f * m + .2309699292f * s,
            -1.2684380046f * l + 2.6097574011f * m - .3413193965f * s,
            -.0041960863f * l - .7034186147f * m + 1.707614701f * s);
    }
}
