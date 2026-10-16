using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using Brutal.VulkanApi;
using HarmonyLib;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.ScreenEfx;

//Owned by the existing PostFx feature. Frame slots are reused only after their GPU fences.
internal sealed class EfxGpuPipeline : IDisposable
{
    private readonly WarmablePass scene = new("RimUnity.ScreenEfx.Scene",
        Path.Combine(RimUnityMod.ShaderDirectory, "ScreenEfxScene.frag.glsl"), new[] { 0, 1 }, pushConstantSize: 16);
    private readonly WarmablePass output = new("RimUnity.ScreenEfx.Output",
        Path.Combine(RimUnityMod.ShaderDirectory, "HdrTransfer.frag.glsl"), new[] { 0 }, pushConstantSize: 16);
    private MeshBatchRenderer[] renderers;
    private EfxRenderContext[] contexts;
    private EfxBufferBudget[] budgets;
    private NativeTarget[][] targets;
    private BorrowedTexture[][] inputs;
    private EfxFrame[] retained;
    private List<EfxPass>[] scratch;
    private VkRenderPass pass;
    private Brutal.VulkanApi.Abstractions.DeviceEx device;
    private bool warmed;
    internal NativeTarget Result { get; private set; }

    //Called even on non-map/empty frames to retire a slot without allocating any targets.
    internal void BeginSlot(int index)
    {
        Result = null;
        if (retained == null) return;
        int bytes = 0;
        foreach (var effect in scratch[index]) { effect.ClearReferences(); bytes += effect.BufferBytes; }
        if (bytes > 256 * 1024) scratch[index].Clear();
        retained[index]?.Drop(); retained[index] = null;
    }

    internal void ReleaseIfIdle()
    {
        if (renderers == null) return;
        if (retained != null) foreach (var frame in retained) if (frame != null) return;
        //Keep only small renderer/pipeline caches for a live, loading-warmed clip. Images
        //remain visibility-driven; the final stopped clip releases everything after retirement.
        if (warmed && EfxPlayer.HasPlaying) ReleaseImages();
        else ReleaseTargets();
    }

    internal int PrepareWarmup(in FrameContext ctx, NativeTarget source, EfxFrame frame)
    {
        if (frame == null || frame.Passes.Count == 0) return 0;
        EnsureResources(in ctx);
        scene.Warm(in ctx); output.WarmInline(in ctx);
        int count = 0;
        for (int slot = 0; slot < renderers.Length; slot++)
        {
            //A previous session's submitted frame may still own this slot. Its normal
            //retirement path will prepare it later; loading never destroys in-flight buffers.
            if (retained?[slot] != null) continue;
            PrepareRenderer(in ctx, frame, slot, source);
            foreach (var effect in frame.Passes)
            {
                EfxRendererFactory.Warm(renderers[slot], effect, pass);
                count++;
            }
        }
        warmed = true;
        return count + 2;
    }

    internal void Prepare(in FrameContext ctx, EfxFrame frame, int frameNumber, NativeTarget source,
        NativeTarget hdr, NativeTarget bloom, float intensity, float saturation)
    {
        if (frame == null || frame.Passes.Count == 0 || source == null) return;
        Ensure(in ctx, source);
        PrepareRenderer(in ctx, frame, ctx.SubmissionFrameIndex, targets[ctx.SubmissionFrameIndex][0]);
        frame.Retain(); retained[ctx.SubmissionFrameIndex] = frame;
        var pair = targets[ctx.SubmissionFrameIndex];
        scene.SetInput(0, hdr ?? source); scene.SetInput(1, bloom ?? source);
        bool gamma = source.ColorFormat is VkFormat.R8G8B8A8UNorm or VkFormat.B8G8R8A8UNorm;
        scene.SetPushConstants(new System.Numerics.Vector4(bloom == null ? 0 : intensity, gamma ? 1 : 0, saturation, hdr == null ? 0 : 1));
        scene.Output = pair[0]; scene.Record(in ctx, null);
        int current = 0;
        var identity = System.Numerics.Matrix4x4.Identity;
        using var drawFrame = new MeshDrawFrame(frameNumber, new CameraView(identity, identity, identity, -1),
            MeshDrawFrame.RentList(), MeshDrawFrame.RentList(), System.Numerics.Vector3.Zero);
        int passIndex = 0;
        foreach (var captured in frame.Passes)
        {
            if (captured.Lease.RenderFailed) continue;
            try
            {
                var slot = scratch[ctx.SubmissionFrameIndex];
                if (passIndex == slot.Count && passIndex < 64) slot.Add(new EfxPass());
                var reusable = passIndex < slot.Count ? slot[passIndex] : new EfxPass();
                passIndex++;
                var bound = captured.BindInput(inputs[ctx.SubmissionFrameIndex][current], source.Width, source.Height, reusable);
                try { Draw(in ctx, bound, pair[1 - current], drawFrame); }
                finally { if (passIndex > 64) reusable.ClearReferences(); }
                current = 1 - current;
            }
            catch (Exception error) { EfxPlayer.Report(captured.Lease, error); }
        }
        Result = pair[current];
        if (Diagnostics.RenderDebugReport.Collecting)
        {
            long reserved = 0;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) reserved += budgets[i].ReservedBytes;
            Diagnostics.RenderDebugReport.Stage("ScreenEfx", $"frame={frameNumber}; passes={frame.Passes.Count}; size={source.Width}x{source.Height}; targets={targets.Length * 2}; slot={ctx.SubmissionFrameIndex}; uploadBufferBytes={reserved}");
        }
    }

    private void PrepareRenderer(in FrameContext ctx, EfxFrame frame, int index, NativeTarget target)
    {
        var limits = ctx.GraphicsDevice.PhysicalDevice.GetProperties().Limits;
        var required = EfxBufferBudget.ForFrame(frame, limits.MinUniformBufferOffsetAlignment, limits.MinStorageBufferOffsetAlignment);
        var context = contexts[index] ??= new EfxRenderContext();
        context.Bind(ctx.RenderContext, ctx.Resources, target, pass);
        if (renderers[index] != null && budgets[index].Covers(required)) return;
        //BeginSlot has retired this slot's old GPU work. Other slots keep their renderer and buffers.
        renderers[index]?.Dispose(); renderers[index] = null;
        renderers[index] = EfxRendererFactory.Create(ctx.GraphicsDevice, context, required);
        budgets[index] = required;
    }

    private void Ensure(in FrameContext ctx, NativeTarget source)
    {
        if (targets != null)
        {
            if (targets[0][0].Width != source.Width || targets[0][0].Height != source.Height)
                throw new InvalidOperationException("Screen-effect targets require the host's safe resize callback.");
            return;
        }
        EnsureResources(in ctx);
        try
        {
            int count = ctx.RenderContext.FramesInFlight;
            targets = new NativeTarget[count][]; inputs = new BorrowedTexture[count][]; retained = new EfxFrame[count];
            scratch = new List<EfxPass>[count];
            for (int f = 0; f < count; f++)
            {
                scratch[f] = new List<EfxPass>();
                targets[f] = new NativeTarget[2]; inputs[f] = new BorrowedTexture[2];
                for (int i = 0; i < 2; i++)
                {
                    var target = targets[f][i] = new NativeTarget(source.Width, source.Height, VkFormat.R8G8B8A8UNorm, name: $"RimUnity.ScreenEfx.{f}.{i}");
                    inputs[f][i] = new BorrowedTexture(target);
                }
            }
        }
        catch { ReleaseTargets(); throw; }
    }

    private void EnsureResources(in FrameContext ctx)
    {
        if (renderers != null) return;
        device = ctx.Device;
        pass = PostFxPassHelpers.CreateColorRenderPass(device, VkFormat.R8G8B8A8UNorm);
        int count = ctx.RenderContext.FramesInFlight;
        renderers = new MeshBatchRenderer[count]; contexts = new EfxRenderContext[count]; budgets = new EfxBufferBudget[count];
    }

    private unsafe void Draw(in FrameContext ctx, EfxPass effect, NativeTarget target, MeshDrawFrame frame)
    {
        var draw = effect.CreateDraw();
        //The host caches dynamic Draws once per frame. PersistentDraws are read on every pass.
        frame.PersistentDraws.Add(draw);
        try
        {
            var framebuffer = target.GetFramebuffer(pass);
            ctx.CommandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
            { RenderPass = pass, Framebuffer = framebuffer, RenderArea = new VkRect2D(target.Extent) }, VkSubpassContents.Inline);
            try
            {
                var viewport = new VkViewport { Width = target.Width, Height = target.Height, MinDepth = 0, MaxDepth = 1 };
                var scissor = new VkRect2D(target.Extent);
                ctx.CommandBuffer.SetViewport(0, new ReadOnlySpan<VkViewport>(in viewport));
                ctx.CommandBuffer.SetScissor(0, new ReadOnlySpan<VkRect2D>(in scissor));
                var context = new FrameContext(contexts[ctx.SubmissionFrameIndex], ctx.Resources, ctx.CommandBuffer, pass, framebuffer,
                    target.Extent, VkSampleCountFlags._1Bit, target.ColorFormat, VkFormat.Undefined);
                var renderer = renderers[ctx.SubmissionFrameIndex];
                renderer.InvalidateCommandBufferBindings();
                renderer.RecordPass(in context, frame, 0, diagnosticPrefix: "RimUnity.ScreenEfx");
            }
            finally { ctx.CommandBuffer.EndRenderPass(); }
        }
        finally { frame.PersistentDraws.Clear(); FinalDrawItem.Return(draw); }
        target.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
    }

    internal void Composite(in FrameContext ctx)
    {
        output.SetInput(0, Result); output.SetPushConstants(System.Numerics.Vector4.Zero);
        output.RecordInline(in ctx, ctx.RenderPass, ctx.SampleCount);
    }

    internal void ReleaseTargets()
    {
        Result = null;
        //Renderer descriptors must be retired before releasing sampled views/framebuffers.
        if (renderers != null) foreach (var renderer in renderers) renderer?.Dispose();
        renderers = null; contexts = null; budgets = null;
        ReleaseImages(); warmed = false;
        if (!pass.IsNull()) device.DestroyRenderPass(pass, device.VkAllocator);
        pass = default;
        scene.Output = null; scene.Invalidate(); output.Invalidate();
    }

    private void ReleaseImages()
    {
        Result = null;
        if (scratch != null) foreach (var slot in scratch) if (slot != null) foreach (var effect in slot) effect.ClearReferences();
        scratch = null;
        if (retained != null) foreach (var frame in retained) frame?.Drop();
        retained = null;
        if (inputs != null) foreach (var pair in inputs) if (pair != null) foreach (var input in pair) input?.Dispose();
        inputs = null;
        if (targets != null) foreach (var pair in targets) if (pair != null) foreach (var target in pair) target?.Dispose();
        targets = null;
        scene.Output = null;
        //The RenderTexture overload dereferences texture.Color/Sampler and cannot clear a
        //binding. Clear the borrowed allocation/sampler directly after GPU slot retirement.
        scene.SetInput(0, null, default); scene.SetInput(1, null, default); output.SetInput(0, null, default);
    }
    public void Dispose() { ReleaseTargets(); scene.Dispose(); output.Dispose(); }

    //Use the exact PostEffectPass build path without recording draws or allocating full-screen images.
    private sealed class WarmablePass(string name, string fragment, int[] bindings, int pushConstantSize)
        : PostEffectPass(name, fragment, bindings, pushConstantSize: pushConstantSize)
    {
        private delegate void InlineInitializer(PostEffectPass owner, in FrameContext ctx, VkRenderPass pass, VkSampleCountFlags samples);
        private static readonly InlineInitializer InitializeInline = AccessTools.Method(typeof(PostEffectPass), "EnsureInline").CreateDelegate<InlineInitializer>();
        internal void Warm(in FrameContext ctx) => EnsureInitialized(in ctx, VkFormat.R8G8B8A8UNorm);
        internal void WarmInline(in FrameContext ctx) => InitializeInline(this, in ctx, ctx.RenderPass, ctx.SampleCount);
    }

    private sealed class BorrowedTexture : Waylith.Rendering.Texture
    {
        public override int width { get; set; }
        public override int height { get; set; }
        internal BorrowedTexture(NativeTarget target)
        {
            width = target.Width; height = target.Height; GpuResource = target.Color;
            wrapMode = Waylith.Rendering.TextureWrapMode.Clamp;
        }
        public override void Dispose() => GpuResource = null; //The NativeTarget owns the allocation.
    }
}
