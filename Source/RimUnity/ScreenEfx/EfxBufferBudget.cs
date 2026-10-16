using System;
using Waylith.Rendering;

namespace RimUnity.ScreenEfx;

//Allocation sizes, not resident VRAM. Upload memory is host-visible and its heap is driver-dependent.
internal readonly record struct EfxBufferBudget(int UniformBytes, int StorageBytes, int DescriptorSets,
    int UniformAlignment, int StorageAlignment)
{
    internal const int InstanceBytes = 4096, IndirectBytes = 4096, PersistentUniformBytes = 16384;
    internal int PersistentStorageBytes => checked(StorageBytes * 2);
    internal long ReservedBytes => (long)UniformBytes + StorageBytes + InstanceBytes + IndirectBytes +
        PersistentUniformBytes + PersistentStorageBytes + 4096; //native fallback vertex stream

    internal static EfxBufferBudget ForFrame(EfxFrame frame, int uniformAlignment, int storageAlignment)
    {
        uniformAlignment = Math.Max(16, uniformAlignment);
        storageAlignment = Math.Max(16, storageAlignment);
        //Camera data plus a full MaterialUniformBytes image for every pass, with conservative headroom.
        long uniforms = 16384 + 4L * Align(256, uniformAlignment);
        long storage = 16384;
        foreach (var pass in frame.Passes)
        {
            uniforms = checked(uniforms + Align(MaterialUniformBytes.MaxBytes, uniformAlignment));
            foreach (var array in pass.Binding.StorageArrays)
                storage = checked(storage + Align(array.Bytes.Length, storageAlignment));
            //Missing bindings may use the native zero-storage fallback. Reserve it conservatively per slot.
            storage = checked(storage + pass.Binding.MaterialLayout.StorageBufferSlots.Length * Align(4096, storageAlignment));
        }
        return new(PowerOfTwo(Math.Max(65536, uniforms)), PowerOfTwo(Math.Max(131072, storage)),
            PowerOfTwo(Math.Max(128, 2L * frame.Passes.Count + 16)), uniformAlignment, storageAlignment);
    }

    internal bool Covers(EfxBufferBudget required) => UniformBytes >= required.UniformBytes && StorageBytes >= required.StorageBytes
        && DescriptorSets >= required.DescriptorSets && UniformAlignment == required.UniformAlignment && StorageAlignment == required.StorageAlignment;
    private static long Align(long size, int alignment) => checked((size + alignment - 1) / alignment * alignment);
    private static int PowerOfTwo(long size)
    {
        int result = 1;
        while (result < size) result = checked(result * 2);
        return result;
    }
}
