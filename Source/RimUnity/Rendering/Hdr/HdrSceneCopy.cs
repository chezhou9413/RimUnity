using System;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering;

//只拷贝场景颜色；地图的深度和模板附件不参与后处理拷贝。
internal static class HdrSceneCopy
{
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
        var region = new VkImageCopy
        {
            SrcSubresource = subresource, DstSubresource = subresource,
            Extent = new VkExtent3D(source.Width, source.Height, 1)
        };
        command.CopyImage(source.Color.Image, VkImageLayout.TransferSrcOptimal,
            destination.Color.Image, VkImageLayout.TransferDstOptimal, new ReadOnlySpan<VkImageCopy>(in region));
        barriers[0] = Barrier(source, VkImageLayout.TransferSrcOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkAccessFlags.TransferReadBit, VkAccessFlags.ShaderReadBit);
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
