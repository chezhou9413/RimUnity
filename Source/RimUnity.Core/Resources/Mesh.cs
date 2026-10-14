using System;
using System.Collections.Generic;
using Waylith.Graphics.Meshes;

namespace RimUnity;

//网格数据在 RimUnity 中维护，提交前统一写入 Waylith 原生顶点和索引缓冲。
public sealed partial class Mesh : Object
{
    private readonly Waylith.Graphics.Meshes.Mesh native = new();
    private Vector3[] positions = Array.Empty<Vector3>(), normalData = Array.Empty<Vector3>();
    private Vector4[] tangentData = Array.Empty<Vector4>();
    private Color32[] colorData = Array.Empty<Color32>();
    private readonly Vector4[][] texcoords = new Vector4[8][];
    private int[][] submeshes = Array.Empty<int[]>();
    private Matrix4x4[] bindData = Array.Empty<Matrix4x4>();
    private BoneWeight[] skin = Array.Empty<BoneWeight>();
    private Bounds meshBounds;
    private bool dirty = true;
    private IndexFormat format = IndexFormat.UInt16;
    public override string name { get => native.DebugName; set => native.DebugName = value; }
    public Waylith.Graphics.Meshes.Mesh EngineMesh { get { Flush(); return native; } }
    public int vertexCount => positions.Length;
    public bool isReadable => native.IsReadable;
    public IndexFormat indexFormat { get => format; set { format = value; dirty = true; } }
    public int subMeshCount
    {
        get => submeshes.Length;
        set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); Array.Resize(ref submeshes, value); for (int i = 0; i < value; i++) submeshes[i] ??= Array.Empty<int>(); dirty = true; }
    }
    public Vector3[] vertices { get => (Vector3[])positions.Clone(); set { positions = (Vector3[])value.Clone(); dirty = true; } }
    public Vector3[] normals { get => (Vector3[])normalData.Clone(); set { normalData = (Vector3[])value.Clone(); dirty = true; } }
    public Vector4[] tangents { get => (Vector4[])tangentData.Clone(); set { tangentData = (Vector4[])value.Clone(); dirty = true; } }
    public Color32[] colors32 { get => (Color32[])colorData.Clone(); set { colorData = (Color32[])value.Clone(); dirty = true; } }
    public Color[] colors { get => Array.ConvertAll(colorData, c => (Color)c); set { colorData = Array.ConvertAll(value, c => (Color32)c); dirty = true; } }
    public Vector2[] uv { get => Array.ConvertAll(texcoords[0] ?? Array.Empty<Vector4>(), v => new Vector2(v.x, v.y)); set => SetUVs(0, value); }
    public Vector2[] uv2 { get => Array.ConvertAll(texcoords[1] ?? Array.Empty<Vector4>(), v => new Vector2(v.x, v.y)); set => SetUVs(1, value); }
    public Vector2[] uv3 { get => Array.ConvertAll(texcoords[2] ?? Array.Empty<Vector4>(), v => new Vector2(v.x, v.y)); set => SetUVs(2, value); }
    public Vector2[] uv4 { get => Array.ConvertAll(texcoords[3] ?? Array.Empty<Vector4>(), v => new Vector2(v.x, v.y)); set => SetUVs(3, value); }
    public Matrix4x4[] bindposes { get => (Matrix4x4[])bindData.Clone(); set => bindData = (Matrix4x4[])value.Clone(); }
    public BoneWeight[] boneWeights { get => (BoneWeight[])skin.Clone(); set => skin = (BoneWeight[])value.Clone(); }
    public Bounds bounds { get => meshBounds; set { meshBounds = value; dirty = true; } }
    public int[] triangles
    {
        get { var all = new List<int>(); foreach (int[] indices in submeshes) all.AddRange(indices); return all.ToArray(); }
        set { subMeshCount = 1; SetTriangles(value, 0); }
    }
    public int[] GetIndices(int submesh) => (int[])submeshes[submesh].Clone();
    public int[] GetTriangles(int submesh) => GetIndices(submesh);
    public void GetTriangles(System.Collections.Generic.List<int> result, int submesh) { result.Clear(); result.AddRange(submeshes[submesh]); }
    public void GetVertices(List<Vector3> result) { result.Clear(); result.AddRange(positions); }
    public void SetVertices(List<Vector3> values) { positions = values.ToArray(); dirty = true; }
    public void SetVertices(Vector3[] values) => vertices = values;
    public void SetNormals(List<Vector3> values) { normalData = values.ToArray(); dirty = true; }
    public void SetNormals(Vector3[] values) => normals = values;
    public void SetTangents(List<Vector4> values) { tangentData = values.ToArray(); dirty = true; }
    public void SetTangents(Vector4[] values) => tangents = values;
    public void SetColors(List<Color32> values) { colorData = values.ToArray(); dirty = true; }
    public void SetColors(List<Color> values) { colorData = Array.ConvertAll(values.ToArray(), c => (Color32)c); dirty = true; }
    public void SetUVs(int channel, IList<Vector2> values)
    {
        CheckChannel(channel); var output = new Vector4[values.Count]; for (int i = 0; i < output.Length; i++) output[i] = new(values[i].x, values[i].y);
        texcoords[channel] = output; dirty = true;
    }
    public void SetUVs(int channel, IList<Vector4> values)
    {
        CheckChannel(channel); var output = new Vector4[values.Count]; values.CopyTo(output, 0); texcoords[channel] = output; dirty = true;
    }
    public void GetUVs(int channel, List<Vector4> values) { CheckChannel(channel); values.Clear(); if (texcoords[channel] != null) values.AddRange(texcoords[channel]); }
    public void GetUVs(int channel, List<Vector2> values) { CheckChannel(channel); values.Clear(); if (texcoords[channel] != null) foreach (Vector4 v in texcoords[channel]) values.Add(new(v.x, v.y)); }
    public void SetTriangles(IList<int> indices, int submesh, bool calculateBounds = true)
    {
        if (submesh < 0) throw new ArgumentOutOfRangeException(nameof(submesh));
        if (submesh >= subMeshCount) subMeshCount = submesh + 1;
        var result = new int[indices.Count]; indices.CopyTo(result, 0); submeshes[submesh] = result; dirty = true;
        if (calculateBounds) RecalculateBounds();
    }
    public void Clear(bool keepVertexLayout = true)
    {
        positions = normalData = Array.Empty<Vector3>(); tangentData = Array.Empty<Vector4>(); colorData = Array.Empty<Color32>();
        Array.Clear(texcoords); submeshes = Array.Empty<int[]>(); bindData = Array.Empty<Matrix4x4>(); skin = Array.Empty<BoneWeight>(); meshBounds = default; dirty = true;
    }
    public void RecalculateBounds()
    {
        if (positions.Length == 0) { meshBounds = default; dirty = true; return; }
        Vector3 min = positions[0], max = min;
        foreach (Vector3 position in positions) { min = Vector3.Min(min, position); max = Vector3.Max(max, position); }
        meshBounds.SetMinMax(min, max); dirty = true;
    }
    public void RecalculateNormals()
    {
        var result = new Vector3[vertexCount];
        foreach (int[] indices in submeshes)
        {
            ValidateIndices(indices);
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Vector3 n = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]); result[a] += n; result[b] += n; result[c] += n;
            }
        }
        for (int i = 0; i < result.Length; i++) result[i].Normalize(); normalData = result; dirty = true;
    }
    public void MarkDynamic() => native.MarkDynamic();
    public void UploadMeshData(bool markNoLongerReadable) { Flush(); native.Upload(markNoLongerReadable); }
    private static void CheckChannel(int channel) { if ((uint)channel >= 8) throw new ArgumentOutOfRangeException(nameof(channel)); }
    private void ValidateIndices(int[] indices)
    {
        if (indices.Length % 3 != 0) throw new ArgumentException("三角形索引数量必须是三的倍数。");
        foreach (int index in indices) if ((uint)index >= vertexCount) throw new ArgumentOutOfRangeException(nameof(indices), "网格索引越界。");
    }
    protected override void Release() => native.Dispose();
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
