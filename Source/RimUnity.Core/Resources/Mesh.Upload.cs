using System;
using System.Collections.Generic;
using Waylith.Graphics.Meshes;

namespace RimUnity;

//构造明确的顶点布局，保留八个 UV 通道及切线，支持粒子流和蒙皮范围。
public sealed partial class Mesh
{
    private unsafe void Flush()
    {
        Runtime.NativeRuntime.AssertThread();
        if (!this) throw new ObjectDisposedException(name);
        if (!dirty) return;
        var attributes = new List<VertexAttribute> { new(VertexAttributeSemantic.Position, VertexAttributeFormat.Float32, 3) };
        Add(normalData.Length, VertexAttributeSemantic.Normal, 3);
        Add(tangentData.Length, VertexAttributeSemantic.Tangent, 4);
        Add(colorData.Length, VertexAttributeSemantic.Color, 4, VertexAttributeFormat.UNorm8);
        for (int i = 0; i < 8; i++) Add(texcoords[i]?.Length ?? 0, MeshArrays.TexCoordSemantic(i), 4);
        int indexCount = 0;
        foreach (int[] indices in submeshes) { ValidateIndices(indices); indexCount = checked(indexCount + indices.Length); }
        if (format == IndexFormat.UInt16 && positions.Length > ushort.MaxValue + 1) throw new InvalidOperationException("顶点数量超过 UInt16 索引容量，请选择 UInt32。");
        var layout = new VertexLayout(attributes);
        CpuMeshData storage = native.CpuStorage;
        storage.SetVertexLayout(layout, positions.Length);
        int stride = layout.GetStreamStride(0);
        byte* target = (byte*)storage.GetVertexStream(0).Pointer;
        foreach (VertexAttribute description in attributes)
        {
            layout.TryGetAttribute(description.Semantic, out var slot);
            for (int i = 0; i < positions.Length; i++)
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
        storage.SetSubMeshCount(submeshes.Length);
        Vector3 min = meshBounds.min, max = meshBounds.max;
        storage.Bounds = new(min.x, min.y, min.z, max.x, max.y, max.z);
        byte* indicesTarget = (byte*)storage.GetIndexBuffer().Pointer;
        int cursor = 0;
        for (int s = 0; s < submeshes.Length; s++)
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

        //缺失通道允许由 Shader 默认输入补齐，长度不匹配则明确拒绝。
        void Add(int count, VertexAttributeSemantic semantic, int dimension, VertexAttributeFormat type = VertexAttributeFormat.Float32)
        {
            if (count == 0) return;
            if (count != positions.Length) throw new InvalidOperationException($"网格 {name} 的 {semantic} 数量 {count} 与顶点数 {positions.Length} 不符。");
            attributes.Add(new(semantic, type, dimension));
        }
    }
}
