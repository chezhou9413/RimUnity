using System;
using Brutal.Memory;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith.Graphics;
using Waylith.Rendering;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.ScreenEfx;

//Each instance belongs to one host fence slot. It borrows the command/resources and never submits.
internal sealed class EfxRenderContext : IRenderContext
{
    private IRenderContext host;
    private NativeTarget target;
    private VkRenderPass pass;
    public GraphicsDevice GraphicsDevice => host.GraphicsDevice;
    public DeviceEx Device => host.Device;
    public VkSampleCountFlags SampleCount => VkSampleCountFlags._1Bit;
    public VkFormat ColorFormat => VkFormat.R8G8B8A8UNorm;
    public VkFormat DepthFormat => VkFormat.Undefined;
    public int FrameIndex => 0;
    public int SubmissionFrameIndex => 0;
    public int FramesInFlight => 1;
    public int ImageCount => 1;
    public FrameResources[] FrameResources { get; } = new FrameResources[1];
    internal void Bind(IRenderContext host, FrameResources resources, NativeTarget target, VkRenderPass pass)
    {
        this.host = host; this.target = target; this.pass = pass; FrameResources[0] = resources;
    }
    public VkRenderPass GetRenderPass() => pass;
    public VkExtent2D GetExtent() => target.Extent;
    public Device GetDevice() => Device;
    public VkCommandPool GetCommandPool() => host.GetCommandPool();
    public void PollEvents() => throw new NotSupportedException("Clip contexts do not poll windows.");
    public FrameAcquireResult TryAcquireNextFrame() => throw new NotSupportedException("The host owns Clip frame acquisition.");
    public FrameResult TrySubmitFrame() => throw new NotSupportedException("The host owns Clip submission.");
    public FrameResult TrySubmitFrame(Span<VkSemaphore> waits, Span<VkPipelineStageFlags> stages, Span<VkSemaphore> signals)
        => throw new NotSupportedException("The host owns Clip submission.");
    public PtrOwner<VkPipelineViewportStateCreateInfo> GetViewportState() => throw new NotSupportedException("Clips use a dynamic viewport.");
    public void Rebuild() => throw new NotSupportedException("The host owns Clip rebuilds.");
    public void Dispose() { }
}
