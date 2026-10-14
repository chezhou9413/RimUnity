using System;
using System.Linq;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Waylith.Rendering;

namespace RimUnity.UI.Runtime;

//RimUnity 网格直接写入原生 ImGui 列表，材质载荷进入 Waylith 帧快照。
internal static class UiMeshSubmission
{
    private delegate void ResolveMaterial(Waylith.Rendering.Material material, out MaterialUniformBytes uniforms, out MaterialTextureResourceSet<Waylith.Rendering.Texture> resources, out bool globals);
    private static ConstructorInfo drawConstructor;
    private static ResolveMaterial resolveMaterial;

    //绑定帧内绘制记录的构造入口。
    private static void Bind()
    {
        if (drawConstructor != null) return;
        var drawType = NativeGuiHost.Host.GetType().Assembly.GetType("RimWorldWaylith.Imgui.ImGuiMaterialDraw", true);
        drawConstructor = drawType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 6);
        var resolver = typeof(ShaderRegistry).Assembly.GetType("Waylith.Rendering.MaterialBindingResolver", true);
        resolveMaterial = resolver.GetMethod("ResolveMaterialBindingData", BindingFlags.Static | BindingFlags.Public).CreateDelegate<ResolveMaterial>();
    }
    internal static void Draw(ReadOnlySpan<GuiVertex> vertices, Texture texture, Material material)
    {
        if (vertices.Length == 0) return;
        material = UiMaskData.Apply(material);
        if (WorldUiSubmission.Current != null) { WorldUiSubmission.Draw(vertices, texture, material); return; }
        if (!NativeGuiHost.CanDraw) return;
        texture ??= Texture2D.whiteTexture;
        var tex = NativeGuiHost.TextureReference(texture);
        var list = NativeGuiHost.DrawList;
        bool custom = material != null;
        if (custom)
        {
            UiShaderStages.Prepare(material);
            Bind();
            if ((UiMaskData.Current?.Masks.Length ?? 0) > 0 && material.HasProperty("_UiMaskAtlas"))
                NativeGuiHost.TextureReference(material.GetTexture("_UiMaskAtlas"));
            else if (!material.HasProperty("_UiMaskPixels"))
                for (int i = 0; i < (UiMaskData.Current?.Masks.Length ?? 0); i++) NativeGuiHost.TextureReference(material.GetTexture("_UiMask" + i));
            material.SetTexture("_MainTex", texture);
            var engine = material.engineMaterial;
            resolveMaterial(engine, out var uniforms, out var resources, out _);
            var draws = NativeGuiHost.MaterialDraws;
            int index = draws.Count;
            draws.Add(drawConstructor.Invoke(new object[] { engine, texture.NativeTexture, tex._TexID.Value, uniforms, resources, engine.StorageArrays }));
            tex = new ImTextureRef { _TexID = (ImTextureID)(-index - 1) };
        }
        list.PushTexture(tex);
        try
        {
            list.PrimReserve(vertices.Length, vertices.Length);
            foreach (ref readonly GuiVertex vertex in vertices)
            {
                float2 xy = NativeGuiHost.Position(vertex.position), uv = new(vertex.uv.x, custom ? vertex.uv.y : 1 - vertex.uv.y);
                list.PrimVtx(in xy, in uv, new ImColor8(vertex.color.r, vertex.color.g, vertex.color.b, vertex.color.a));
            }
        }
        finally { list.PopTexture(); }
    }
}
