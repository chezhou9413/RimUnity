using System;
using System.Collections.Generic;
using ShaderRegistry = Waylith.Rendering.ShaderRegistry;

namespace RimUnity;

//CPU 只计算每根骨骼的矩阵，顶点权重混合在 Waylith 顶点 Shader 中执行。
public sealed partial class SkinnedMeshRenderer : MonoBehaviour
{
    internal static readonly HashSet<SkinnedMeshRenderer> Active = new();
    public Mesh sharedMesh;
    public Material[] sharedMaterials = Array.Empty<Material>();
    public Transform[] bones = Array.Empty<Transform>();
    public Transform rootBone;
    public Bounds localBounds;
    public bool updateWhenOffscreen;
    public Material sharedMaterial { get => sharedMaterials.Length == 0 ? null : sharedMaterials[0]; set => sharedMaterials = new[] { value }; }
    private Mesh source, gpuMesh;
    private Matrix4x4[] bindposes, matrices = Array.Empty<Matrix4x4>();
    private ComputeBuffer buffer, weightsBuffer;
    private readonly MaterialPropertyBlock properties = new();
    private readonly Dictionary<Material, Material> materials = new();

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
    private void OnDestroy()
    {
        Active.Remove(this); buffer?.Dispose(); weightsBuffer?.Dispose();
        if (gpuMesh != null) Destroy(gpuMesh);
        foreach (var material in materials.Values) Destroy(material);
        materials.Clear();
    }
    //原网格共享缓存，不写回实例专用权重通道或骨骼矩阵。
    private void Prepare()
    {
        if (source == sharedMesh) return;
        if (gpuMesh != null) Destroy(gpuMesh);
        source = sharedMesh;
        if (source == null) { gpuMesh = null; return; }
        bindposes = source.bindposes;
        var weights = Rendering.SkinWeights.Get(source);
        if (bindposes.Length == 0 || bones.Length != bindposes.Length) throw new InvalidOperationException("骨骼数量、Bind Pose 与顶点权重不匹配：" + name);
        gpuMesh = new Mesh { name = source.name + " GPU Skin", indexFormat = IndexFormat.UInt32, vertices = source.vertices, normals = source.normals, tangents = source.tangents, uv = source.uv, colors = source.colors, subMeshCount = source.subMeshCount };
        for (int i = 0; i < source.subMeshCount; i++) gpuMesh.SetTriangles(source.GetIndices(i), i);
        foreach (var influence in weights.influences) if (influence.x >= bones.Length) throw new InvalidOperationException("骨骼索引越界：" + source.name);
        gpuMesh.SetUVs(6, weights.ranges);
        weightsBuffer?.Dispose(); weightsBuffer = new ComputeBuffer(Math.Max(1, weights.influences.Length), 16);
        weightsBuffer.SetData(weights.influences.Length == 0 ? new Vector4[1] : weights.influences); properties.SetBuffer("_SkinWeights", weightsBuffer);
        gpuMesh.bounds = localBounds.size.sqrMagnitude > 0 ? localBounds : source.bounds;
        matrices = new Matrix4x4[bones.Length]; buffer?.Dispose(); buffer = new ComputeBuffer(bones.Length, 64); properties.SetBuffer("_Bones", buffer);
    }
    internal void Draw(Camera camera)
    {
        if (!isActiveAndEnabled || sharedMesh == null) return;
        Prepare();
        Matrix4x4 inverse = transform.worldToLocalMatrix;
        for (int i = 0; i < matrices.Length; i++) matrices[i] = inverse * bones[i].localToWorldMatrix * bindposes[i];
        buffer.SetData(matrices);
        for (int i = 0; i < gpuMesh.subMeshCount && i < sharedMaterials.Length; i++)
        {
            var material = sharedMaterials[i]; if (material == null) continue;
            if (!materials.TryGetValue(material, out var adapted))
            {
                string shaderName = material.shader.name switch { "RimUnity_Unlit" => "RimUnity_SkinnedUnlit", "RimUnity_Transparent" => "RimUnity_SkinnedTransparent", _ => throw new NotSupportedException("此 Shader 尚未提供 GPU 蒙皮入口：" + material.shader.name) };
                if (!ShaderRegistry.TryResolve(shaderName, out var shader)) throw new InvalidOperationException("未注册蒙皮 Shader：" + shaderName);
                adapted = new Material(Shader.FromEngineShader(shaderName, shader)); materials.Add(material, adapted);
            }
            adapted.CopyPropertiesFromMaterial(material);
            RimUnity.Graphics.DrawMesh(gpuMesh, transform.localToWorldMatrix, adapted, gameObject.layer, i, properties);
        }
    }
}
