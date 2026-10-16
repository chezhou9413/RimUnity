using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Brutal.VulkanApi;
using Waylith.Graphics;
using Waylith.Rendering;

namespace RimUnity.ScreenEfx;

//The host exposes encoder capacity arguments, but MeshBatchRenderer hardcodes them. Only this
//synchronous construction scope overrides those arguments; other renderers retain native defaults.
internal static class EfxRendererFactory
{
    private static readonly AccessTools.FieldRef<MeshBatchRenderer, MeshPipelineCache> PipelineCache = AccessTools.FieldRefAccess<MeshBatchRenderer, MeshPipelineCache>("pipelines");
    private static readonly AccessTools.FieldRef<MeshBatchRenderer, VkDescriptorSetLayout> FrameLayout = AccessTools.FieldRefAccess<MeshBatchRenderer, VkDescriptorSetLayout>("frameDescriptorSetLayout");
    private static readonly AccessTools.FieldRef<MeshBatchRenderer, VkDescriptorSetLayout> GlobalLayout = AccessTools.FieldRefAccess<MeshBatchRenderer, VkDescriptorSetLayout>("globalTextureDescriptorSetLayout");

    internal static void Warm(MeshBatchRenderer renderer, EfxPass effect, VkRenderPass pass)
    {
        var frameLayout = FrameLayout(renderer); var globalLayout = GlobalLayout(renderer);
        var state = effect.Binding.PipelineState;
        var pipeline = PipelineCache(renderer).GetOrCreatePipeline(in frameLayout, in globalLayout, effect.Binding,
            effect.Mesh.Layout, effect.Mesh.Topology, in state, pass, VkSampleCountFlags._1Bit, 1, 0, "RimUnity.ScreenEfx.Warmup");
        if (pipeline.IsNull()) throw new InvalidOperationException("The host rejected the screen-effect warmup pipeline.");
    }
    [ThreadStatic] private static EfxBufferBudget? current;
    private static readonly Type encoderType = typeof(MeshBatchRenderer).Assembly.GetType("Waylith.Rendering.VulkanMeshDrawEncoder");
    private static readonly ConstructorInfo encoderConstructor = encoderType == null ? null : AccessTools.Constructor(encoderType,
        new[] { typeof(Brutal.VulkanApi.Abstractions.DeviceEx), typeof(Brutal.VulkanApi.Vma.VmaAllocator),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) });
    private static readonly Type cacheType = AccessTools.Inner(typeof(MeshBatchRenderer), "PersistentBufferCache");
    private static readonly ConstructorInfo cacheConstructor = cacheType == null ? null : AccessTools.Constructor(cacheType,
        new[] { typeof(Brutal.VulkanApi.Abstractions.DeviceEx), typeof(Brutal.VulkanApi.Vma.VmaAllocator), typeof(string), typeof(int),
            typeof(int), typeof(Brutal.VulkanApi.VkBufferUsageFlags), typeof(GpuMemoryCategory) });

    internal static MeshBatchRenderer Create(GraphicsDevice graphics, IRenderContext context, EfxBufferBudget budget)
    {
        if (!HooksInstalled)
            throw new NotSupportedException("The host's Clip renderer capacity hooks are unavailable; screen effects were safely disabled.");
        using (Enter(budget)) return new MeshBatchRenderer(graphics, context);
    }

    internal static bool HooksInstalled => Hooked(encoderConstructor, typeof(EncoderPatch)) && Hooked(cacheConstructor, typeof(CachePatch));

    private static bool Hooked(MethodBase target, Type patch)
    {
        if (target == null) return false;
        var info = Harmony.GetPatchInfo(target);
        if (info == null) return false;
        var method = AccessTools.Method(patch, "Prefix");
        foreach (var item in info.Prefixes) if (item.PatchMethod == method) return true;
        return false;
    }

    internal static IDisposable Enter(EfxBufferBudget budget) => new Scope(budget);
    private sealed class Scope : IDisposable
    {
        private readonly EfxBufferBudget? previous;
        private bool disposed;
        internal Scope(EfxBufferBudget budget) { previous = current; current = budget; }
        public void Dispose() { if (disposed) return; disposed = true; current = previous; }
    }

    internal static void ConfigureEncoder(ref int uniformBufferAlignment, ref int descriptorSetCapacity,
        ref int uniformBufferCapacity, ref int instanceBufferCapacity, ref int storageBufferAlignment,
        ref int storageBufferCapacity, ref int indirectBufferCapacity)
    {
        if (current is not EfxBufferBudget budget) return;
        uniformBufferAlignment = budget.UniformAlignment; descriptorSetCapacity = budget.DescriptorSets;
        uniformBufferCapacity = budget.UniformBytes; instanceBufferCapacity = EfxBufferBudget.InstanceBytes;
        storageBufferAlignment = budget.StorageAlignment; storageBufferCapacity = budget.StorageBytes;
        indirectBufferCapacity = EfxBufferBudget.IndirectBytes;
    }

    internal static void ConfigureCache(string label, ref int capacityBytes, ref int alignment)
    {
        if (current is not EfxBufferBudget budget) return;
        if (label == "PersistentMaterialUniforms") { capacityBytes = EfxBufferBudget.PersistentUniformBytes; alignment = budget.UniformAlignment; }
        else if (label == "PersistentMaterialStorage") { capacityBytes = budget.PersistentStorageBytes; alignment = budget.StorageAlignment; }
    }

    [HarmonyPatch]
    private static class EncoderPatch
    {
        private static bool Prepare() => encoderConstructor != null;
        private static MethodBase TargetMethod() => encoderConstructor;
        private static void Prefix(ref int uniformBufferAlignment, ref int descriptorSetCapacity,
            ref int uniformBufferCapacity, ref int instanceBufferCapacity, ref int storageBufferAlignment,
            ref int storageBufferCapacity, ref int indirectBufferCapacity)
            => ConfigureEncoder(ref uniformBufferAlignment, ref descriptorSetCapacity, ref uniformBufferCapacity,
                ref instanceBufferCapacity, ref storageBufferAlignment, ref storageBufferCapacity, ref indirectBufferCapacity);
    }

    [HarmonyPatch]
    private static class CachePatch
    {
        private static bool Prepare() => cacheConstructor != null;
        private static MethodBase TargetMethod() => cacheConstructor;
        private static void Prefix(string label, ref int capacityBytes, ref int alignment) => ConfigureCache(label, ref capacityBytes, ref alignment);
    }
}
