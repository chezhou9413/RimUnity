using System;
using System.Collections.Generic;
using Waylith.Graphics.Meshes;

namespace RimUnity;

//网格数据在 RimUnity 中维护，提交前统一写入 Waylith 原生顶点和索引缓冲。
public sealed partial class Mesh : Object
{
    private readonly Waylith.Graphics.Meshes.Mesh native = new();
    private readonly List<Vector3> positions = new(), normalData = new();
    private readonly List<Vector4> tangentData = new();
    private readonly List<Color32> colorData = new();
    private readonly List<Vector4>[] texcoords = new List<Vector4>[8];
    private readonly List<List<int>> submeshes = new();
    private int activeSubmeshes;
    private Matrix4x4[] bindData = Array.Empty<Matrix4x4>();
    private BoneWeight[] skin = Array.Empty<BoneWeight>();
    private Bounds meshBounds;
    private bool dirty = true;
    private IndexFormat format = IndexFormat.UInt16;
    public override string name { get => native.DebugName; set => native.DebugName = value; }
    public Waylith.Graphics.Meshes.Mesh EngineMesh { get { Flush(); return native; } }
    public int vertexCount => positions.Count;
    public bool isReadable => native.IsReadable;
    public IndexFormat indexFormat { get => format; set { format = value; dirty = true; } }
    public int subMeshCount
    {
        get => activeSubmeshes;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            while (submeshes.Count < value) submeshes.Add(new());
            for (int i = Math.Min(value, activeSubmeshes); i < Math.Max(value, activeSubmeshes); i++) submeshes[i].Clear();
            activeSubmeshes = value; dirty = true;
        }
    }
    public Vector3[] vertices { get => positions.ToArray(); set => Copy(positions, value); }
    public Vector3[] normals { get => normalData.ToArray(); set => Copy(normalData, value); }
    public Vector4[] tangents { get => tangentData.ToArray(); set => Copy(tangentData, value); }
    public Color32[] colors32 { get => colorData.ToArray(); set => Copy(colorData, value); }
    public Color[] colors
    {
        get { var result = new Color[colorData.Count]; for (int i = 0; i < result.Length; i++) result[i] = colorData[i]; return result; }
        set => SetColors(value);
    }
    public Vector2[] uv { get => ReadUV(0); set => SetUVs(0, value); }
    public Vector2[] uv2 { get => ReadUV(1); set => SetUVs(1, value); }
    public Vector2[] uv3 { get => ReadUV(2); set => SetUVs(2, value); }
    public Vector2[] uv4 { get => ReadUV(3); set => SetUVs(3, value); }
    public Matrix4x4[] bindposes { get => (Matrix4x4[])bindData.Clone(); set => bindData = (Matrix4x4[])value.Clone(); }
    public BoneWeight[] boneWeights { get => (BoneWeight[])skin.Clone(); set => skin = (BoneWeight[])value.Clone(); }
    public Bounds bounds { get => meshBounds; set { meshBounds = value; dirty = true; } }
    public int[] triangles
    {
        get { var all = new List<int>(); for (int i = 0; i < activeSubmeshes; i++) all.AddRange(submeshes[i]); return all.ToArray(); }
        set { subMeshCount = 1; SetTriangles(value, 0); }
    }
    public int[] GetIndices(int submesh) { CheckSubmesh(submesh); return submeshes[submesh].ToArray(); }
    public int[] GetTriangles(int submesh) => GetIndices(submesh);
    public void GetTriangles(System.Collections.Generic.List<int> result, int submesh) { CheckSubmesh(submesh); result.Clear(); result.AddRange(submeshes[submesh]); }
    public void GetVertices(List<Vector3> result) { result.Clear(); result.AddRange(positions); }
    public void SetVertices(List<Vector3> values) => Copy(positions, values);
    public void SetVertices(Vector3[] values) => vertices = values;
    public void SetNormals(List<Vector3> values) => Copy(normalData, values);
    public void SetNormals(Vector3[] values) => normals = values;
    public void SetTangents(List<Vector4> values) => Copy(tangentData, values);
    public void SetTangents(Vector4[] values) => tangents = values;
    public void SetColors(List<Color32> values) => Copy(colorData, values);
    public void SetColors(List<Color> values) => SetColors((IList<Color>)values);
    //写入时保留容量，公开读取仍返回独立快照。
    private void Copy<T>(List<T> target, IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        target.Clear(); target.AddRange(values); dirty = true;
    }
    //颜色转换写入复用的字节缓冲区。
    private void SetColors(IList<Color> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        colorData.Clear(); colorData.EnsureCapacity(values.Count);
        for (int i = 0; i < values.Count; i++) colorData.Add(values[i]);
        dirty = true;
    }
    //UV 快照只包含有效元素，缓存容量不暴露给调用者。
    private Vector2[] ReadUV(int channel)
    {
        var values = texcoords[channel];
        if (values == null) return Array.Empty<Vector2>();
        var result = new Vector2[values.Count];
        for (int i = 0; i < result.Length; i++) result[i] = new(values[i].x, values[i].y);
        return result;
    }
    public void SetUVs(int channel, IList<Vector2> values)
    {
        CheckChannel(channel); ArgumentNullException.ThrowIfNull(values);
        var output = texcoords[channel] ??= new(); output.Clear(); output.EnsureCapacity(values.Count);
        for (int i = 0; i < values.Count; i++) output.Add(new(values[i].x, values[i].y));
        dirty = true;
    }
    public void SetUVs(int channel, IList<Vector4> values)
    {
        CheckChannel(channel); Copy(texcoords[channel] ??= new(), values);
    }
    public void GetUVs(int channel, List<Vector4> values) { CheckChannel(channel); values.Clear(); if (texcoords[channel] != null) values.AddRange(texcoords[channel]); }
    public void GetUVs(int channel, List<Vector2> values) { CheckChannel(channel); values.Clear(); if (texcoords[channel] != null) foreach (Vector4 v in texcoords[channel]) values.Add(new(v.x, v.y)); }
    public void SetTriangles(IList<int> indices, int submesh, bool calculateBounds = true)
    {
        if (submesh < 0) throw new ArgumentOutOfRangeException(nameof(submesh));
        if (submesh >= subMeshCount) subMeshCount = submesh + 1;
        Copy(submeshes[submesh], indices);
        if (calculateBounds) RecalculateBounds();
    }
    public void Clear(bool keepVertexLayout = true)
    {
        positions.Clear(); normalData.Clear(); tangentData.Clear(); colorData.Clear();
        foreach (var channel in texcoords) channel?.Clear();
        subMeshCount = 0; bindData = Array.Empty<Matrix4x4>(); skin = Array.Empty<BoneWeight>(); meshBounds = default; dirty = true;
    }
    public void RecalculateBounds()
    {
        if (positions.Count == 0) { meshBounds = default; dirty = true; return; }
        Vector3 min = positions[0], max = min;
        foreach (Vector3 position in positions) { min = Vector3.Min(min, position); max = Vector3.Max(max, position); }
        meshBounds.SetMinMax(min, max); dirty = true;
    }
    public void RecalculateNormals()
    {
        var result = new Vector3[vertexCount];
        for (int submesh = 0; submesh < activeSubmeshes; submesh++)
        {
            var indices = submeshes[submesh];
            ValidateIndices(indices);
            for (int i = 0; i < indices.Count; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Vector3 n = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]); result[a] += n; result[b] += n; result[c] += n;
            }
        }
        for (int i = 0; i < result.Length; i++) result[i].Normalize(); Copy(normalData, result);
    }
    public void MarkDynamic() => native.MarkDynamic();
    public void UploadMeshData(bool markNoLongerReadable) { Flush(); native.Upload(markNoLongerReadable); }
    private static void CheckChannel(int channel) { if ((uint)channel >= 8) throw new ArgumentOutOfRangeException(nameof(channel)); }
    private void CheckSubmesh(int index) { if ((uint)index >= activeSubmeshes) throw new ArgumentOutOfRangeException(nameof(index)); }
    private void ValidateIndices(List<int> indices)
    {
        if (indices.Count % 3 != 0) throw new ArgumentException("三角形索引数量必须是三的倍数。");
        foreach (int index in indices) if ((uint)index >= vertexCount) throw new ArgumentOutOfRangeException(nameof(indices), "网格索引越界。");
    }
    protected override void Release() { if (edgeMesh != null) Destroy(edgeMesh); mirrored?.Dispose(); native.Dispose(); }
}

//四权重字段用于旧作者数据，任意数量权重仍由 SkinWeights 管理。
public struct BoneWeight
{
    public float weight0, weight1, weight2, weight3;
    public int boneIndex0, boneIndex1, boneIndex2, boneIndex3;
}
public enum IndexFormat { UInt16, UInt32 }

//静态网格引用随预制体序列化，不自行创建渲染器。
public sealed class MeshFilter : Component
{
    public Mesh sharedMesh;
}
