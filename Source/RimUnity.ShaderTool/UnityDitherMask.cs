using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//Unity 的内置 4x4x16 阴影纹理没有材质资源可导出，使用有序抖动重建覆盖率。
internal static class UnityDitherMask
{
    internal static string Adapt(string source, ShaderLabManifest manifest)
    {
        if (!Regex.IsMatch(source, @"\b(?:uniform\s+)?sampler3D\s+_DitherMaskLOD\s*;")) return source;
        string adapted = Regex.Replace(source, @"\btex3D\s*\(\s*_DitherMaskLOD\s*,", "RU_UnityDitherMask(");
        if (adapted == source) return source;
        adapted = Regex.Replace(adapted, @"\b(?:uniform\s+)?sampler3D\s+_DitherMaskLOD\s*;", "");
        //若还有不支持的采样或传参方式，保留明确错误，不生成缺失的纹理绑定。
        if (Regex.IsMatch(adapted, @"\b_DitherMaskLOD\b"))
            throw new System.NotSupportedException("_DitherMaskLOD 目前仅支持 tex3D 采样。");
        manifest.approximations.Add("Unity 内置 _DitherMaskLOD 使用程序化 4×4 Bayer 抖动近似，保留 16 级透明阴影覆盖率；像素排列可能与 Unity 不同。");
        return """
            float4 RU_UnityDitherMask(float3 uv) {
                uint2 p = (uint2)floor(frac(uv.xy) * 4.0);
                uint low = ((p.x & 1u) ^ (p.y & 1u)) * 2u + (p.y & 1u);
                uint high = (((p.x >> 1u) & 1u) ^ ((p.y >> 1u) & 1u)) * 2u + ((p.y >> 1u) & 1u);
                float slice = floor(saturate(uv.z) * 16.0);
                //首尾层必须分别全透明和全覆盖，CFXR 的 alpha=1 对应 z=15/16。
                float threshold = ((float)(low * 4u + high) + 0.5) / 16.0;
                float coverage = threshold < saturate(slice / 15.0) ? 1.0 : 0.0;
                return float4(coverage, coverage, coverage, coverage);
            }
            """ + "\n" + adapted;
    }
}
