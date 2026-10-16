using System;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith.Rendering;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering;

//只拷贝场景颜色；地图的深度和模板附件不参与后处理拷贝。
internal static class HdrSceneCopy
{
    //辉光从透明黑开始，直接清颜色图像，省去全屏着色和输入描述符。
    internal static unsafe void Clear(CommandBuffer command, NativeTarget target)
    {
        var layout = target.Color.CurrentLayout;
        var barrier = Barrier(target, layout, VkImageLayout.TransferDstOptimal,
            layout == VkImageLayout.Undefined ? 0 : VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.ShaderReadBit,
            VkAccessFlags.TransferWriteBit);
        command.PipelineBarrier(VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.FragmentShaderBit,
            VkPipelineStageFlags.TransferBit, VkDependencyFlags.None, default, default,
            new ReadOnlySpan<VkImageMemoryBarrier>(in barrier));
        var clear = new VkClearColorValue();
        var range = barrier.SubresourceRange;
        command.ClearColorImage(target.Color.Image, VkImageLayout.TransferDstOptimal, &clear,
            new ReadOnlySpan<VkImageSubresourceRange>(in range));
        barrier = Barrier(target, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkAccessFlags.TransferWriteBit, VkAccessFlags.ShaderReadBit | VkAccessFlags.ColorAttachmentReadBit | VkAccessFlags.ColorAttachmentWriteBit);
        command.PipelineBarrier(VkPipelineStageFlags.TransferBit,
            VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.ColorAttachmentOutputBit,
            VkDependencyFlags.None, default, default, new ReadOnlySpan<VkImageMemoryBarrier>(in barrier));
        target.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
    }

    //格式能力只在目标首次写回时查询，相同格式沿用普通图像复制。
    internal static bool SupportsBlit(in FrameContext ctx, VkFormat source, VkFormat destination)
    {
        if (source == destination) return true;
        var device = ctx.GraphicsDevice.PhysicalDevice;
        return (device.GetFormatProperties(source).OptimalTilingFeatures & VkFormatFeatureFlags.BlitSrcBit) != 0
            && (device.GetFormatProperties(destination).OptimalTilingFeatures & VkFormatFeatureFlags.BlitDstBit) != 0;
    }

    //不同格式由 Blit 完成量化与 sRGB 附件编码，同尺寸使用最近邻避免额外模糊。
    internal static void Record(CommandBuffer command, NativeTarget source, NativeTarget destination)
    {
        Span<VkImageMemoryBarrier> barriers = stackalloc VkImageMemoryBarrier[2];
        barriers[0] = Barrier(source, VkImageLayout.ShaderReadOnlyOptimal, VkImageLayout.TransferSrcOptimal,
            VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.ShaderReadBit, VkAccessFlags.TransferReadBit);
        barriers[1] = Barrier(destination, VkImageLayout.ShaderReadOnlyOptimal, VkImageLayout.TransferDstOptimal,
            VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.ShaderReadBit, VkAccessFlags.TransferWriteBit);
        command.PipelineBarrier(VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.FragmentShaderBit,
            VkPipelineStageFlags.TransferBit, VkDependencyFlags.None, default, default, barriers);
        var subresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 };
        if (source.ColorFormat == destination.ColorFormat)
        {
            var region = new VkImageCopy
            {
                SrcSubresource = subresource, DstSubresource = subresource,
                Extent = new VkExtent3D(source.Width, source.Height, 1)
            };
            command.CopyImage(source.Color.Image, VkImageLayout.TransferSrcOptimal,
                destination.Color.Image, VkImageLayout.TransferDstOptimal, new ReadOnlySpan<VkImageCopy>(in region));
        }
        else
        {
            var region = new VkImageBlit { SrcSubresource = subresource, DstSubresource = subresource };
            region.SrcOffsets[1] = new VkOffset3D(source.Width, source.Height, 1);
            region.DstOffsets[1] = new VkOffset3D(destination.Width, destination.Height, 1);
            command.BlitImage(source.Color.Image, VkImageLayout.TransferSrcOptimal,
                destination.Color.Image, VkImageLayout.TransferDstOptimal, new ReadOnlySpan<VkImageBlit>(in region), VkFilter.Nearest);
        }
        barriers[0] = Barrier(source, VkImageLayout.TransferSrcOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkAccessFlags.TransferReadBit, VkAccessFlags.ShaderReadBit | VkAccessFlags.ColorAttachmentReadBit | VkAccessFlags.ColorAttachmentWriteBit);
        barriers[1] = Barrier(destination, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkAccessFlags.TransferWriteBit, VkAccessFlags.ShaderReadBit | VkAccessFlags.ColorAttachmentReadBit | VkAccessFlags.ColorAttachmentWriteBit);
        command.PipelineBarrier(VkPipelineStageFlags.TransferBit,
            VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.ColorAttachmentOutputBit,
            VkDependencyFlags.None, default, default, barriers);
        destination.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
    }

    //同一队列中转换颜色图像用途，不转移队列所有权。
    private static VkImageMemoryBarrier Barrier(NativeTarget texture, VkImageLayout from, VkImageLayout to,
        VkAccessFlags source, VkAccessFlags destination) => new()
    {
        Image = texture.Color.Image, OldLayout = from, NewLayout = to,
        SrcAccessMask = source, DstAccessMask = destination, SrcQueueFamilyIndex = -1, DstQueueFamilyIndex = -1,
        SubresourceRange = new VkImageSubresourceRange { AspectMask = VkImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 }
    };
}
