using System;
using System.Collections.Generic;
using HarmonyLib;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//主线程保存绘制快照，沿用宿主的排序键、实例参数和骨骼缓冲。
internal sealed class HdrDrawFrame : IDisposable
{
    internal static readonly byte MapPass = (byte)AccessTools.Field(
        AccessTools.TypeByName("RimWorldWaylith.Rendering.RenderPassIds"), "MapMain").GetValue(null);
    internal static readonly string MetadataLayout;
    private static readonly Action<MeshDrawFrame, MeshDrawFrame> CopyMetadata = FrameMetadataCopier.Build<MeshDrawFrame>(out MetadataLayout);
    internal readonly MeshDrawFrame Frame;
    internal readonly HashSet<FinalDrawItem> Linear = new();
    private delegate FinalDrawItem RentDraw(System.Numerics.Matrix4x4 matrix, byte passId, MeshDrawGeometry mesh, MaterialBinding binding,
        bool hasOverride, in MaterialUniformBytes uniforms, in MaterialTextureResourceSet<Waylith.Rendering.Texture> resources,
        int order, int queue, InstancedDrawInfo instancing, int layer, ushort hostOrder, InstanceStorageBinding[] storage);
    private static readonly RentDraw Rent = AccessTools.Method(typeof(FinalDrawItem), "Rent").CreateDelegate<RentDraw>();
    private static readonly AccessTools.FieldRef<FinalDrawItem, DrawSortKey> SortKey =
        AccessTools.FieldRefAccess<FinalDrawItem, DrawSortKey>("<SortKey>k__BackingField");

    //只有地图帧包含线性材质时才需要接管颜色附件。
    internal static bool Needed(MeshDrawFrame frame)
    {
        foreach (var draw in frame.Draws) if (IsMap(draw) && LinearShaderVariants.TryGet(draw.Binding.Shader, out _)) return true;
        foreach (var draw in frame.PersistentDraws) if (IsMap(draw) && LinearShaderVariants.TryGet(draw.Binding.Shader, out _)) return true;
        return false;
    }
    private static bool IsMap(FinalDrawItem draw) => draw.PassId == 0 || draw.PassId == MapPass;

    internal HdrDrawFrame(MeshDrawFrame source, int mask = -1, bool readOnlyDepth = false)
    {
        Frame = Shell(source, MeshDrawFrame.RentList(), MeshDrawFrame.RentList());
        var dynamic = new List<FinalDrawItem>();
        foreach (var item in source.Draws) if (IsMap(item) && (mask & (1 << item.Layer)) != 0) dynamic.Add(item);
        dynamic.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
        var persistent = source.PersistentDraws;
        var bindings = new Dictionary<MaterialBinding, MaterialBinding>();
        int p = 0, d = 0;
        try
        {
            //与宿主合并顺序一致：键相等时先画持久绘制项。
            while (p < persistent.Count || d < dynamic.Count)
            {
                if (p < persistent.Count && (!IsMap(persistent[p]) || (mask & (1 << persistent[p].Layer)) == 0)) { p++; continue; }
                var item = p < persistent.Count && (d == dynamic.Count || persistent[p].SortKey.CompareTo(dynamic[d].SortKey) <= 0)
                    ? persistent[p++] : dynamic[d++];
                var original = item.Binding;
                bool linear = LinearShaderVariants.TryGet(original.Shader, out var shader);
                if (!linear) shader = original.Shader;
                if (!bindings.TryGetValue(original, out var binding))
                {
                    var state = original.PipelineState;
                    if (readOnlyDepth) state = state with { Depth = state.Depth with { Write = false },
                        Stencil = state.Stencil with { WriteMask = 0, PassOp = StencilOp.Keep, FailOp = StencilOp.Keep, DepthFailOp = StencilOp.Keep } };
                    var variant = original.ShaderVariant;
                    var key = new ShaderVariantKey(shader, variant.DynamicKeywordKeyId, variant.DynamicKeywordKey,
                        variant.UseTexture, variant.OutlineMrt, variant.Instanced);
                    binding = new MaterialBinding(original.Id, original.MaterialId, original.MaterialVersion, original.DebugName,
                        shader, original.MaterialLayout, original.Uniforms, original.Resources, original.RenderQueue,
                        key, state, original.WrapModeOverride, original.UsesGlobals);
                    binding.UpdateStorageArrays(original.StorageArrays);
                    bindings.Add(original, binding);
                }
                var copy = Rent(item.Matrix, item.PassId, item.Mesh, binding, true, in item.Uniforms, in item.Resources,
                    Frame.Draws.Count, item.RenderQueue, item.Instancing, item.Layer, 0, item.Storage);
                SortKey(copy) = item.SortKey;
                Frame.Draws.Add(copy);
                if (linear) Linear.Add(copy);
            }
        }
        catch { Frame.Dispose(); throw; }
    }

    //借用的 PersistentDraws 不归临时帧销毁，所有绘制项只由快照释放一次。
    internal static MeshDrawFrame Shell(MeshDrawFrame source, List<FinalDrawItem> draws, List<FinalDrawItem> persistent)
    {
        var frame = new MeshDrawFrame(source.FrameNumber, source.View, draws, persistent, source.SunDirection);
        CopyMetadata(source, frame);
        return frame;
    }
    public void Dispose() => Frame.Dispose();
}
