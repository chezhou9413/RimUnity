using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Waylith.Rendering;
using NativeMaterial = Waylith.Rendering.Material;
using NativeShader = Waylith.Rendering.Shader;

namespace RimUnity.Rendering;

//屏幕相机输出预乘颜色，附件 Alpha 记录覆盖率，避免再次合成时透明度被平方。
internal static class ScreenCameraMaterials
{
    private sealed class Entry
    {
        internal NativeMaterial material;
        internal NativeShader source;
        internal int version = -1;
    }
    private static readonly ConditionalWeakTable<NativeMaterial, Entry> materials = new();
    private static readonly Dictionary<NativeShader, NativeShader> shaders = new();

    //只调整临时屏幕附件的混合，显式 RenderTexture 和地图继续使用原管线。
    internal static void Prepare(List<MeshDrawCommand> commands)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            var draw = commands[i];
            NativeMaterial source = draw.Material;
            NativeShader shader = Resolve(source.Shader);
            if (shader == source.Shader) continue;
            Entry entry = materials.GetOrCreateValue(source);
            if (entry.source != source.Shader)
            {
                entry.material = new NativeMaterial(source) { Shader = shader };
                entry.source = source.Shader; entry.version = source.Version;
            }
            else if (entry.version != source.Version)
            {
                entry.material.CopyPropertiesFrom(source);
                entry.material.RenderQueue = source.RenderQueue;
                entry.version = source.Version;
            }
            commands[i] = new MeshDrawCommand(draw.Mesh, entry.material, draw.PropertyRange, draw.Matrix,
                draw.SubmeshIndex, draw.PassId, draw.Instancing, draw.RenderQueueOverride, draw.Layer, draw.Storage);
        }
    }

    //普通透明采用覆盖率合成，加色只增加颜色而不遮挡下层。
    private static NativeShader Resolve(NativeShader source)
    {
        var blend = source.PipelineState.Blend;
        if (!blend.Enabled || blend.ColorOp != BlendOp.Add ||
            blend.SrcColor is not (BlendFactor.One or BlendFactor.SrcAlpha) ||
            blend.DstColor is not (BlendFactor.OneMinusSrcAlpha or BlendFactor.One)) return source;
        var coverage = blend with
        {
            SrcAlpha = blend.DstColor == BlendFactor.One ? BlendFactor.Zero : BlendFactor.One,
            DstAlpha = blend.DstColor, AlphaOp = BlendOp.Add
        };
        if (coverage == blend) return source;
        if (shaders.TryGetValue(source, out var cached)) return cached;
        string key = source.Key + "_ScreenCoverage";
        var shader = new NativeShader(key, source.ShaderPath, source.Program, source.Keywords.ToArray(), source.MaterialLayout,
            source.PipelineState with { Blend = coverage }, vertexInputLayout: source.VertexInputLayout, precompiledStages: source.PrecompiledStages);
        if (shader.HasErrors) throw new InvalidOperationException("屏幕相机材质管线无效：" + source.Key);
        shaders.Add(source, shader);
        return shader;
    }
}
