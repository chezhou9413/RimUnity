using System;
using System.Collections.Generic;
using Waylith.Graphics.Meshes;

namespace RimUnity;

//构造明确的顶点布局，保留八个 UV 通道及切线，支持粒子流和蒙皮范围。
public sealed partial class Mesh
{
    private readonly List<VertexAttribute> attributes = new();
    private VertexLayout uploadLayout;
    private int layoutMask = -1;

    //通道数量每次检查，布局仅在通道组合变化时重新建立。
    private unsafe void Flush()
    {
        Runtime.NativeRuntime.AssertThread();
        if (!this) throw new ObjectDisposedException(name);
        if (!dirty) return;
        int mask = ChannelBit(normalData.Count, 0) | ChannelBit(tangentData.Count, 1) | ChannelBit(colorData.Count, 2);
        for (int i = 0; i < 8; i++) mask |= ChannelBit(texcoords[i]?.Count ?? 0, i + 3);
        if (mask != layoutMask)
        {
            attributes.Clear(); attributes.Add(new(VertexAttributeSemantic.Position, VertexAttributeFormat.Float32, 3));
            if (normalData.Count > 0) attributes.Add(new(VertexAttributeSemantic.Normal, VertexAttributeFormat.Float32, 3));
            if (tangentData.Count > 0) attributes.Add(new(VertexAttributeSemantic.Tangent, VertexAttributeFormat.Float32, 4));
            if (colorData.Count > 0) attributes.Add(new(VertexAttributeSemantic.Color, VertexAttributeFormat.UNorm8, 4));
            for (int i = 0; i < 8; i++)
                if (texcoords[i]?.Count > 0) attributes.Add(new(MeshArrays.TexCoordSemantic(i), VertexAttributeFormat.Float32, 4));
            uploadLayout = new VertexLayout(attributes); layoutMask = mask;
        }
        int indexCount = 0;
        for (int i = 0; i < activeSubmeshes; i++) { ValidateIndices(submeshes[i]); indexCount = checked(indexCount + submeshes[i].Count); }
        if (format == IndexFormat.UInt16 && positions.Count > ushort.MaxValue + 1) throw new InvalidOperationException("顶点数量超过 UInt16 索引容量，请选择 UInt32。");
        var layout = uploadLayout;
        CpuMeshData storage = native.CpuStorage;
        storage.SetVertexLayout(layout, positions.Count);
        int stride = layout.GetStreamStride(0);
        byte* target = (byte*)storage.GetVertexStream(0).Pointer;
        if (!UploadParticleVertices(target, mask, stride)) foreach (VertexAttribute description in attributes)
        {
            layout.TryGetAttribute(description.Semantic, out var slot);
            for (int i = 0; i < positions.Count; i++)
            {
                byte* address = target + i * stride + slot.OffsetBytes;
                switch (slot.Semantic)
                {
                    case VertexAttributeSemantic.Position: *(Vector3*)address = positions[i]; break;
                    case VertexAttributeSemantic.Normal: *(Vector3*)address = normalData[i]; break;
                    case VertexAttributeSemantic.Tangent: *(Vector4*)address = tangentData[i]; break;
                    case VertexAttributeSemantic.Color: *(Color32*)address = colorData[i]; break;
                    default: *(Vector4*)address = texcoords[(int)slot.Semantic - (int)VertexAttributeSemantic.TexCoord0][i]; break;
                }
            }
        }
        storage.SetIndexBufferParams(indexCount, format == IndexFormat.UInt32 ? MeshIndexFormat.UInt32 : MeshIndexFormat.UInt16);
        storage.SetSubMeshCount(activeSubmeshes);
        Vector3 min = meshBounds.min, max = meshBounds.max;
        storage.Bounds = new(min.x, min.y, min.z, max.x, max.y, max.z);
        byte* indicesTarget = (byte*)storage.GetIndexBuffer().Pointer;
        int cursor = 0;
        for (int s = 0; s < activeSubmeshes; s++)
        {
            int start = cursor;
            foreach (int index in submeshes[s])
            {
                if (format == IndexFormat.UInt32) ((uint*)indicesTarget)[cursor++] = (uint)index;
                else ((ushort*)indicesTarget)[cursor++] = checked((ushort)index);
            }
            storage.SetSubMesh(s, new(start, cursor - start, bounds: storage.Bounds));
        }
        native.MarkCpuStorageChanged(); dirty = false;
        edgeVersion++;
    }

    //缺失通道允许由 Shader 默认输入补齐，长度不匹配则明确拒绝。
    private int ChannelBit(int count, int bit)
    {
        if (count == 0) return 0;
        if (count != positions.Count) throw new InvalidOperationException($"网格 {name} 的通道 {bit} 数量 {count} 与顶点数 {positions.Count} 不符。");
        return 1 << bit;
    }
}
