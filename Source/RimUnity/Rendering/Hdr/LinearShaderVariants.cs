using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using Waylith.Rendering;
using NativeShader = Waylith.Rendering.Shader;

namespace RimUnity.Rendering;

//保留屏幕输出程序，另存直接输出线性颜色的浮点附件程序。
internal static class LinearShaderVariants
{
    private static readonly ConcurrentDictionary<NativeShader, NativeShader> Variants = new();
    internal static bool TryGet(NativeShader source, out NativeShader linear) => Variants.TryGetValue(source, out linear);

    //只移除导出器生成的最终编码，不改原 Shader 的 Alpha、混合和纹理计算。
    internal static void Register(NativeShader source, string fragment)
    {
        if (!fragment.Contains("// @rimunity-linear-program 1", StringComparison.Ordinal)) return;
        const string wrapper = @"void\s+main\s*\(\s*\)\s*\{\s*RU_SourceMain\(\);\s*(\w+)\.rgb\s*=\s*RU_Encode\(\1\.rgb\);\s*\}";
        var matches = Regex.Matches(fragment, wrapper);
        if (matches.Count != 1) throw new InvalidOperationException(source.Key + "：线性程序缺少唯一的输出编码入口。");
        string linearSource = Regex.Replace(fragment, wrapper, "void main() { RU_SourceMain(); }");
        string key = source.Key + "_HdrOutput";
        var stages = new PrecompiledShaderStages(source.PrecompiledStages.Vertex,
            RuntimeShaderCompiler.Compile(linearSource, key + ".frag.glsl", "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment), false);
        var shader = new NativeShader(key, source.ShaderPath, source.Program, source.Keywords.ToArray(), source.MaterialLayout,
            source.PipelineState, vertexInputLayout: source.VertexInputLayout, precompiledStages: stages);
        if (shader.HasErrors) throw new InvalidOperationException(key + "：线性 HDR 程序布局不合法。");
        ShaderRegistry.Register(shader);
        Variants[source] = shader;
    }
}
