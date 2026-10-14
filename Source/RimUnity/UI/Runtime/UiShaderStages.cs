using System;
using System.Collections.Generic;
using Waylith.Rendering;

namespace RimUnity.UI.Runtime;

//宿主 GUI 仅对 PrecompiledStages 使用材质的顶点程序和混合状态。
internal static class UiShaderStages
{
    private static readonly Dictionary<string, RimUnity.Shader> compiled = new(StringComparer.Ordinal);
    internal static void Prepare(RimUnity.Material material)
    {
        var source = material.engineMaterial.Shader;
        if (source.PrecompiledStages != null) return;
        if (source.Key is not ("RimUnity_UI" or "RimUnity_UIAdditive" or "RimUnity_UIPremultiply" or "RimUnity_SDF"))
            throw new NotSupportedException("UI 材质必须提供已经适配的顶点阶段：" + source.Key);
        if (!compiled.TryGetValue(source.Key, out var shader))
        {
            string vertex = ShaderSourceLoader.ReadShaderSource(source.VertexShaderFileName);
            string fragment = ShaderSourceLoader.ReadShaderSource(source.FragmentShaderFileName);
            var stages = new PrecompiledShaderStages(
                RuntimeShaderCompiler.Compile(vertex, source.VertexShaderFileName, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Vertex),
                RuntimeShaderCompiler.Compile(fragment, source.FragmentShaderFileName, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment), false);
            var adapted = new Waylith.Rendering.Shader(source.Key, source.ShaderPath, source.Program, materialLayout: source.MaterialLayout,
                pipelineState: source.PipelineState, vertexInputLayout: source.VertexInputLayout, precompiledStages: stages);
            if (adapted.HasErrors) throw new InvalidOperationException("UI Shader 布局不合法：" + source.Key);
            ShaderRegistry.Register(adapted);
            shader = RimUnity.Shader.FromEngineShader(source.Key, adapted); compiled.Add(source.Key, shader);
        }
        material.shader = shader;
    }
}
