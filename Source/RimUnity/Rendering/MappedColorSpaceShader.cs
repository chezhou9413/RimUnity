using System;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//内置 Unlit 和粒子映射使用与离线转换器相同的颜色边界，不改全局游戏 Shader。
internal static class MappedColorSpaceShader
{
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
        if (layout.HasErrors || ShaderMetadataParser.ValidateStageInterface(vertex, fragment, key, key))
            throw new InvalidOperationException("Linear 材质布局不合法：" + key);
        var stages = new PrecompiledShaderStages(
            RuntimeShaderCompiler.Compile(vertex, key + ".vert.glsl", "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Vertex),
            RuntimeShaderCompiler.Compile(fragment, key + ".frag.glsl", "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment), false);
        var shader = new Waylith.Rendering.Shader(key, source.ShaderPath, source.Program, source.Keywords.ToArray(), layout, source.PipelineState,
            vertexInputLayout: source.VertexInputLayout, precompiledStages: stages);
        if (shader.HasErrors) throw new InvalidOperationException("Linear 材质注册失败：" + key);
        ShaderRegistry.Register(shader);
        ParticleBatchShaders.Register(shader, vertex, fragment);
        LinearShaderVariants.Register(shader, fragment);
        return RimUnity.Shader.FromEngineShader(key, shader);
    }
}
