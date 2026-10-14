using System;
using System.Collections.Generic;

namespace RimUnity;

public enum ParticleSystemMeshDistribution { UniformRandom, NonUniformRandom }

//多个发射网格共享解码缓存，随机分配在同一粒子种子下保持稳定。
public sealed partial class ParticleSystemRenderer
{
    private Mesh primaryMesh;
    public Mesh mesh
    {
        get => meshes.Length > 0 ? meshes[0] : primaryMesh;
        set { primaryMesh = value; meshes = value != null ? new[] { value } : Array.Empty<Mesh>(); meshGeometry.Clear(); }
    }
    public Mesh[] meshes = Array.Empty<Mesh>();
    public float[] meshWeightings = Array.Empty<float>();
    public ParticleSystemMeshDistribution meshDistribution;
    private readonly Dictionary<Mesh, MeshGeometry> meshGeometry = new();
    private sealed class MeshGeometry
    {
        internal Vector3[] positions, normals;
        internal Vector2[] uv, uv3, uv4;
        internal Vector4[] tangents;
        internal int[] triangles;
        internal MeshGeometry(Mesh source)
        { positions = source.vertices; normals = source.normals; uv = source.uv; uv3 = source.uv3; uv4 = source.uv4; tangents = source.tangents; triangles = source.triangles; }
    }
    public int meshCount => meshes.Length > 0 ? meshes.Length : mesh != null ? 1 : 0;
    public int GetMeshes(Mesh[] target)
    {
        ArgumentNullException.ThrowIfNull(target);
        int n = Math.Min(meshCount, target.Length);
        for (int i = 0; i < n; i++) target[i] = meshes.Length > 0 ? meshes[i] : mesh;
        return n;
    }
    public void SetMeshes(Mesh[] values) => SetMeshes(values, values.Length);
    public void SetMeshes(Mesh[] values, int size)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (size < 0 || size > values.Length) throw new ArgumentOutOfRangeException(nameof(size));
        meshes = new Mesh[size]; Array.Copy(values, meshes, size); meshGeometry.Clear();
        primaryMesh = size > 0 ? meshes[0] : null;
    }
    public int GetMeshWeightings(float[] target)
    { int n = Math.Min(target.Length, meshWeightings.Length); Array.Copy(meshWeightings, target, n); return n; }
    public void SetMeshWeightings(float[] values) => SetMeshWeightings(values, values.Length);
    public void SetMeshWeightings(float[] values, int size)
    {
        if (size < 0 || size > values.Length) throw new ArgumentOutOfRangeException(nameof(size));
        meshWeightings = new float[size]; Array.Copy(values, meshWeightings, size);
    }
    //显式粒子覆盖优先于系统随机分布，非法索引不会静默选择其他网格。
    internal int MeshIndex(ParticleSystem.Particle particle)
    {
        int n = meshCount;
        if (n == 0) return 0;
        if (particle.meshIndexOverride.HasValue)
        {
            int index = particle.meshIndexOverride.Value;
            if ((uint)index >= n) throw new InvalidOperationException("粒子网格索引超出 Renderer 的 Mesh 列表。");
            return index;
        }
        float random = ParticleSystem.Random(particle.randomSeed, 226);
        if (meshDistribution == ParticleSystemMeshDistribution.UniformRandom) return Math.Min(n - 1, (int)(random * n));
        if (meshWeightings.Length != n) throw new InvalidOperationException("Mesh Weightings 数量必须等于网格数量。");
        float total = 0;
        foreach (float weight in meshWeightings)
        { if (weight < 0 || !float.IsFinite(weight)) throw new InvalidOperationException("网格权重必须为非负有限数。"); total += weight; }
        if (total <= 0) throw new InvalidOperationException("网格权重之和必须大于零。");
        float cursor = random * total;
        for (int i = 0; i < n; i++) { cursor -= meshWeightings[i]; if (cursor < 0) return i; }
        return n - 1;
    }
    private void SelectMesh(ParticleSystem.Particle particle)
    {
        int index = MeshIndex(particle);
        Mesh selected = meshes.Length > 0 ? meshes[index] : mesh;
        if (selected == null) throw new InvalidOperationException("Mesh 粒子没有设置网格：" + index);
        if (!meshGeometry.TryGetValue(selected, out MeshGeometry geometry)) meshGeometry.Add(selected, geometry = new MeshGeometry(selected));
        sourceMesh = selected; sourcePositions = geometry.positions; sourceNormals = geometry.normals; sourceUV = geometry.uv;
        sourceTangents = geometry.tangents; sourceTriangles = geometry.triangles;
        sourceUV3 = geometry.uv3; sourceUV4 = geometry.uv4;
    }
}
