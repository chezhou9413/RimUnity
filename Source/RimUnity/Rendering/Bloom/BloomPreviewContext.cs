
using System;
using Brutal.Memory;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith.Graphics;
using Waylith.Rendering;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering
{
    //预览每次提交都等待完成，固定使用一个资源槽，不读取地图线程的帧编号。
    internal sealed class BloomPreviewContext : IRenderContext
    {
        private readonly VkCommandPool pool;
        private NativeTarget target;
        private VkRenderPass pass;
        public GraphicsDevice GraphicsDevice { get; }
        public DeviceEx Device => GraphicsDevice.Device;
        public VkSampleCountFlags SampleCount => VkSampleCountFlags._1Bit;
        public VkFormat ColorFormat => target.ColorFormat;
        public VkFormat DepthFormat => target.DepthFormat;
        public int FrameIndex => 0;
        public int SubmissionFrameIndex => 0;
        public int FramesInFlight => 1;
        public int ImageCount => 1;
        public FrameResources[] FrameResources { get; }

        //只借用图形设备，资源提交和释放由预览渲染器负责。
        internal BloomPreviewContext(GraphicsDevice graphics, VkCommandPool pool)
        {
            GraphicsDevice = graphics; this.pool = pool;
            FrameResources = new[] { new FrameResources(graphics.Device) { Allocator = graphics.Device.VkAllocator } };
        }
        //尺寸变化时更新离屏目标，不触发窗口重建。
        internal void SetTarget(NativeTarget texture, VkRenderPass renderPass) { target = texture; pass = renderPass; }
        //返回当前离屏通道。
        public VkRenderPass GetRenderPass() => pass;
        //返回当前离屏尺寸。
        public VkExtent2D GetExtent() => target.Extent;
        //网格管线借用同一 Vulkan 设备。
        public Device GetDevice() => Device;
        //命令仅从预览自己的池中分配。
        public VkCommandPool GetCommandPool() => pool;
        //离屏预览没有窗口事件。
        public void PollEvents() => throw new NotSupportedException("离屏预览不处理窗口事件。");
        //离屏预览不获取交换链图像。
        public FrameAcquireResult TryAcquireNextFrame() => throw new NotSupportedException("离屏预览没有交换链。");
        //提交必须经过预览渲染器的专用栅栏。
        public FrameResult TrySubmitFrame() => throw new NotSupportedException("由预览渲染器提交命令。");
        //不接管窗口的信号量。
        public FrameResult TrySubmitFrame(Span<VkSemaphore> waits, Span<VkPipelineStageFlags> stages, Span<VkSemaphore> signals)
            => throw new NotSupportedException("由预览渲染器提交命令。");
        //每次捕获显式记录动态视口。
        public PtrOwner<VkPipelineViewportStateCreateInfo> GetViewportState() => throw new NotSupportedException("离屏预览使用动态视口。");
        //资源尺寸由预览窗口管理。
        public void Rebuild() => throw new NotSupportedException("由预览渲染器重建目标。");
        //上下文本身不拥有 GPU 资源。
        public void Dispose() { }
    }
}

