using System;
using System.Collections.Generic;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith;
using Waylith.Graphics;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering;

//离屏相机提供场景和深度，亮部提取与模糊全部复用 BloomPipeline。
internal sealed class BloomPreviewRenderer : IDisposable
{
    private readonly EngineApplication app;
    private readonly GraphicsDevice graphics;
    private readonly DeviceEx device;
    private readonly BloomPreviewContext context;
    private readonly BloomPipeline bloom = new(null);
    private readonly HdrDrawTarget sceneDraw = new("RimUnity.Preview.Scene");
    private readonly VkCommandPool pool;
    private readonly CommandBuffer command;
    private readonly VkFence fence;
    private NativeTarget scene, result;
    private VkRenderPass scenePass;
    private int frameNumber;
    private bool disposed;

    //独立命令池与栅栏避免预览和地图的在途帧竞争。
    internal BloomPreviewRenderer()
    {
        app = EngineApplication.Instance;
        graphics = app.GraphicsDevice; device = graphics.Device;
        pool = device.CreateCommandPool(new VkCommandPoolCreateInfo
        { QueueFamilyIndex = graphics.GraphicsFamilyIndex, Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit }, device.VkAllocator);
        command = device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = pool, Level = VkCommandBufferLevel.Primary });
        fence = device.CreateFence(new VkFenceCreateInfo(), device.VkAllocator);
        context = new BloomPreviewContext(graphics, pool);
        app.BeforeGraphicsShutdown += Shutdown;
    }

    //完整背景预览采用与地图相同的色彩与图层筛选规则。
    internal void Render(UnityEngine.RenderTexture target, IReadOnlyList<MeshDrawCommand> commands, CameraView view,
        UnityEngine.Rect viewport, UnityEngine.Color? background, int mask)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (viewport != new UnityEngine.Rect(0, 0, 1, 1) || !background.HasValue || background.Value.a != 1)
            throw new InvalidOperationException("Bloom 预览需要全尺寸视口与不透明背景。");
        if (!target.Create() || target.GpuTexture == null) throw new InvalidOperationException("无法创建 Bloom 预览目标。");
        GpuTextureService.FlushPendingTextureCommands();
        graphics.MeshUploads.DrainPendingUploads();
        EnsureTargets(target);
        int frame = ++frameNumber;
        using var captured = Capture(commands, view, frame);
        using var sceneFrame = new HdrDrawFrame(captured);
        var settings = RimUnityMod.Bloom.Snapshot() with
        { FrameNumber = frame, Draws = new HdrDrawFrame(captured, mask, readOnlyDepth: true) };
        using (settings)
        {
            var ctx = new FrameContext(context, context.FrameResources[0], command, scenePass, scene.GetFramebuffer(scenePass),
                scene.Extent, VkSampleCountFlags._1Bit, scene.ColorFormat, scene.DepthFormat);
            command.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
            Clear(in ctx, background.Value);
            scene.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
            sceneDraw.Begin(in ctx, scene, frame, clear: false);
            sceneDraw.BindDepth(in ctx, scene);
            sceneDraw.Record(in ctx, scene, sceneFrame);
            bloom.RenderPreview(in ctx, scene, sceneDraw.Result, result, settings);
            BloomPreviewTransfer.Copy(command, result.Color, target.GpuTexture, target.width, target.height);
            command.End();
            device.ResetFences(new ReadOnlySpan<VkFence>(in fence));
            var submit = command;
            graphics.SubmitGraphics(default, default, new Span<CommandBuffer>(ref submit), default, fence);
            device.WaitForFence(fence, -1);
        }
    }

    //宿主即时绘制已经固定所有参数；捕获期间不重新求值业务动画。
    private static MeshDrawFrame Capture(IReadOnlyList<MeshDrawCommand> commands, CameraView view, int frame)
    {
        var captured = new MeshDrawFrame(frame, view, MeshDrawFrame.RentList(), MeshDrawFrame.RentList(), System.Numerics.Vector3.UnitY);
        try
        {
            for (int i = 0; i < commands.Count; i++)
            {
                var item = MeshDrawSubmission.DrawItemMaker.TryMake(commands[i], MeshDrawSubmission.ActiveArena,
                    UnityEngine.Shader.GlobalKeywordKey, i, "RimUnity.Preview");
                if (item == null) throw new InvalidOperationException("预览网格无法生成绘制项。");
                captured.Draws.Add(item);
            }
            return captured;
        }
        catch { captured.Dispose(); throw; }
    }

    //场景只清空一次，完整角色随后在共享 HDR 绘制器中写入颜色和深度。
    private static unsafe void Clear(in FrameContext ctx, UnityEngine.Color color)
    {
        VkClearValue* clears = stackalloc VkClearValue[2];
        clears[0] = new VkClearValue { Color = new VkClearColorValue { Float32 = new float4(color.r, color.g, color.b, color.a) } };
        clears[1] = new VkClearValue { DepthStencil = new VkClearDepthStencilValue { Depth = 0 } };
        ctx.CommandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
        { RenderPass = ctx.RenderPass, Framebuffer = ctx.Framebuffer, RenderArea = new VkRect2D(ctx.Extent), ClearValueCount = 2, ClearValues = clears }, VkSubpassContents.Inline);
        ctx.CommandBuffer.EndRenderPass();
    }

    //保留场景深度供 Bloom 遮挡判断，尺寸变化时先释放引用该深度的附件。
    private void EnsureTargets(UnityEngine.RenderTexture target)
    {
        if (scene != null && scene.Width == target.width && scene.Height == target.height && scene.ColorFormat == target.GpuTexture.Format) return;
        bloom.ReleaseTargets(); sceneDraw.Dispose();
        scene?.Dispose(); result?.Dispose();
        if (!scenePass.IsNull()) device.DestroyRenderPass(scenePass, device.VkAllocator);
        scenePass = PostFxPassHelpers.CreateColorRenderPass(device, target.GpuTexture.Format, VkFormat.D32SFloatS8UInt, VkAttachmentLoadOp.Clear);
        scene = new NativeTarget(target.width, target.height, target.GpuTexture.Format, VkFormat.D32SFloatS8UInt, name: "RimUnity.Preview.Scene");
        result = new NativeTarget(target.width, target.height, target.GpuTexture.Format, name: "RimUnity.Preview.Composite");
        context.SetTarget(scene, scenePass);
    }

    //设备关闭前释放预览，不依赖窗口是否已经关闭。
    private void Shutdown(EngineApplication application) => Dispose();

    //每次提交均已等待栅栏，关闭窗口即可安全回收。
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        app.BeforeGraphicsShutdown -= Shutdown;
        bloom.Dispose(); sceneDraw.Dispose(); scene?.Dispose(); result?.Dispose();
        if (!scenePass.IsNull()) device.DestroyRenderPass(scenePass, device.VkAllocator);
        device.DestroyFence(fence, device.VkAllocator);
        device.DestroyCommandPool(pool, device.VkAllocator);
    }
}
