
using System;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith.Graphics;
using Waylith.Rendering;
using RenderTexture = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering
{
    //把原生后处理结果写回 IMGUI 使用的 Unity 兼容纹理。
    internal static class BloomPreviewTransfer
    {
        //两侧尺寸和格式相同，直接复制 GPU 图像，不经 CPU 回读。
        internal static void Copy(CommandBuffer command, GpuTextureAllocation source, GpuTextureAllocation destination, int width, int height)
        {
            var read = Barrier(source, source.CurrentLayout, VkImageLayout.TransferSrcOptimal,
                VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.ShaderReadBit, VkAccessFlags.TransferReadBit);
            var write = Barrier(destination, destination.CurrentLayout, VkImageLayout.TransferDstOptimal,
                destination.CurrentLayout == VkImageLayout.Undefined ? VkAccessFlags.None : VkAccessFlags.ShaderReadBit | VkAccessFlags.ColorAttachmentWriteBit,
                VkAccessFlags.TransferWriteBit);
            command.PipelineBarrier(VkPipelineStageFlags.AllCommandsBit, VkPipelineStageFlags.TransferBit, VkDependencyFlags.None,
                default, default, new[] { read, write });
            var layers = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 };
            var region = new VkImageCopy
            { SrcSubresource = layers, DstSubresource = layers, Extent = new VkExtent3D(width, height, 1) };
            command.CopyImage(source.Image, VkImageLayout.TransferSrcOptimal, destination.Image,
                VkImageLayout.TransferDstOptimal, new ReadOnlySpan<VkImageCopy>(in region));
            read = Barrier(source, VkImageLayout.TransferSrcOptimal, VkImageLayout.ShaderReadOnlyOptimal, VkAccessFlags.TransferReadBit, VkAccessFlags.ShaderReadBit);
            write = Barrier(destination, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal, VkAccessFlags.TransferWriteBit, VkAccessFlags.ShaderReadBit);
            command.PipelineBarrier(VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit, VkDependencyFlags.None,
                default, default, new[] { read, write });
            source.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
            destination.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
        }

        //只转换预览使用的第一层颜色图像。
        private static VkImageMemoryBarrier Barrier(GpuTextureAllocation texture, VkImageLayout oldLayout, VkImageLayout newLayout,
            VkAccessFlags sourceAccess, VkAccessFlags targetAccess) => new VkImageMemoryBarrier
        {
            Image = texture.Image, OldLayout = oldLayout, NewLayout = newLayout,
            SrcAccessMask = sourceAccess, DstAccessMask = targetAccess,
            SrcQueueFamilyIndex = -1, DstQueueFamilyIndex = -1,
            SubresourceRange = new VkImageSubresourceRange { AspectMask = VkImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 }
        };
    }
}

