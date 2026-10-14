using System;
using System.Collections.Generic;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//ShaderDef 未暴露 BlendOp，减法粒子在资源加载时创建明确的管线变体。
internal static class StandardParticleShader
{
    private static readonly Dictionary<string, Waylith.Rendering.Shader> cache = new();

    internal static RimUnity.Shader Resolve(Waylith.Rendering.Shader source, bool linear = false)
    {
        if (source.Key != "RimUnity_StandardParticle5" && source.Key != "RimUnity_StandardParticle5Back")
            return MappedColorSpaceShader.Wrap(source, linear);
        if (cache.TryGetValue(source.Key, out var cached)) return MappedColorSpaceShader.Wrap(cached, linear);
        var blend = new BlendState(true, BlendFactor.SrcAlpha, BlendFactor.One, BlendOp.ReverseSubtract,
            BlendFactor.SrcAlpha, BlendFactor.One, BlendOp.ReverseSubtract);
        var pipeline = new PipelineState(blend, new DepthState(source.DepthTest, source.DepthWrite), source.Program.cullMode,
            source.Program.ResolveStencil(), source.Program.colorWrite);
        var stages = new PrecompiledShaderStages(
            RuntimeShaderCompiler.Compile(ShaderSourceLoader.ReadShaderSource(source.VertexShaderFileName), source.VertexShaderFileName, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Vertex),
            RuntimeShaderCompiler.Compile(ShaderSourceLoader.ReadShaderSource(source.FragmentShaderFileName), source.FragmentShaderFileName, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment), false);
        string key = source.Key + "_ReverseSubtract";
        var shader = new Waylith.Rendering.Shader(key, source.ShaderPath, source.Program, materialLayout: source.MaterialLayout,
            pipelineState: pipeline, vertexInputLayout: source.VertexInputLayout, precompiledStages: stages);
        if (shader.HasErrors) throw new InvalidOperationException("减法粒子 Shader 注册失败：" + key);
        ShaderRegistry.Register(shader);
        cache.Add(source.Key, shader);
        return MappedColorSpaceShader.Wrap(shader, linear);
    }
}
