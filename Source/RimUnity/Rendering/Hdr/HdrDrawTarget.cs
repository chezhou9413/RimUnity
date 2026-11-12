using System;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith.Graphics;
using Waylith.Rendering;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering;

//连续绘制段共用浮点颜色，原生显示颜色段和线性段切换时保留 HDR 与 Alpha。
internal sealed unsafe partial class HdrDrawTarget : IDisposable
{
    private DeviceEx device;
    private MeshBatchRenderer renderer;
    private VkRenderPass pass;
    private NativeTarget[][] colors;
    private NativeTarget[] sceneCopies;
    private VkFramebuffer[][] framebuffers;
    private readonly string name;
    private HdrTransferPass transfer, output;
    private int current;
    private int sceneWidth, sceneHeight;
    private VkFormat sceneColor, sceneDepth;
    private GpuTextureAllocation[] boundDepth;
    internal NativeTarget Result { get; private set; }
    internal HdrDrawTarget(string name) => this.name = name;
    internal (MeshBatchRenderer renderer, VkRenderPass pass) PrepareWarmup(in FrameContext ctx, NativeTarget scene)
    {
        EnsurePipelineResources(in ctx, scene);
        return (renderer, pass);
    }

    //地图先保留已清屏的背景；Bloom 从透明黑开始，不把背景计入亮部。
    internal void Begin(in FrameContext ctx, NativeTarget scene, int frame, bool clear)
    {
        if (colors != null && SceneChanged(in ctx, scene))
        {
            var context = ctx.RenderContext;
            ctx.GraphicsDevice.WaitIdleAndRun(() => RebuildTargets(context));
        }
        if (colors == null) Initialize(in ctx, scene);
        // Native map/preview shaders produce display-space numeric values. A floating
        // or sRGB attachment does not change that shader contract: sRGB sampling
        // cancels the attachment's store encoding. Only an explicitly cleared light
        // buffer starts in linear space. Never infer content encoding from VkFormat.
        current = clear ? 1 : 0;
        transfer.Copy(in ctx, frame, scene, colors[ctx.SubmissionFrameIndex][current], 0, clear);
        Result = null;
    }

    private bool SceneChanged(in FrameContext ctx, NativeTarget scene)
        => scene.Width != sceneWidth || scene.Height != sceneHeight
            || scene.ColorFormat != sceneColor || scene.DepthFormat != sceneDepth
            || colors.Length != ctx.RenderContext.FramesInFlight;

    //只在颜色语义改变时做全屏转换，绘制仍逐项沿用宿主次序。
    internal void Record(in FrameContext ctx, NativeTarget scene, HdrDrawFrame snapshot, int queueMin = int.MinValue, int queueMax = int.MaxValue)
    {
        using var spike = Diagnostics.ParticleSpikeCapture.Measure("HDR.Record");
        using var group = HdrDrawFrame.Shell(snapshot.Frame, MeshDrawFrame.RentList(), MeshDrawFrame.RentList());
        int groupMode = -1;
        for (int i = 0; i < snapshot.Frame.Draws.Count; i++)
        {
            var draw = snapshot.Frame.Draws[i];
            if (draw.RenderQueue < queueMin || draw.RenderQueue >= queueMax) continue;
            int mode = snapshot.IsLinear(i) ? 1 : 0;
            if (groupMode != -1 && mode != groupMode)
            {
                Draw(in ctx, scene, group, groupMode);
                group.PersistentDraws.Clear();
            }
            groupMode = mode;
            group.PersistentDraws.Add(draw);
        }
        if (group.PersistentDraws.Count > 0) Draw(in ctx, scene, group, groupMode);
        Switch(in ctx, snapshot.Frame.FrameNumber, 1);
        Result = colors[ctx.SubmissionFrameIndex][1];
    }

    //输出给场景抓取和原生合成的显示颜色，后处理仍可读取保留的线性 HDR。
    internal void WriteScene(in FrameContext ctx, NativeTarget scene, int frame)
    {
        var copy = sceneCopies[ctx.SubmissionFrameIndex] ??= new NativeTarget(scene.Width, scene.Height,
            scene.ColorFormat, name: name + ".SceneCopy");
        // Restore the numeric values expected by native map grabs and CompositePass,
        // including float/10-bit/sRGB targets. Attachment conversion is separate.
        output.Copy(in ctx, frame, Result, copy, -1);
        HdrSceneCopy.Record(ctx.CommandBuffer, copy, scene);
    }

    private void Switch(in FrameContext ctx, int frame, int mode)
    {
        if (mode == current) return;
        var pair = colors[ctx.SubmissionFrameIndex];
        transfer.Copy(in ctx, frame, pair[current], pair[mode], mode == 1 ? 1 : -1);
        current = mode;
    }

    //深度与模板附件直接使用地图当前帧，保留原材质的测试、写入和混合因子。
    private void Draw(in FrameContext ctx, NativeTarget scene, MeshDrawFrame group, int mode)
    {
        Switch(in ctx, group.FrameNumber, mode);
        int frame = ctx.SubmissionFrameIndex;
        var color = colors[frame][mode];
        var command = ctx.CommandBuffer;
        var begin = new VkRenderPassBeginInfo
        {
            RenderPass = pass, Framebuffer = framebuffers[frame][mode], RenderArea = new VkRect2D(color.Extent)
        };
        command.BeginRenderPass(in begin, VkSubpassContents.Inline);
        var viewport = new VkViewport { Width = scene.Width, Height = scene.Height, MinDepth = 0, MaxDepth = 1 };
        var scissor = new VkRect2D(color.Extent);
        command.SetViewport(0, new ReadOnlySpan<VkViewport>(in viewport));
        command.SetScissor(0, new ReadOnlySpan<VkRect2D>(in scissor));
        var context = new FrameContext(ctx.RenderContext, ctx.Resources, command, pass, framebuffers[frame][mode], color.Extent,
            VkSampleCountFlags._1Bit, color.ColorFormat, scene.DepthFormat);
        //所有项放在持久列表中，避免同帧分段提交被宿主的动态列表缓存替换。
        renderer.InvalidateCommandBufferBindings();
        renderer.RecordPass(in context, group, HdrDrawFrame.MapPass, diagnosticPrefix: name);
        command.EndRenderPass();
        color.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
    }

    private void Initialize(in FrameContext ctx, NativeTarget scene)
    {
        EnsurePipelineResources(in ctx, scene);
        transfer = new HdrTransferPass(name + ".Transfer"); output = new HdrTransferPass(name + ".Output");
        sceneWidth = scene.Width; sceneHeight = scene.Height;
        sceneColor = scene.ColorFormat;
        colors = new NativeTarget[ctx.RenderContext.FramesInFlight][];
        sceneCopies = new NativeTarget[colors.Length];
        framebuffers = new VkFramebuffer[colors.Length][];
        boundDepth = new GpuTextureAllocation[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = new NativeTarget[2]; framebuffers[i] = new VkFramebuffer[2];
            for (int mode = 0; mode < 2; mode++)
                colors[i][mode] = new NativeTarget(scene.Width, scene.Height, VkFormat.R16G16B16A16SFloat, name: $"{name}.{i}.{mode}");
        }
    }

    //Prewarming must not allocate full-resolution color targets or discard its warmed renderer later.
    private void EnsurePipelineResources(in FrameContext ctx, NativeTarget scene)
    {
        if (!pass.IsNull())
        {
            if (sceneDepth == scene.DepthFormat) return;
            var context = ctx.RenderContext;
            ctx.GraphicsDevice.WaitIdleAndRun(() => RebuildTargets(context));
        }
        device = ctx.Device;
        var newPass = CreatePass(device, scene.DepthFormat);
        try
        {
            renderer ??= new MeshBatchRenderer(ctx.GraphicsDevice, ctx.RenderContext);
        }
        catch
        {
            device.DestroyRenderPass(newPass, device.VkAllocator);
            throw;
        }
        pass = newPass;
        sceneDepth = scene.DepthFormat;
    }

    //各在途帧的深度视图不同，等到实际使用该帧时绑定。
    internal void BindDepth(in FrameContext ctx, NativeTarget scene)
    {
        int frame = ctx.SubmissionFrameIndex;
        VkImageView* views = stackalloc VkImageView[2];
        var depth = scene.Depth;
        views[1] = depth?.ImageView ?? default;
        bool depthChanged = !ReferenceEquals(boundDepth[frame], depth);
        for (int mode = 0; mode < 2; mode++)
        {
            ref var framebuffer = ref framebuffers[frame][mode];
            if (!framebuffer.IsNull())
            {
                if (!depthChanged) continue;
                device.DestroyFramebuffer(framebuffer, device.VkAllocator);
                framebuffer = default;
            }
            views[0] = colors[frame][mode].ColorView;
            framebuffer = device.CreateFramebuffer(new VkFramebufferCreateInfo
            { RenderPass = pass, AttachmentCount = scene.HasDepth ? 2 : 1, Attachments = views, Width = scene.Width, Height = scene.Height, Layers = 1 }, device.VkAllocator);
        }
        boundDepth[frame] = depth;
    }

    //窗口重建沿用宿主的网格渲染器生命周期，附件在 GPU 空闲后单独释放。
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
        transfer?.Dispose(); transfer = null; output?.Dispose(); output = null;
        if (framebuffers != null) foreach (var frame in framebuffers) foreach (var framebuffer in frame)
            if (!framebuffer.IsNull()) device.DestroyFramebuffer(framebuffer, device.VkAllocator);
        if (colors != null) foreach (var pair in colors) foreach (var color in pair) color.Dispose();
        if (sceneCopies != null) foreach (var copy in sceneCopies) copy?.Dispose();
        if (!pass.IsNull()) device.DestroyRenderPass(pass, device.VkAllocator);
        pass = default; colors = null; sceneCopies = null; framebuffers = null; boundDepth = null; Result = null;
    }
}
