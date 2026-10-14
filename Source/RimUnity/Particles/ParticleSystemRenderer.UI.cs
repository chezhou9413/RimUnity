using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RimUnity.UI.Runtime;

namespace RimUnity;

//UI 粒子复用模拟和几何生成，在所在层级位置提交并继承画布裁切及透明度。
public sealed partial class ParticleSystemRenderer
{
    private UguiRuntime.Entry uiEntry;
    private readonly Dictionary<Material, Material> uiMaterials = new();
    private readonly List<GuiVertex> guiVertices = new();
    internal void DrawUI(UguiRuntime.Entry entry)
    {
        uiEntry = entry;
        try { Draw(entry.Particles, null); }
        finally { uiEntry = null; }
    }
    private void SubmitUI(Material source)
    {
        if (!uiMaterials.TryGetValue(source, out var material))
        {
            string shaderName = source.shader.name switch
            {
                "RimUnity_ParticleAlpha" or "RimUnity_Transparent" => "RimUnity_UI",
                "RimUnity_ParticleAdditive" => "RimUnity_UIAdditive",
                _ => throw new NotSupportedException("UI 粒子材质尚无 UI 顶点入口：" + source.shader.name)
            };
            if (!Waylith.Rendering.ShaderRegistry.TryResolve(shaderName, out var shader)) throw new InvalidOperationException("UI 粒子 Shader 未注册：" + shaderName);
            material = new Material(Shader.FromEngineShader(shaderName, shader)); uiMaterials.Add(source, material);
        }
        material.CopyPropertiesFromMaterial(source);
        guiVertices.Clear();
        foreach (int i in indices)
        {
            Color color = colors[i]; color.a *= uiEntry.Alpha;
            guiVertices.Add(new GuiVertex { position = RectTransformUtility.ToGui(uiEntry.Canvas, vertices[i]), uv = uv[i], color = color });
        }
        UiMeshSubmission.Draw(CollectionsMarshal.AsSpan(guiVertices), source.mainTexture, material);
    }
}
