using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
    private bool sourceUniformNormals, sourceUniformTangents;
    private sealed class MeshGeometry
    {
        internal Vector3[] positions, normals;
        internal Vector2[] uv, uv3, uv4;
        internal Vector4[] tangents;
        internal int[] triangles;
        internal readonly bool uniformNormals, uniformTangents;
        internal MeshGeometry(Mesh source)
        {
            positions = source.vertices; normals = source.normals; uv = source.uv; uv3 = source.uv3; uv4 = source.uv4; tangents = source.tangents; triangles = source.triangles;
            uniformNormals = UniformDirection(normals, positions.Length); uniformTangents = UniformDirection(tangents, positions.Length);
        }
        // Metadata belongs to the existing decoded mesh snapshot, with the same invalidation.
        // Only xyz is transformed; tangent handedness is copied per vertex below.
        // Compare bits so signed zero and NaN payloads cannot merge distinct directions.
        private static bool UniformDirection<T>(T[] values, int vertices) where T : unmanaged
        {
            int count = Math.Min(values.Length, vertices);
            if (count < 2) return false;
            var first = MemoryMarshal.AsBytes(values.AsSpan(0, 1)).Slice(0, 3 * sizeof(float));
            for (int i = 1; i < count; i++)
                if (!first.SequenceEqual(MemoryMarshal.AsBytes(values.AsSpan(i, 1)).Slice(0, first.Length))) return false;
            return true;
        }
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
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detailSelection = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.MeshSelect);
#endif

        int index = MeshIndex(particle);
        Mesh selected = meshes.Length > 0 ? meshes[index] : mesh;
        if (selected == null) throw new InvalidOperationException("Mesh 粒子没有设置网格：" + index);
        if (!meshGeometry.TryGetValue(selected, out MeshGeometry geometry)) meshGeometry.Add(selected, geometry = new MeshGeometry(selected));
        sourceMesh = selected; sourcePositions = geometry.positions; sourceNormals = geometry.normals; sourceUV = geometry.uv;
        sourceTangents = geometry.tangents; sourceTriangles = geometry.triangles;
        sourceUV3 = geometry.uv3; sourceUV4 = geometry.uv4;
        sourceUniformNormals = geometry.uniformNormals; sourceUniformTangents = geometry.uniformTangents;
    }

    private void AppendMeshParticle(ParticleSystem s, in ParticleSystem.Particle p, in ParticleCameraState cameraState,
        Vector3 center, Vector3 size, Rect rect, Color32 color, Vector4 c1, Vector4 c2)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Mesh);
#endif

        SelectMesh(p);
        int offset = vertices.Count;
        // Shape-aligned particles already carry their emission orientation; other meshes obey Renderer alignment.
        Quaternion orientation;
        Matrix4x4 matrix, normalMatrix;
        Vector3 sharedNormal;
        Vector4 sharedTangent;
#if RIMUNITY_PARTICLE_ANALYSIS
        using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.MeshPose))
#endif
        {
            orientation = p.alignedToDirection ? s.ParticleRotation(p) : BillboardAlignment(s, in cameraState, center, streamVelocity);
            matrix = Matrix4x4.TRS(center, orientation * Quaternion.Euler(p.rotation3D), size);
            normalMatrix = sourcePositions.Length != 0 && sourceNormals.Length != 0 ? matrix.inverse.transpose : default;
        }
#if RIMUNITY_PARTICLE_ANALYSIS
        using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.MeshDirections))
#endif
        {
            // Reuse identical source directions only within this particle's current pose.
            sharedNormal = sourceUniformNormals ? normalMatrix.MultiplyVector(sourceNormals[0]).normalized : default;
            sharedTangent = sourceUniformTangents ? (Vector4)matrix.MultiplyVector((Vector3)sourceTangents[0]).normalized : default;
        }
        bool flipMesh = ParticleSystem.Random(p.randomSeed, 82) < flip.z;
        if (sourcePositions.Length != 0 && streamPacking == StreamPacking.General) PrepareParticleStreams(c1, c2);
        if (streamPacking != StreamPacking.General)
            AppendSimpleMesh(matrix, normalMatrix, sharedNormal, sharedTangent, flipMesh, rect, color, c1, c2);
        else
        {
            for (int i = 0; i < sourcePositions.Length; i++)
            {
#if RIMUNITY_PARTICLE_ANALYSIS
                using var detailMeshVertex = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.MeshVertices);
#endif
                streamNormal = i < sourceNormals.Length ? (sourceUniformNormals ? sharedNormal : normalMatrix.MultiplyVector(sourceNormals[i]).normalized) : Vector3.forward;
                streamTangent = i < sourceTangents.Length ? (sourceUniformTangents ? sharedTangent : (Vector4)matrix.MultiplyVector((Vector3)sourceTangents[i]).normalized) : new Vector4(1, 0, 0, 1);
                if (i < sourceTangents.Length) streamTangent.w = sourceTangents[i].w;
                Vector2 source = i < sourceUV.Length ? sourceUV[i] : Vector2.zero;
                streamVertexId = i; streamUV3 = i < sourceUV3.Length ? sourceUV3[i] : Vector2.zero; streamUV4 = i < sourceUV4.Length ? sourceUV4[i] : Vector2.zero;
                Vector3 local = sourcePositions[i] + pivot;
                if (flipMesh) local.z = -local.z;
                Vertex(matrix.MultiplyPoint3x4(local), new Vector2(rect.x + source.x * rect.width, rect.y + source.y * rect.height), color, c1, c2);
            }
        }
#if RIMUNITY_PARTICLE_ANALYSIS
        using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.IndexWrite))
#endif
        foreach (int index in sourceTriangles) indices.Add(offset + index);
    }

    // Simple layouts have no vertex-dependent numeric stream plan. Reserve the
    // owned channel slices once per particle; no source or output span escapes.
    private void AppendSimpleMesh(Matrix4x4 matrix, Matrix4x4 normalMatrix, Vector3 sharedNormal,
        Vector4 sharedTangent, bool flipMesh, Rect rect, Color32 color, Vector4 c1, Vector4 c2)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.MeshVertices);
#endif

        int count = sourcePositions.Length;
        var positionOutput = AppendSlots(vertices, count);
        var uvOutput = AppendSlots(uv, count);
        var packedOutput = AppendSlots(packedUV, count);
        var normalOutput = AppendSlots(normals, count);
        var tangentOutput = AppendSlots(tangents, count);
        AppendSlots(colors, count).Fill(color);
        AppendSlots(custom1, count).Fill(streamPacking == StreamPacking.Default ? c1 : default);
        AppendSlots(custom2, count).Fill(streamPacking == StreamPacking.Default ? c2 : default);
        if (streamPacking == StreamPacking.Zero) packedOutput.Clear();
        for (int i = 0; i < count; i++)
        {
            streamNormal = i < sourceNormals.Length ? (sourceUniformNormals ? sharedNormal : normalMatrix.MultiplyVector(sourceNormals[i]).normalized) : Vector3.forward;
            streamTangent = i < sourceTangents.Length ? (sourceUniformTangents ? sharedTangent : (Vector4)matrix.MultiplyVector((Vector3)sourceTangents[i]).normalized) : new Vector4(1, 0, 0, 1);
            if (i < sourceTangents.Length) streamTangent.w = sourceTangents[i].w;
            Vector2 source = i < sourceUV.Length ? sourceUV[i] : Vector2.zero;
            streamVertexId = i; streamUV3 = i < sourceUV3.Length ? sourceUV3[i] : Vector2.zero; streamUV4 = i < sourceUV4.Length ? sourceUV4[i] : Vector2.zero;
            Vector3 local = sourcePositions[i] + pivot;
            if (flipMesh) local.z = -local.z;
            Vector2 texcoord = new(rect.x + source.x * rect.width, rect.y + source.y * rect.height);
            positionOutput[i] = matrix.MultiplyPoint3x4(local); uvOutput[i] = texcoord;
            if (streamPacking != StreamPacking.Zero) packedOutput[i] = new Vector4(texcoord.x, texcoord.y, 0, 0);
            normalOutput[i] = streamNormal; tangentOutput[i] = streamTangent;
        }
    }
}
