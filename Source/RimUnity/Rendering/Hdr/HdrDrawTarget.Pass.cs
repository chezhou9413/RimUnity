using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace RimUnity.Rendering;

//颜色转换与网格绘制之间显式建立读写依赖，深度和模板保持地图原有内容。
internal sealed unsafe partial class HdrDrawTarget
{
    private static VkRenderPass CreatePass(DeviceEx device, VkFormat depthFormat)
    {
        bool depth = depthFormat != VkFormat.Undefined;
        VkAttachmentDescription* attachments = stackalloc VkAttachmentDescription[2];
        attachments[0] = new VkAttachmentDescription
        {
            Format = VkFormat.R16G16B16A16SFloat, Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Load, StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare, StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.ShaderReadOnlyOptimal, FinalLayout = VkImageLayout.ShaderReadOnlyOptimal
        };
        attachments[1] = new VkAttachmentDescription
        {
            Format = depthFormat, Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Load, StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.Load, StencilStoreOp = VkAttachmentStoreOp.Store,
            InitialLayout = VkImageLayout.DepthStencilAttachmentOptimal, FinalLayout = VkImageLayout.DepthStencilAttachmentOptimal
        };
        var colorRef = new VkAttachmentReference { Attachment = 0, Layout = VkImageLayout.ColorAttachmentOptimal };
        var depthRef = new VkAttachmentReference { Attachment = 1, Layout = VkImageLayout.DepthStencilAttachmentOptimal };
        var subpass = new VkSubpassDescription
        {
            PipelineBindPoint = VkPipelineBindPoint.Graphics, ColorAttachmentCount = 1,
            ColorAttachments = &colorRef, DepthStencilAttachment = depth ? &depthRef : null
        };
        VkSubpassDependency* dependencies = stackalloc VkSubpassDependency[2];
        var stages = VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.EarlyFragmentTestsBit |
            VkPipelineStageFlags.LateFragmentTestsBit | VkPipelineStageFlags.ColorAttachmentOutputBit;
        var access = VkAccessFlags.ShaderReadBit | VkAccessFlags.DepthStencilAttachmentReadBit |
            VkAccessFlags.DepthStencilAttachmentWriteBit | VkAccessFlags.ColorAttachmentReadBit | VkAccessFlags.ColorAttachmentWriteBit;
        dependencies[0] = new VkSubpassDependency
        { SrcSubpass = -1, DstSubpass = 0, SrcStageMask = stages, DstStageMask = stages, SrcAccessMask = access, DstAccessMask = access };
        dependencies[1] = new VkSubpassDependency
        { SrcSubpass = 0, DstSubpass = -1, SrcStageMask = stages, DstStageMask = stages, SrcAccessMask = access, DstAccessMask = access };
        return device.CreateRenderPass(new VkRenderPassCreateInfo
        {
            AttachmentCount = depth ? 2 : 1, Attachments = attachments, SubpassCount = 1, Subpasses = &subpass,
            DependencyCount = 2, Dependencies = dependencies
        }, device.VkAllocator);
    }
}
