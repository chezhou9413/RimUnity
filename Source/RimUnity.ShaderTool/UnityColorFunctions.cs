using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//对应 UnityCG 的颜色辅助函数；由源程序显式调用的转换仍按 Unity 公式执行。
internal static class UnityColorFunctions
{
    internal static string Add(string source)
    {
        var functions = new Dictionary<string, string>
        {
            ["SRGBToLinear"] = "float SRGBToLinear(float c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }\nfloat3 SRGBToLinear(float3 c) { return float3(SRGBToLinear(c.r), SRGBToLinear(c.g), SRGBToLinear(c.b)); }\nfloat4 SRGBToLinear(float4 c) { return float4(SRGBToLinear(c.rgb), c.a); }",
            ["LinearToSRGB"] = "float LinearToSRGB(float c) { return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(abs(c), 1.0 / 2.4) - 0.055; }\nfloat3 LinearToSRGB(float3 c) { return float3(LinearToSRGB(c.r), LinearToSRGB(c.g), LinearToSRGB(c.b)); }\nfloat4 LinearToSRGB(float4 c) { return float4(LinearToSRGB(c.rgb), c.a); }",
            ["IsGammaSpace"] = "bool IsGammaSpace() {\n#ifdef UNITY_COLORSPACE_GAMMA\nreturn true;\n#else\nreturn false;\n#endif\n}",
            ["GammaToLinearSpaceExact"] = "float GammaToLinearSpaceExact(float v) { return v <= 0.04045 ? v / 12.92 : v < 1 ? pow((v + 0.055) / 1.055, 2.4) : pow(v, 2.2); }",
            ["LinearToGammaSpaceExact"] = "float LinearToGammaSpaceExact(float v) { return v <= 0 ? 0 : v <= 0.0031308 ? v * 12.92 : v < 1 ? 1.055 * pow(v, 0.4166667) - 0.055 : pow(v, 0.45454545); }",
            ["GammaToLinearSpace"] = "float3 GammaToLinearSpace(float3 c) { return c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878); }",
            ["LinearToGammaSpace"] = "float3 LinearToGammaSpace(float3 c) { return max(1.055 * pow(max(c, 0.0), 0.416666667) - 0.055, 0.0); }"
        };
        foreach (var entry in functions)
            if (Regex.IsMatch(source, @"\b" + entry.Key + @"\s*\(") && !Regex.IsMatch(source, @"\b(?:float[1-4]?|bool)\s+" + entry.Key + @"\s*\("))
                source = entry.Value + "\n" + source;
        return source;
    }
}
