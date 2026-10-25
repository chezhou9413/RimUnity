using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RimUnity;

public sealed partial class Mesh
{
    // Alternate canonical storage for owned particle pages. General mesh access
    // materializes the public channels once; Clear discards either representation.
    private readonly List<ParticleVertex> particleVertices = new();

    private void MaterializeParticleChannels()
    {
        if (particleVertices.Count == 0) return;
        AppendParticleChannels(CollectionsMarshal.AsSpan(particleVertices));
        particleVertices.Clear();
    }

    private T[] ReadChannel<T>(List<T> channel)
    {
        MaterializeParticleChannels();
        return channel.ToArray();
    }

    private void AppendParticleChannels(ReadOnlySpan<ParticleVertex> data)
    {
        int count = checked(positions.Count + data.Length);
        positions.EnsureCapacity(count); normalData.EnsureCapacity(count);
        tangentData.EnsureCapacity(count); colorData.EnsureCapacity(count);
        for (int i = 0; i < 3; i++) (texcoords[i] ??= new()).EnsureCapacity(count);
        foreach (ref readonly var vertex in data)
        {
            positions.Add(vertex.Position); normalData.Add(vertex.Normal);
            tangentData.Add(vertex.Tangent); colorData.Add(vertex.Color);
            texcoords[0].Add(vertex.UV0); texcoords[1].Add(vertex.UV1); texcoords[2].Add(vertex.UV2);
        }
    }

    // Interleave incoming channels and reduce bounds in the same pass. Keep
    // the original first-point/reduction order for signed zero and nonfinite data.
    private (Vector3 Min, Vector3 Max) AppendUnpackedParticleVertices(List<Vector3> vertices, List<Vector3> normals,
        List<Vector4> tangents, List<Color32> colors, List<Vector4> uv0, List<Vector4> uv1, List<Vector4> uv2)
    {
        int offset = particleVertices.Count, count = vertices.Count;
        int end = checked(offset + count);
        particleVertices.EnsureCapacity(end);
        CollectionsMarshal.SetCount(particleVertices, end);
        var output = CollectionsMarshal.AsSpan(particleVertices).Slice(offset, count);
        // Counts are validated before mutation; none of these source lists is
        // resized while its span is live, and no span escapes the append.
        var positions = CollectionsMarshal.AsSpan(vertices);
        var normal = CollectionsMarshal.AsSpan(normals);
        var tangent = CollectionsMarshal.AsSpan(tangents);
        var color = CollectionsMarshal.AsSpan(colors);
        var firstUV = CollectionsMarshal.AsSpan(uv0);
        var secondUV = CollectionsMarshal.AsSpan(uv1);
        var thirdUV = CollectionsMarshal.AsSpan(uv2);
        Vector3 min = positions[0], max = min;
        for (int i = 0; i < count; i++)
        {
            Vector3 position = positions[i];
            output[i] = new ParticleVertex { Position = position, Normal = normal[i], Tangent = tangent[i],
                Color = color[i], UV0 = firstUV[i], UV1 = secondUV[i], UV2 = thirdUV[i] };
            min = Vector3.Min(min, position); max = Vector3.Max(max, position);
        }
        return (min, max);
    }

    internal void AppendParticleGeometry(List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents,
        List<Color32> colors, List<Vector4> uv0, List<Vector4> uv1, List<Vector4> uv2, List<int> indices,
        int submesh, List<ParticleVertex> packed)
        => AppendParticleGeometryCore(vertices, normals, tangents, colors, uv0, uv1, uv2, indices, submesh, false, packed);
}
