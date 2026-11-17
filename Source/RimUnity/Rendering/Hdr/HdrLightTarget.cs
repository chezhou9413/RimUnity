using System;
using System.Collections.Generic;
using System.IO;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using HarmonyLib;
using Waylith.Graphics;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering;

//Experiment 光缓冲：宿主场景不动，线性特效画进 16F，再按段合成回场景。
internal sealed unsafe class HdrLightTarget : IDisposable
{
    private readonly string name;
    private DeviceEx device;
    private MeshBatchRenderer renderer;
    private VkRenderPass lightPass, compositePass;
    private NativeTarget[] lights, snapshots;
    private VkFramebuffer[] lightFramebuffers;
    //同帧的多个切片可引用同一附件；已记录的 framebuffer 保留到安全的目标重建阶段。
    private Dictionary<VkImageView, VkFramebuffer>[] compositeFramebuffers;
    private GpuTextureAllocation[] boundDepth;
    private CompositeInline composite;
    private int sceneWidth, sceneHeight;
    private VkFormat sceneColor, sceneDepth;

    internal HdrLightTarget(string name) => this.name = name;

    internal (MeshBatchRenderer renderer, VkRenderPass pass) PrepareWarmup(in FrameContext ctx, NativeTarget scene)
    {
        EnsurePipelineResources(in ctx, scene);
        return (renderer, lightPass);
    }

    //只合成一个连续光段；中间的宿主绘制必须先完成，再处理下一光段。
    internal void RecordLights(in FrameContext ctx, NativeTarget scene, MeshDrawFrame frame, List<FinalDrawItem> draws)
    {
        if (draws.Count == 0) return;
        if (lights != null && SceneChanged(in ctx, scene))
        {
            var context = ctx.RenderContext;
            ctx.GraphicsDevice.WaitIdleAndRun(() => RebuildTargets(context));
        }
        if (lights == null) Initialize(in ctx, scene);
        BindDepth(in ctx, scene);
        int slot = ctx.SubmissionFrameIndex;
        SnapshotScene(ctx.CommandBuffer, scene, snapshots[slot]);
        HdrSceneCopy.Clear(ctx.CommandBuffer, lights[slot]);
        DrawLights(in ctx, scene, frame, draws);
        Composite(in ctx, scene, slot);
    }

    //只画一次排在光之后的外壳（夜晚/迷雾等），压在光上且不重复压暗底层。
    internal void DrawShellItems(in FrameContext ctx, PassAttachment attachment, MeshBatchRenderer hostRenderer,
        byte hostPassId, MeshDrawFrame meta, List<FinalDrawItem> draws)
    {
        if (draws == null || draws.Count == 0) return;
        using var part = HdrDrawFrame.Shell(meta, MeshDrawFrame.RentList(), MeshDrawFrame.RentList());
        try
        {
            foreach (var draw in draws) part.PersistentDraws.Add(draw);
            var target = attachment.MakeFrameContext(in ctx);
            var begin = new VkRenderPassBeginInfo
            {
                RenderPass = target.RenderPass, Framebuffer = target.Framebuffer, RenderArea = new VkRect2D(target.Extent)
            };
            ctx.CommandBuffer.BeginRenderPass(in begin, VkSubpassContents.Inline);
            try
            {
                var viewport = new VkViewport { Width = target.Extent.Width, Height = target.Extent.Height, MinDepth = 0, MaxDepth = 1 };
                var scissor = new VkRect2D(target.Extent);
                ctx.CommandBuffer.SetViewport(0, new ReadOnlySpan<VkViewport>(in viewport));
                ctx.CommandBuffer.SetScissor(0, new ReadOnlySpan<VkRect2D>(in scissor));
                hostRenderer.RecordPass(in target, part, hostPassId, null, null, int.MinValue, int.MaxValue, -1, true);
            }
            finally { ctx.CommandBuffer.EndRenderPass(); }
        }
        finally { part.PersistentDraws.Clear(); }
    }

    private bool SceneChanged(in FrameContext ctx, NativeTarget scene)
        => scene.Width != sceneWidth || scene.Height != sceneHeight
            || scene.ColorFormat != sceneColor || scene.DepthFormat != sceneDepth
            || lights.Length != ctx.RenderContext.FramesInFlight;

    private void SnapshotScene(CommandBuffer command, NativeTarget scene, NativeTarget snapshot)
        => BloomPreviewTransfer.Copy(command, scene.Color, snapshot.Color, scene.Width, scene.Height);

    private void DrawLights(in FrameContext ctx, NativeTarget scene, MeshDrawFrame frame, List<FinalDrawItem> draws)
    {
        using var group = HdrDrawFrame.Shell(frame, MeshDrawFrame.RentList(), MeshDrawFrame.RentList());
        group.PersistentDraws.AddRange(draws);
        if (group.PersistentDraws.Count == 0) return;

        int slot = ctx.SubmissionFrameIndex;
        var light = lights[slot];
        var depth = scene.Depth;
        VkImageLayout depthRestore = default;
        if (depth != null)
        {
            depthRestore = depth.CurrentLayout;
            if (depthRestore != VkImageLayout.DepthStencilAttachmentOptimal)
                TransitionDepth(ctx.CommandBuffer, depth, depthRestore, VkImageLayout.DepthStencilAttachmentOptimal);
        }
        try
        {
            var beginInfo = new VkRenderPassBeginInfo
            {
                RenderPass = lightPass, Framebuffer = lightFramebuffers[slot], RenderArea = new VkRect2D(light.Extent)
            };
            ctx.CommandBuffer.BeginRenderPass(in beginInfo, VkSubpassContents.Inline);
            var viewport = new VkViewport { Width = scene.Width, Height = scene.Height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new VkRect2D(light.Extent);
            ctx.CommandBuffer.SetViewport(0, new ReadOnlySpan<VkViewport>(in viewport));
            ctx.CommandBuffer.SetScissor(0, new ReadOnlySpan<VkRect2D>(in scissor));
            var context = new FrameContext(ctx.RenderContext, ctx.Resources, ctx.CommandBuffer, lightPass,
                lightFramebuffers[slot], light.Extent, VkSampleCountFlags._1Bit, light.ColorFormat, scene.DepthFormat);
            renderer.InvalidateCommandBufferBindings();
            renderer.RecordPass(in context, group, HdrDrawFrame.MapPass, diagnosticPrefix: name);
            ctx.CommandBuffer.EndRenderPass();
            light.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
        }
        finally
        {
            if (depth != null && depthRestore != VkImageLayout.DepthStencilAttachmentOptimal)
                TransitionDepth(ctx.CommandBuffer, depth, VkImageLayout.DepthStencilAttachmentOptimal, depthRestore);
            group.PersistentDraws.Clear();
        }
    }

    private void Composite(in FrameContext ctx, NativeTarget scene, int slot)
    {
        EnsureComposite(in ctx, scene);
        var begin = new VkRenderPassBeginInfo
        {
            RenderPass = compositePass, Framebuffer = BindCompositeFramebuffer(slot, scene), RenderArea = new VkRect2D(scene.Extent)
        };
        ctx.CommandBuffer.BeginRenderPass(in begin, VkSubpassContents.Inline);
        try
        {
            composite.SetInput(0, snapshots[slot]);
            composite.SetInput(1, lights[slot]);
            composite.RecordInline(in ctx, compositePass, VkSampleCountFlags._1Bit);
        }
        finally { ctx.CommandBuffer.EndRenderPass(); }
        scene.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
    }

    private void EnsureComposite(in FrameContext ctx, NativeTarget scene)
    {
        if (!compositePass.IsNull()) return;
        device = ctx.Device;
        composite ??= new CompositeInline(name + ".Composite");
        compositePass = CreateCompositePass(device, scene.ColorFormat);
        compositeFramebuffers = new Dictionary<VkImageView, VkFramebuffer>[ctx.RenderContext.FramesInFlight];
        for (int i = 0; i < compositeFramebuffers.Length; i++)
            compositeFramebuffers[i] = new();
    }

    private VkFramebuffer BindCompositeFramebuffer(int slot, NativeTarget scene)
    {
        var cache = compositeFramebuffers[slot];
        var view = scene.ColorView;
        if (cache.TryGetValue(view, out var framebuffer)) return framebuffer;
        VkImageView* views = stackalloc VkImageView[1];
        views[0] = view;
        framebuffer = device.CreateFramebuffer(new VkFramebufferCreateInfo
        {
            RenderPass = compositePass, AttachmentCount = 1, Attachments = views,
            Width = scene.Width, Height = scene.Height, Layers = 1
        }, device.VkAllocator);
        cache.Add(view, framebuffer);
        return framebuffer;
    }

    private void Initialize(in FrameContext ctx, NativeTarget scene)
    {
        EnsurePipelineResources(in ctx, scene);
        sceneWidth = scene.Width; sceneHeight = scene.Height;
        sceneColor = scene.ColorFormat; sceneDepth = scene.DepthFormat;
        int count = ctx.RenderContext.FramesInFlight;
        lights = new NativeTarget[count];
        snapshots = new NativeTarget[count];
        lightFramebuffers = new VkFramebuffer[count];
        boundDepth = new GpuTextureAllocation[count];
        for (int i = 0; i < count; i++)
        {
            lights[i] = new NativeTarget(scene.Width, scene.Height, VkFormat.R16G16B16A16SFloat, name: $"{name}.Light.{i}");
            snapshots[i] = new NativeTarget(scene.Width, scene.Height, scene.ColorFormat, name: $"{name}.Snapshot.{i}");
        }
        EnsureComposite(in ctx, scene);
    }

    private void EnsurePipelineResources(in FrameContext ctx, NativeTarget scene)
    {
        if (!lightPass.IsNull())
        {
            if (sceneDepth == scene.DepthFormat) return;
            var context = ctx.RenderContext;
            ctx.GraphicsDevice.WaitIdleAndRun(() => RebuildTargets(context));
        }
        device = ctx.Device;
        var created = HdrDrawTarget.CreatePass(device, scene.DepthFormat);
        try { renderer ??= new MeshBatchRenderer(ctx.GraphicsDevice, ctx.RenderContext); }
        catch { device.DestroyRenderPass(created, device.VkAllocator); throw; }
        lightPass = created;
        sceneDepth = scene.DepthFormat;
    }

    private void BindDepth(in FrameContext ctx, NativeTarget scene)
    {
        int frame = ctx.SubmissionFrameIndex;
        VkImageView* views = stackalloc VkImageView[2];
        var depth = scene.Depth;
        views[1] = depth?.ImageView ?? default;
        bool depthChanged = !ReferenceEquals(boundDepth[frame], depth);
        ref var framebuffer = ref lightFramebuffers[frame];
        if (!framebuffer.IsNull())
        {
            if (!depthChanged) return;
            device.DestroyFramebuffer(framebuffer, device.VkAllocator);
            framebuffer = default;
        }
        views[0] = lights[frame].ColorView;
        framebuffer = device.CreateFramebuffer(new VkFramebufferCreateInfo
        {
            RenderPass = lightPass, AttachmentCount = scene.HasDepth ? 2 : 1, Attachments = views,
            Width = scene.Width, Height = scene.Height, Layers = 1
        }, device.VkAllocator);
        boundDepth[frame] = depth;
    }

    private static void TransitionDepth(CommandBuffer command, GpuTextureAllocation depth,
        VkImageLayout from, VkImageLayout to)
    {
        var aspect = DepthAspect(depth.Format);
        var barrier = new VkImageMemoryBarrier
        {
            Image = depth.Image, OldLayout = from, NewLayout = to,
            SrcAccessMask = VkAccessFlags.DepthStencilAttachmentReadBit | VkAccessFlags.DepthStencilAttachmentWriteBit
                | VkAccessFlags.ShaderReadBit,
            DstAccessMask = VkAccessFlags.DepthStencilAttachmentReadBit | VkAccessFlags.DepthStencilAttachmentWriteBit
                | VkAccessFlags.ShaderReadBit,
            SrcQueueFamilyIndex = -1, DstQueueFamilyIndex = -1,
            SubresourceRange = new VkImageSubresourceRange { AspectMask = aspect, LevelCount = 1, LayerCount = 1 }
        };
        command.PipelineBarrier(
            VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit | VkPipelineStageFlags.FragmentShaderBit,
            VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit | VkPipelineStageFlags.FragmentShaderBit,
            VkDependencyFlags.None, default, default, new ReadOnlySpan<VkImageMemoryBarrier>(in barrier));
        depth.SetMipLayout(0, to);
    }

    private static VkImageAspectFlags DepthAspect(VkFormat format) => format switch
    {
        VkFormat.D24UNormS8UInt or VkFormat.D32SFloatS8UInt => VkImageAspectFlags.DepthBit | VkImageAspectFlags.StencilBit,
        _ => VkImageAspectFlags.DepthBit,
    };

    private static VkRenderPass CreateCompositePass(DeviceEx device, VkFormat colorFormat)
    {
        VkAttachmentDescription* attachments = stackalloc VkAttachmentDescription[1];
        attachments[0] = new VkAttachmentDescription
        {
            Format = colorFormat, Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Load, StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare, StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.ShaderReadOnlyOptimal, FinalLayout = VkImageLayout.ShaderReadOnlyOptimal
        };
        var colorRef = new VkAttachmentReference { Attachment = 0, Layout = VkImageLayout.ColorAttachmentOptimal };
        var subpass = new VkSubpassDescription
        {
            PipelineBindPoint = VkPipelineBindPoint.Graphics, ColorAttachmentCount = 1, ColorAttachments = &colorRef
        };
        VkSubpassDependency* dependencies = stackalloc VkSubpassDependency[2];
        dependencies[0] = new VkSubpassDependency
        {
            SrcSubpass = -1, DstSubpass = 0,
            SrcStageMask = VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.TransferBit,
            DstStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = VkAccessFlags.ShaderReadBit | VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.TransferWriteBit,
            DstAccessMask = VkAccessFlags.ColorAttachmentReadBit | VkAccessFlags.ColorAttachmentWriteBit
        };
        dependencies[1] = new VkSubpassDependency
        {
            SrcSubpass = 0, DstSubpass = -1,
            SrcStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = VkPipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = VkAccessFlags.ShaderReadBit
        };
        return device.CreateRenderPass(new VkRenderPassCreateInfo
        {
            AttachmentCount = 1, Attachments = attachments, SubpassCount = 1, Subpasses = &subpass,
            DependencyCount = 2, Dependencies = dependencies
        }, device.VkAllocator);
    }

    internal void RebuildTargets(IRenderContext context)
    {
        renderer?.OnRenderContextRebuilt(context);
        ReleaseTargets();
    }

    public void Dispose()
    {
        renderer?.Dispose(); renderer = null;
        ReleaseTargets();
    }

    private void ReleaseTargets()
    {
        composite?.Dispose(); composite = null;
        if (lightFramebuffers != null)
            foreach (var framebuffer in lightFramebuffers)
                if (!framebuffer.IsNull()) device.DestroyFramebuffer(framebuffer, device.VkAllocator);
        if (compositeFramebuffers != null)
            foreach (var cache in compositeFramebuffers)
                if (cache != null)
                    foreach (var framebuffer in cache.Values)
                        if (!framebuffer.IsNull()) device.DestroyFramebuffer(framebuffer, device.VkAllocator);
        if (lights != null) foreach (var light in lights) light?.Dispose();
        if (snapshots != null) foreach (var snapshot in snapshots) snapshot?.Dispose();
        if (!lightPass.IsNull()) device.DestroyRenderPass(lightPass, device.VkAllocator);
        if (!compositePass.IsNull()) device.DestroyRenderPass(compositePass, device.VkAllocator);
        lightPass = default; compositePass = default;
        lights = null; snapshots = null; lightFramebuffers = null; compositeFramebuffers = null; boundDepth = null;
    }

    //写入颜色、保留目标 Alpha；无光像素由 shader discard。
    private sealed class CompositeInline : PostEffectPass
    {
        private delegate void InlineInitializer(PostEffectPass owner, in FrameContext ctx, VkRenderPass pass, VkSampleCountFlags samples);
        private static readonly InlineInitializer InitializeInline =
            AccessTools.Method(typeof(PostEffectPass), "EnsureInline").CreateDelegate<InlineInitializer>();

        internal CompositeInline(string name) : base(name,
            Path.Combine(RimUnityMod.ShaderDirectory, "HdrLightComposite.frag.glsl"),
            new[] { 0, 1 },
            new BlendState(true, BlendFactor.One, BlendFactor.Zero, BlendOp.Add, BlendFactor.Zero, BlendFactor.One, BlendOp.Add),
            pushConstantSize: 0, writeDepth: false)
        { }

        internal void WarmInline(in FrameContext ctx, VkRenderPass pass)
            => InitializeInline(this, in ctx, pass, VkSampleCountFlags._1Bit);
    }
}
