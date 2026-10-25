using Brutal.Memory;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith.Graphics;
using Waylith.Rendering;

// Separate host frame-resource/descriptor slots; tests enqueue every slot before
// waiting, rather than serializing each color operation with a fence.
internal sealed class TestRenderContext(GraphicsDevice graphics, VkCommandPool pool) : IRenderContext
{
    private RenderTexture target;
    private VkRenderPass pass;
    internal int Slot;
    public GraphicsDevice GraphicsDevice => graphics;
    public DeviceEx Device => graphics.Device;
    public VkSampleCountFlags SampleCount => VkSampleCountFlags._1Bit;
    public VkFormat ColorFormat => target.ColorFormat;
    public VkFormat DepthFormat => target.DepthFormat;
    public int FrameIndex => Slot;
    public int SubmissionFrameIndex => Slot;
    public int FramesInFlight => 3;
    public int ImageCount => 3;
    public FrameResources[] FrameResources { get; } = Enumerable.Range(0, 3)
        .Select(_ => new FrameResources(graphics.Device) { Allocator = graphics.Device.VkAllocator }).ToArray();
    internal void SetTarget(RenderTexture texture, VkRenderPass renderPass) { target = texture; pass = renderPass; }
    public VkRenderPass GetRenderPass() => pass;
    public VkExtent2D GetExtent() => target.Extent;
    public Device GetDevice() => Device;
    public VkCommandPool GetCommandPool() => pool;
    public void PollEvents() => throw new NotSupportedException();
    public FrameAcquireResult TryAcquireNextFrame() => throw new NotSupportedException();
    public FrameResult TrySubmitFrame() => throw new NotSupportedException();
    public FrameResult TrySubmitFrame(Span<VkSemaphore> waits, Span<VkPipelineStageFlags> stages, Span<VkSemaphore> signals) => throw new NotSupportedException();
    public PtrOwner<VkPipelineViewportStateCreateInfo> GetViewportState() => throw new NotSupportedException();
    public void Rebuild() => throw new NotSupportedException();
    public void Dispose() { }
}
