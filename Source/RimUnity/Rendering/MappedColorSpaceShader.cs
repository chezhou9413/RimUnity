using System;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//内置 Unlit 和粒子映射使用与离线转换器相同的颜色边界，不改全局游戏 Shader。
internal static class MappedColorSpaceShader
{
    //复用原有 Gamma 程序，Linear 材质按需生成并注册变体。
    internal static RimUnity.Shader Wrap(Waylith.Rendering.Shader source, bool linear)
    {
        if (!linear)
        {
            ParticleBatchShaders.RegisterMapped(source);
            return RimUnity.Shader.FromEngineShader(source.Key, source);
        }
        string key = source.Key + "_Linear";
        if (ShaderRegistry.TryResolve(key, out var cached)) return RimUnity.Shader.FromEngineShader(key, cached);
        var (vertex, fragment) = ShaderColorSpace.Convert(ShaderSourceLoader.ReadShaderSource(source.VertexShaderFileName),
            ShaderSourceLoader.ReadShaderSource(source.FragmentShaderFileName));
        var layout = ShaderMetadataParser.ExtractMaterialLayout(fragment, key, 2);
        if (layout.HasErrors)
            throw new InvalidOperationException("Linear 材质布局不合法：" + key);
        string vertexName = key + ".vert.glsl", fragmentName = key + ".frag.glsl";
        ShaderStageValidation.Validate(vertex, fragment, vertexName, fragmentName);
        var stages = new PrecompiledShaderStages(
            RuntimeShaderCompiler.Compile(vertex, vertexName, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Vertex),
            RuntimeShaderCompiler.Compile(fragment, fragmentName, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment), false);
        var shader = new Waylith.Rendering.Shader(key, source.ShaderPath, source.Program, source.Keywords.ToArray(), layout, source.PipelineState,
            vertexInputLayout: source.VertexInputLayout, precompiledStages: stages);
        if (shader.HasErrors) throw new InvalidOperationException("Linear 材质注册失败：" + key);
        ShaderRegistry.Register(shader);
        ParticleBatchShaders.Register(shader, vertex, fragment);
        LinearShaderVariants.Register(shader, fragment);
        return RimUnity.Shader.FromEngineShader(key, shader);
    }
}
