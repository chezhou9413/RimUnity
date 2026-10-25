using System.Runtime.CompilerServices;

namespace RimUnity;

public sealed partial class Mesh
{
    // Diagnostics only. Does not flush/upload or retain any resource references.
    // Payload capacities exclude object/list headers, allocator overhead and GPU buffers.
    internal (long Vertices, long Indices, long ManagedBytes, long NativeCpuBytes) ParticleBufferUsage()
    {
        long indices = 0;
        foreach (var list in submeshes) indices += list.Capacity;
        long managed = (long)particleVertices.Capacity * Unsafe.SizeOf<ParticleVertex>() + (positions.Capacity + (long)normalData.Capacity) * 12
            + (long)tangentData.Capacity * 16 + (long)colorData.Capacity * 4 + indices * 4
            + (long)particleSubmeshBounds.Capacity * 24;
        foreach (var channel in texcoords) managed += (long)(channel?.Capacity ?? 0) * 16;
        var storage = native.CpuStorage;
        long nativeBytes = storage.GetIndexBuffer().LengthBytes;
        if (storage.Layout is { } layout)
            for (int i = 0; i < layout.StreamCount; i++) nativeBytes += storage.GetVertexStream(i).LengthBytes;
        return (positions.Capacity + (long)particleVertices.Capacity, indices, managed, nativeBytes);
    }
}
