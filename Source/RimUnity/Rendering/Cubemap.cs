using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Brutal.VulkanApi.Vma;
using HarmonyLib;
using Waylith.Rendering;

namespace RimUnity;

//六面纹理直接上传为 Vulkan Cube 图像，由资源对象持有设备内存。
public sealed class Cubemap : Texture
{
    internal bool Linear { get; }
    private Cubemap(string name, int size, int mipCount, float[][] faces, bool linear)
        : base(new CubeCore(name, size, mipCount, faces))
    {
        Linear = linear;
        this.name = name;
    }

    //从导出的浮点面数据恢复各级纹理。
    internal static Cubemap Load(string name, int size, int mipCount, byte[] bytes, bool linear)
    {
        if (size <= 0 || mipCount <= 0) throw new ArgumentException("Invalid cubemap dimensions.");
        int maxMipCount = 1;
        for (int edge = size; edge > 1; edge >>= 1) maxMipCount++;
        if (mipCount > maxMipCount) throw new ArgumentException("Invalid cubemap mip count.");
        long expected = 0;
        for (int mip = 0; mip < mipCount; mip++) { long edge = Math.Max(1, size >> mip); expected = checked(expected + edge * edge * 6 * 16); }
        if (bytes == null || bytes.LongLength != expected) throw new ArgumentException("Cubemap byte count does not match dimensions.");
        var faces = new float[6 * mipCount][];
        int offset = 0;
        for (int mip = 0; mip < mipCount; mip++)
        {
            int edge = Math.Max(1, size >> mip);
            for (int face = 0; face < 6; face++)
            {
                var values = new float[checked(edge * edge * 4)];
                Buffer.BlockCopy(bytes, offset, values, 0, values.Length * sizeof(float));
                offset += values.Length * sizeof(float); faces[mip * 6 + face] = values;
            }
        }
        return new Cubemap(name, size, mipCount, faces, linear);
    }
    private sealed class CubeCore : Waylith.Rendering.Texture
    {
        private delegate void Commands(Action<CommandBuffer, List<BufferAllocationInfo>> record, bool batch, (GpuTextureAllocation, int)[] touched);
        private static readonly Commands Execute = AccessTools.MethodDelegate<Commands>(AccessTools.Method(typeof(GpuTextureService), "ExecuteImageCommands",
            new[] { typeof(Action<CommandBuffer, List<BufferAllocationInfo>>), typeof(bool), typeof((GpuTextureAllocation, int)[]) }));
        private static readonly Func<string, long, BufferAllocationInfo> Rent = AccessTools.MethodDelegate<Func<string, long, BufferAllocationInfo>>(
            AccessTools.Method(typeof(GpuTextureService), "RentStagingBuffer"));
        private static ref long NextHandle => ref AccessTools.StaticFieldRefAccess<long>(typeof(GpuTextureService), "_nextHandle");
        private static readonly Action<GpuTextureAllocation, VkImageType, VkImageUsageFlags, VkImageViewType, TextureSwizzle> ImageInfo = AccessTools.MethodDelegate<Action<GpuTextureAllocation, VkImageType, VkImageUsageFlags, VkImageViewType, TextureSwizzle>>(AccessTools.Method(typeof(GpuTextureAllocation), "SetImageInfo"));
        public override int width { get; set; }
        public override int height { get; set; }
        public override bool CanRehydrate => false;

        internal CubeCore(string name, int size, int mipCount, float[][] faces)
        {
            if (size <= 0 || mipCount <= 0 || faces == null || faces.Length != 6 * mipCount)
                throw new ArgumentException("Cubemap requires six faces for every mip level.");
            for (int mip = 0; mip < mipCount; mip++)
                for (int face = 0; face < 6; face++)
                    if (faces[mip * 6 + face]?.Length != Math.Max(1, size >> mip) * Math.Max(1, size >> mip) * 4)
                        throw new ArgumentException("Cubemap face dimensions do not match its mip level.");
            if (!GpuTextureService.IsConfigured) throw new InvalidOperationException("Cubemap load requires the initialized rendering device.");
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var device = (DeviceEx)typeof(GpuTextureService).GetField("_device", flags).GetValue(null);
            var allocator = (VmaAllocator)typeof(GpuTextureService).GetField("_allocator", flags).GetValue(null);
            Name = name; width = height = size; MipCount = mipCount;
            var usage = VkImageUsageFlags.TransferSrcBit | VkImageUsageFlags.TransferDstBit | VkImageUsageFlags.SampledBit;
            var format = VkFormat.R32G32B32A32SFloat;
            var allocation = GpuTextureService.AllocateTextureImage(allocator, name, MemoryCategory, new VkImageCreateInfo {
                Flags = VkImageCreateFlags.CubeCompatibleBit, ImageType = VkImageType._2D, Format = format,
                Extent = new VkExtent3D(size, size, 1), MipLevels = mipCount, ArrayLayers = 6,
                Samples = VkSampleCountFlags._1Bit, Tiling = VkImageTiling.Optimal, Usage = usage,
                SharingMode = VkSharingMode.Exclusive, InitialLayout = VkImageLayout.Undefined });
            var view = device.CreateImageView(new VkImageViewCreateInfo {
                Image = allocation.Image, ViewType = VkImageViewType.Cube, Format = format,
                SubresourceRange = new VkImageSubresourceRange { AspectMask = VkImageAspectFlags.ColorBit,
                    LevelCount = mipCount, LayerCount = 6 } }, device.VkAllocator);
            var gpu = new GpuTextureAllocation(device, allocator, allocation, view,
                new IntPtr(Interlocked.Increment(ref NextHandle)), mipCount, format, name, MemoryCategory);
            ImageInfo(gpu, VkImageType._2D, usage, VkImageViewType.Cube, default);
            GpuResource = gpu;
            try
            {
                Execute((command, staging) => {
                    command.PipelineBarrier(VkPipelineStageFlags.TopOfPipeBit, VkPipelineStageFlags.TransferBit, VkDependencyFlags.None,
                        default(ReadOnlySpan<VkMemoryBarrier>), default(ReadOnlySpan<VkBufferMemoryBarrier>), new[] {
                            new VkImageMemoryBarrier { OldLayout = VkImageLayout.Undefined, NewLayout = VkImageLayout.TransferDstOptimal,
                                DstAccessMask = VkAccessFlags.TransferWriteBit, SrcQueueFamilyIndex = -1, DstQueueFamilyIndex = -1,
                                Image = gpu.Image, SubresourceRange = new VkImageSubresourceRange {
                                    AspectMask = VkImageAspectFlags.ColorBit, LevelCount = mipCount, LayerCount = 6 } } });
                    gpu.CurrentLayout = VkImageLayout.TransferDstOptimal;
                    for (int mip = 0; mip < mipCount; mip++)
                    {
                        gpu.SetMipLayout(mip, VkImageLayout.TransferDstOptimal);
                        int edge = Math.Max(1, size >> mip);
                        for (int face = 0; face < 6; face++)
                        {
                            var bytes = new byte[faces[mip * 6 + face].Length * sizeof(float)];
                            Buffer.BlockCopy(faces[mip * 6 + face], 0, bytes, 0, bytes.Length);
                            var buffer = Rent(name + ".Cube[" + face + "," + mip + "]", bytes.Length);
                            var map = new AllocationMapInfo(buffer.AllocationInfo) { Region = new MapRegion { Offset = ByteSize.Zero, Size = buffer.BufferSize } };
                            var pointer = allocator.MapMemory(in map);
                            try { Marshal.Copy(bytes, 0, (nint)pointer, bytes.Length); }
                            finally { allocator.UnmapMemory(in map); }
                            staging.Add(buffer);
                            command.CopyBufferToImage(buffer.VkBuffer, gpu.Image, VkImageLayout.TransferDstOptimal, new[] {
                                new VkBufferImageCopy { BufferOffset = ByteSize.Zero, BufferRowLength = edge,
                                    ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, MipLevel = mip, BaseArrayLayer = face, LayerCount = 1 },
                                    ImageExtent = new VkExtent3D(edge, edge, 1) } });
                        }
                    }
                }, false, new[] { (gpu, -1) });
            }
            catch { gpu.Dispose(); GpuResource = null; throw; }
        }
    }
}
