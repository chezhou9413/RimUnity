using System;
using System.Collections.Generic;
using System.Linq;
using Waylith.Rendering;
using NativeShader = Waylith.Rendering.Shader;

namespace RimUnity.Rendering;

//原材质的各个 Pass 独立生成 GPU 蒙皮程序，保留其混合、深度与参数布局。
internal static class SkinnedShaderVariants
{
    private static readonly Dictionary<NativeShader, Shader> variants = new();

    //只在首次使用时编译；不支持的声明明确报错，不退回 CPU 蒙皮。
    internal static Shader Get(NativeShader source)
    {
        if (variants.TryGetValue(source, out var cached)) return cached;
        string key = source.Key + "_GpuSkin";
        var (vertex, fragment) = SkinnedShaderProgram.Convert(ShaderSourceLoader.ReadShaderSource(source.VertexShaderFileName),
            ShaderSourceLoader.ReadShaderSource(source.FragmentShaderFileName));
        var layout = CustomShaderTextureLayout.BindTexelSizes(ShaderStorageLayout.Extract(fragment, key));
        var inputs = ShaderMetadataParser.ExtractVertexInputLayout(vertex, key);
        if (layout.HasErrors || inputs.HasErrors)
            throw new InvalidOperationException(key + "：蒙皮 Shader 布局无效。");
        string vertexName = key + ".vert.glsl", fragmentName = key + ".frag.glsl";
        ShaderStageValidation.Validate(vertex, fragment, vertexName, fragmentName);
        var stages = new PrecompiledShaderStages(
            RuntimeShaderCompiler.Compile(vertex, vertexName, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Vertex),
            RuntimeShaderCompiler.Compile(fragment, fragmentName, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment), false);
        var shader = new NativeShader(key, source.ShaderPath, source.Program, source.Keywords.ToArray(), layout,
            source.PipelineState, vertexInputLayout: inputs, precompiledStages: stages);
        if (shader.HasErrors) throw new InvalidOperationException(key + "：蒙皮 Shader 校验失败。");
        ShaderRegistry.Register(shader);
        LinearShaderVariants.Register(shader, fragment);
        var result = Shader.FromEngineShader(key, shader);
        result.name = source.Key;
        variants.Add(source, result);
        return result;
    }

    //保留材质自己的 Pass 覆盖，不能仅读取主 Shader 的默认 Pass 列表。
    internal static Material Adapt(Material source)
    {
        var passes = Enumerable.Range(0, source.passCount).Select(i => Get(source.GetNativePass(i).Shader)).ToArray();
        var result = new Material(passes[0]);
        result.SetPasses(passes);
        if (source.shadowCaster != null) result.shadowCaster = Get(source.shadowCaster.EngineShader);
        return result;
    }
}
