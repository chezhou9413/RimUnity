using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RimUnity;

//把已经生成的世界空间粒子几何直接追加到同一网格，避免中间 Mesh 和二次拷贝。
public sealed partial class Mesh
{
    private readonly List<Bounds> particleSubmeshBounds = new();
    private bool particleBoundsValid;
    //目标应为空网格或相同粒子布局；索引按追加前的顶点数偏移。
    public void AppendParticleGeometry(List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents,
        List<Color32> colors, List<Vector4> uv0, List<Vector4> uv1, List<Vector4> uv2, List<int> indices)
        => AppendParticleGeometryCore(vertices, normals, tangents, colors, uv0, uv1, uv2, indices, 0, true);

    // Separate ordered draw ranges may share one upload. Each range keeps its own bounds.
    public void AppendParticleGeometry(List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents,
        List<Color32> colors, List<Vector4> uv0, List<Vector4> uv1, List<Vector4> uv2, List<int> indices, int submesh)
        => AppendParticleGeometryCore(vertices, normals, tangents, colors, uv0, uv1, uv2, indices, submesh, false);

    private void AppendParticleGeometryCore(List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents,
        List<Color32> colors, List<Vector4> uv0, List<Vector4> uv1, List<Vector4> uv2, List<int> indices, int submesh, bool singleRange, List<ParticleVertex> packed = null)
    {
        Runtime.NativeRuntime.AssertThread();
        ArgumentNullException.ThrowIfNull(indices);
        if (packed == null)
        {
            ArgumentNullException.ThrowIfNull(vertices); ArgumentNullException.ThrowIfNull(normals);
            ArgumentNullException.ThrowIfNull(tangents); ArgumentNullException.ThrowIfNull(colors);
            ArgumentNullException.ThrowIfNull(uv0); ArgumentNullException.ThrowIfNull(uv1); ArgumentNullException.ThrowIfNull(uv2);
        }
        int count = packed?.Count ?? vertices.Count, offset = vertexCount;
        if (submesh < 0 || submesh > activeSubmeshes) throw new ArgumentOutOfRangeException(nameof(submesh));
        if (packed == null && (normals.Count != count || tangents.Count != count || colors.Count != count
            || uv0.Count != count || uv1.Count != count || uv2.Count != count))
            throw new ArgumentException("粒子顶点通道数量不一致。");
        if (offset != 0 && (particleVertices.Count == 0 && (normalData.Count != offset || tangentData.Count != offset || colorData.Count != offset
            || texcoords[0]?.Count != offset || texcoords[1]?.Count != offset || texcoords[2]?.Count != offset)
            || singleRange && activeSubmeshes != 1))
            throw new InvalidOperationException("只能追加到相同布局的粒子几何。");
        if (indices.Count % 3 != 0) throw new ArgumentException("粒子三角形索引数量必须是三的倍数。", nameof(indices));
        foreach (int index in CollectionsMarshal.AsSpan(indices)) if ((uint)index >= count) throw new ArgumentOutOfRangeException(nameof(indices));
        if (count == 0) return;
        // The existing overload collapses an empty mesh's predeclared submeshes to one.
        if (singleRange && activeSubmeshes > 1) subMeshCount = 1;
        if (!particleBoundsValid)
        {
            particleSubmeshBounds.Clear();
            for (int i = 0; i < activeSubmeshes; i++) particleSubmeshBounds.Add(meshBounds);
        }
        bool newRange = submesh == activeSubmeshes;
        if (newRange) { subMeshCount = submesh + 1; particleSubmeshBounds.Add(default); }
        bool emptyRange = offset == 0 || newRange || submeshes[submesh].Count == 0;
        Vector3 min = default, max = default;
        bool boundsCalculated = false;
        if (packed != null && (offset == 0 || particleVertices.Count != 0)) particleVertices.AddRange(packed);
        else if (packed != null) AppendParticleChannels(CollectionsMarshal.AsSpan(packed));
        else if (particleVertices.Count != 0)
        {
            (min, max) = AppendUnpackedParticleVertices(vertices, normals, tangents, colors, uv0, uv1, uv2);
            boundsCalculated = true;
        }
        else
        {
            positions.AddRange(vertices); normalData.AddRange(normals); tangentData.AddRange(tangents); colorData.AddRange(colors);
            (texcoords[0] ??= new()).AddRange(uv0); (texcoords[1] ??= new()).AddRange(uv1); (texcoords[2] ??= new()).AddRange(uv2);
        }
        var triangles = submeshes[submesh];
        int start = triangles.Count, end = checked(start + indices.Count);
        triangles.EnsureCapacity(end);
        // The source is validated above; fill only the newly owned destination slice.
        CollectionsMarshal.SetCount(triangles, end);
        var targetIndices = CollectionsMarshal.AsSpan(triangles).Slice(start, indices.Count);
        var sourceIndices = CollectionsMarshal.AsSpan(indices);
        for (int i = 0; i < sourceIndices.Length; i++) targetIndices[i] = checked(sourceIndices[i] + offset);
        if (packed != null)
        {
            var source = CollectionsMarshal.AsSpan(packed);
            min = max = source[0].Position;
            foreach (ref readonly var vertex in source) { min = Vector3.Min(min, vertex.Position); max = Vector3.Max(max, vertex.Position); }
        }
        else if (!boundsCalculated)
        {
            var source = CollectionsMarshal.AsSpan(vertices);
            min = max = source[0];
            foreach (var vertex in source) { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
        }
        Vector3 globalMin = offset == 0 ? min : Vector3.Min(meshBounds.min, min);
        Vector3 globalMax = offset == 0 ? max : Vector3.Max(meshBounds.max, max);
        meshBounds.SetMinMax(globalMin, globalMax); dirty = true;
        Bounds range = particleSubmeshBounds[submesh];
        min = emptyRange ? min : Vector3.Min(range.min, min); max = emptyRange ? max : Vector3.Max(range.max, max);
        range.SetMinMax(min, max); particleSubmeshBounds[submesh] = range; particleBoundsValid = true;
    }
}
