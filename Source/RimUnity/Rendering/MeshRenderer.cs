using System;
using System.Collections.Generic;

namespace RimUnity;

//按子网格与 Shader Pass 顺序提交绘制。
public sealed class MeshRenderer : MonoBehaviour
{
    internal static readonly HashSet<MeshRenderer> Active = new();
    public Material[] sharedMaterials = Array.Empty<Material>();
    public int sortingOrder;
    public Rendering.ShadowCastingMode shadowCastingMode;
    public bool receiveShadows = true;
    public Material sharedMaterial { get => sharedMaterials.Length == 0 ? null : sharedMaterials[0]; set => sharedMaterials = new[] { value }; }
    private MeshFilter filter;
    private readonly List<Material> ownedMaterials = new();
    private readonly MaterialPropertyBlock properties = new();
    //变换八个角点，支持层级旋转与非均匀缩放。
    public Bounds bounds
    {
        get
        {
            var local = (filter ?? GetComponent<MeshFilter>())?.sharedMesh?.bounds ?? new Bounds();
            var matrix = transform.localToWorldMatrix;
            var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++) result.Encapsulate(matrix.MultiplyPoint3x4(local.center + new Vector3(
                (i & 1) == 0 ? -local.extents.x : local.extents.x, (i & 2) == 0 ? -local.extents.y : local.extents.y,
                (i & 4) == 0 ? -local.extents.z : local.extents.z)));
            return result;
        }
    }

    //与 Unity 一样，访问 material 时从共享材质创建当前实例的副本。
    public Material material
    {
        get
        {
            Material source = sharedMaterial;
            if (source == null || ownedMaterials.Contains(source)) return source;
            var clone = new Material(source);
            sharedMaterials = (Material[])sharedMaterials.Clone(); sharedMaterials[0] = clone;
            ownedMaterials.Add(clone);
            return clone;
        }
        set => sharedMaterial = value;
    }

    //批量访问时同样隔离实例材质，避免建筑参数改动共享模板。
    public Material[] materials
    {
        get
        {
            var result = (Material[])sharedMaterials.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] == null || ownedMaterials.Contains(result[i])) continue;
                result[i] = new Material(result[i]);
                ownedMaterials.Add(result[i]);
            }
            sharedMaterials = result;
            return (Material[])result.Clone();
        }
        set => sharedMaterials = (Material[])value.Clone();
    }

    private void OnEnable() { filter = GetComponent<MeshFilter>(); Active.Add(this); }
    private void OnDisable() => Active.Remove(this);

    //Unity 对超出子网格数量的材质重复绘制最后一个子网格。
    internal void Draw(Camera camera, bool shadow = false)
    {
        if (!isActiveAndEnabled || filter == null || filter.sharedMesh == null) return;
        if (shadow ? shadowCastingMode == Rendering.ShadowCastingMode.Off : shadowCastingMode == Rendering.ShadowCastingMode.ShadowsOnly) return;
        Mesh mesh = filter.sharedMesh;
        if (mesh.subMeshCount == 0) return;
        Matrix4x4 matrix = transform.localToWorldMatrix;
        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            Material material = sharedMaterials[i];
            if (material == null) continue;
            if (shadow) material = material.GetShadowMaterial();
            if (material == null) continue;
            int submesh = Math.Min(i, mesh.subMeshCount - 1);
            properties.SetFloat("_RU_DisableShadows", receiveShadows ? 0 : 1);
            if (!shadow && Diagnostics.ParticleDebugWindow.Capturing)
                Diagnostics.RendererDebugCapture.Capture(this, mesh, material, "MeshRenderer", 1, Array.Empty<Color>());
            RimUnity.Graphics.DrawMesh(mesh, matrix, material, gameObject.layer, submesh, properties);
        }
    }

    private void OnDestroy()
    {
        Active.Remove(this);
        foreach (Material owned in ownedMaterials) Destroy(owned);
    }
}
