using System;
using System.Runtime.CompilerServices;
using Brutal.VulkanApi;
using Waylith.Graphics;
using Waylith.Rendering;

namespace RimUnity.RenderPipeline;

//渲染线程执行单个自定义步骤时的上下文，提供通用命令录制与提交后主线程回调。
public readonly struct RenderPassContext
{
    private static readonly VkImageSubresourceRange s_colorMip0Range = new()
    {
        AspectMask = VkImageAspectFlags.ColorBit,
        BaseMipLevel = 0,
        LevelCount = 1,
        BaseArrayLayer = 0,
        LayerCount = 1
    };

    private readonly FrameContext _frameContext;
    private readonly Action<Action> _enqueueAfterSubmit;

    public RenderPassEvent Event { get; }
    public int Width => _frameContext.Extent.Width;
    public int Height => _frameContext.Extent.Height;
    public int FrameIndex => _frameContext.FrameIndex;
    public bool CanCopyColorTarget { get; }

    internal RenderPassContext(in FrameContext frameContext, RenderPassEvent passEvent, bool canCopyColorTarget, Action<Action> enqueueAfterSubmit)
    {
        _frameContext = frameContext;
        Event = passEvent;
        CanCopyColorTarget = canCopyColorTarget;
        _enqueueAfterSubmit = enqueueAfterSubmit;
    }

    //在 AfterRendering（主 RenderPass 结束之后）将当前帧交换链颜色缓冲直接 Blit 到已分配的 RenderTexture，无主线程同步等待。
    public bool BlitColorTarget(RenderTexture destination, bool flipY = false, bool linearFilter = false)
    {
        if (Event != RenderPassEvent.AfterRendering || !CanCopyColorTarget || destination == null || destination.Target == null)
        {
            return false;
        }

        GpuTextureAllocation destinationGpu = destination.Target.GpuTexture;
        int sourceWidth = _frameContext.Extent.Width;
        int sourceHeight = _frameContext.Extent.Height;
        int destinationWidth = destination.width;
        int destinationHeight = destination.height;
        if (destinationGpu == null || sourceWidth <= 0 || sourceHeight <= 0 || destinationWidth <= 0 || destinationHeight <= 0)
        {
            return false;
        }

        CommandBuffer commandBuffer = _frameContext.CommandBuffer;
        VkImage colorImage = _frameContext.Resources.ColorImage;
        VkImageLayout finalLayout = _frameContext.RenderContext is RenderContext renderContext
            ? renderContext.FinalLayout
            : VkImageLayout.PresentSrcKHR;
        VkImageLayout destinationOldLayout = destinationGpu.GetMipLayout(0);
        VkAccessFlags destinationSrcAccess = destinationOldLayout == VkImageLayout.Undefined
            ? VkAccessFlags.None
            : VkAccessFlags.ShaderReadBit | VkAccessFlags.TransferWriteBit;

        InlineArray2<VkImageMemoryBarrier> preBarriers = default;
        preBarriers[0] = _CreateBarrier(colorImage, finalLayout, VkImageLayout.TransferSrcOptimal,
            VkAccessFlags.ColorAttachmentWriteBit, VkAccessFlags.TransferReadBit, s_colorMip0Range);
        preBarriers[1] = _CreateBarrier(destinationGpu.Image, destinationOldLayout, VkImageLayout.TransferDstOptimal,
            destinationSrcAccess, VkAccessFlags.TransferWriteBit, s_colorMip0Range);
        commandBuffer.PipelineBarrier(
            VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.TransferBit,
            VkPipelineStageFlags.TransferBit, VkDependencyFlags.None, default, default, preBarriers);

        VkImageBlit region = _BuildBlitRegion(sourceWidth, sourceHeight, destinationWidth, destinationHeight, flipY);
        VkFilter filter = linearFilter || sourceWidth != destinationWidth || sourceHeight != destinationHeight
            ? VkFilter.Linear
            : VkFilter.Nearest;
        commandBuffer.BlitImage(colorImage, VkImageLayout.TransferSrcOptimal,
            destinationGpu.Image, VkImageLayout.TransferDstOptimal, new ReadOnlySpan<VkImageBlit>(in region), filter);

        InlineArray2<VkImageMemoryBarrier> postBarriers = default;
        postBarriers[0] = _CreateBarrier(colorImage, VkImageLayout.TransferSrcOptimal, finalLayout,
            VkAccessFlags.TransferReadBit, VkAccessFlags.None, s_colorMip0Range);
        postBarriers[1] = _CreateBarrier(destinationGpu.Image, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkAccessFlags.TransferWriteBit, VkAccessFlags.ShaderReadBit, s_colorMip0Range);
        commandBuffer.PipelineBarrier(
            VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.BottomOfPipeBit,
            VkDependencyFlags.None, default, default, postBarriers);

        destinationGpu.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
        if (destinationGpu.MipCount <= 1)
        {
            destinationGpu.CurrentLayout = VkImageLayout.ShaderReadOnlyOptimal;
        }
        return true;
    }

    //在 RenderPass 外部（BeforeRendering / BeforePostProcessing / AfterRendering）在两张离屏 RenderTexture 之间录制 Blit。
    public bool Blit(RenderTexture source, RenderTexture destination, bool flipY = false, bool linearFilter = true)
    {
        if (source == null || destination == null || ReferenceEquals(source, destination)
            || source.Target == null || destination.Target == null)
        {
            return false;
        }

        GpuTextureAllocation sourceGpu = source.Target.GpuTexture;
        GpuTextureAllocation destinationGpu = destination.Target.GpuTexture;
        int sourceWidth = source.width;
        int sourceHeight = source.height;
        int destinationWidth = destination.width;
        int destinationHeight = destination.height;
        if (sourceGpu == null || destinationGpu == null || sourceWidth <= 0 || sourceHeight <= 0 || destinationWidth <= 0 || destinationHeight <= 0)
        {
            return false;
        }

        CommandBuffer commandBuffer = _frameContext.CommandBuffer;
        VkImageLayout sourceOldLayout = sourceGpu.GetMipLayout(0);
        VkAccessFlags sourceSrcAccess = sourceOldLayout == VkImageLayout.Undefined
            ? VkAccessFlags.None
            : VkAccessFlags.ShaderReadBit | VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.TransferWriteBit;
        VkImageLayout destinationOldLayout = destinationGpu.GetMipLayout(0);
        VkAccessFlags destinationSrcAccess = destinationOldLayout == VkImageLayout.Undefined
            ? VkAccessFlags.None
            : VkAccessFlags.ShaderReadBit | VkAccessFlags.TransferWriteBit;

        InlineArray2<VkImageMemoryBarrier> preBarriers = default;
        preBarriers[0] = _CreateBarrier(sourceGpu.Image, sourceOldLayout, VkImageLayout.TransferSrcOptimal,
            sourceSrcAccess, VkAccessFlags.TransferReadBit, s_colorMip0Range);
        preBarriers[1] = _CreateBarrier(destinationGpu.Image, destinationOldLayout, VkImageLayout.TransferDstOptimal,
            destinationSrcAccess, VkAccessFlags.TransferWriteBit, s_colorMip0Range);
        commandBuffer.PipelineBarrier(
            VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.TransferBit,
            VkPipelineStageFlags.TransferBit, VkDependencyFlags.None, default, default, preBarriers);

        VkImageBlit region = _BuildBlitRegion(sourceWidth, sourceHeight, destinationWidth, destinationHeight, flipY);
        VkFilter filter = linearFilter || sourceWidth != destinationWidth || sourceHeight != destinationHeight
            ? VkFilter.Linear
            : VkFilter.Nearest;
        commandBuffer.BlitImage(sourceGpu.Image, VkImageLayout.TransferSrcOptimal,
            destinationGpu.Image, VkImageLayout.TransferDstOptimal, new ReadOnlySpan<VkImageBlit>(in region), filter);

        InlineArray2<VkImageMemoryBarrier> postBarriers = default;
        postBarriers[0] = _CreateBarrier(sourceGpu.Image, VkImageLayout.TransferSrcOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkAccessFlags.TransferReadBit, VkAccessFlags.ShaderReadBit, s_colorMip0Range);
        postBarriers[1] = _CreateBarrier(destinationGpu.Image, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkAccessFlags.TransferWriteBit, VkAccessFlags.ShaderReadBit, s_colorMip0Range);
        commandBuffer.PipelineBarrier(
            VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit,
            VkDependencyFlags.None, default, default, postBarriers);

        sourceGpu.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
        if (sourceGpu.MipCount <= 1)
        {
            sourceGpu.CurrentLayout = VkImageLayout.ShaderReadOnlyOptimal;
        }
        destinationGpu.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
        if (destinationGpu.MipCount <= 1)
        {
            destinationGpu.CurrentLayout = VkImageLayout.ShaderReadOnlyOptimal;
        }
        return true;
    }

    //登记一个在当前帧完成 GPU 队列提交后回到主线程执行的回调。
    public void EnqueueMainThreadAfterSubmit(Action callback)
    {
        if (callback == null || _enqueueAfterSubmit == null)
        {
            return;
        }
        _enqueueAfterSubmit(callback);
    }

    private static VkImageMemoryBarrier _CreateBarrier(VkImage image, VkImageLayout oldLayout, VkImageLayout newLayout,
        VkAccessFlags srcAccess, VkAccessFlags dstAccess, VkImageSubresourceRange range)
    {
        return new VkImageMemoryBarrier
        {
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = -1,
            DstQueueFamilyIndex = -1,
            Image = image,
            SubresourceRange = range
        };
    }

    private static VkImageBlit _BuildBlitRegion(int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight, bool flipY)
    {
        VkImageSubresourceLayers layers = new()
        {
            AspectMask = VkImageAspectFlags.ColorBit,
            MipLevel = 0,
            BaseArrayLayer = 0,
            LayerCount = 1
        };
        VkImageBlit blitRegion = new()
        {
            SrcSubresource = layers,
            DstSubresource = layers
        };
        blitRegion.SrcOffsets[0] = new VkOffset3D(0, 0, 0);
        blitRegion.SrcOffsets[1] = new VkOffset3D(sourceWidth, sourceHeight, 1);
        blitRegion.DstOffsets[0] = new VkOffset3D(0, flipY ? destinationHeight : 0, 0);
        blitRegion.DstOffsets[1] = new VkOffset3D(destinationWidth, flipY ? 0 : destinationHeight, 1);
        return blitRegion;
    }
}
