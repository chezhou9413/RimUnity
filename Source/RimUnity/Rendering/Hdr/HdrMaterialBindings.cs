using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//派生绑定只在源状态改变时重建，旧快照始终持有不可变的历史绑定。
internal static class HdrMaterialBindings
{
    private sealed class Variants
    {
        internal Entry Color, ReadOnlyDepth;
    }
    private sealed record Entry(long DataVersion, int MaterialVersion, PipelineState Pipeline, ShaderVariantKey Variant,
        int Queue, bool UsesGlobals, MaterialBinding Binding);
    private static readonly ConditionalWeakTable<MaterialBinding, Variants> cache = new();
    //宿主缓存只比较版本是否相等；负数区间与原绑定的正数版本隔离，也区分各次派生结果。
    private static long nextDataVersion = -1;
    private static readonly AccessTools.FieldRef<MaterialBinding, long> DataVersion =
        AccessTools.FieldRefAccess<MaterialBinding, long>("resolvedDataVersion");

    //弱键不延长源材质生命周期；颜色和只读深度分别缓存，避免相互覆盖状态。
    internal static MaterialBinding Get(MaterialBinding source, Waylith.Rendering.Shader shader, bool readOnlyDepth)
    {
        var variants = cache.GetValue(source, static _ => new Variants());
        var previous = readOnlyDepth ? variants.ReadOnlyDepth : variants.Color;
        if (previous != null && previous.DataVersion == source.ResolvedDataVersion && previous.MaterialVersion == source.MaterialVersion
            && previous.Pipeline.Equals(source.PipelineState) && previous.Variant.Equals(source.ShaderVariant)
            && previous.Queue == source.RenderQueue && previous.UsesGlobals == source.UsesGlobals
            && ReferenceEquals(previous.Binding.Shader, shader)) return previous.Binding;
        var state = source.PipelineState;
        if (readOnlyDepth) state = state with { Depth = state.Depth with { Write = false },
            Stencil = state.Stencil with { WriteMask = 0, PassOp = StencilOp.Keep, FailOp = StencilOp.Keep, DepthFailOp = StencilOp.Keep } };
        var variant = source.ShaderVariant;
        var key = new ShaderVariantKey(shader, variant.DynamicKeywordKeyId, variant.DynamicKeywordKey,
            variant.UseTexture, variant.OutlineMrt, variant.Instanced);
        var binding = new MaterialBinding(source.Id, source.MaterialId, source.MaterialVersion, source.DebugName,
            shader, source.MaterialLayout, source.Uniforms, source.Resources, source.RenderQueue,
            key, state, source.WrapModeOverride, source.UsesGlobals);
        binding.UpdateStorageArrays(source.StorageArrays);
        DataVersion(binding) = Interlocked.Decrement(ref nextDataVersion);
        var entry = new Entry(source.ResolvedDataVersion, source.MaterialVersion, source.PipelineState, source.ShaderVariant,
            source.RenderQueue, source.UsesGlobals, binding);
        if (readOnlyDepth) variants.ReadOnlyDepth = entry; else variants.Color = entry;
        return binding;
    }
}
