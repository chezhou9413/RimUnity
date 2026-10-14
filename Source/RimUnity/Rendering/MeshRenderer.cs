using System;
using System.Collections.Generic;

namespace RimUnity;

//按子网格与 Shader Pass 顺序提交绘制。
public sealed class MeshRenderer : MonoBehaviour
{
    internal static readonly HashSet<MeshRenderer> Active = new();
    public Material[] sharedMaterials = Array.Empty<Material>();
    public int sortingOrder;
    public Material sharedMaterial { get => sharedMaterials.Length == 0 ? null : sharedMaterials[0]; set => sharedMaterials = new[] { value }; }
    private MeshFilter filter;
    private readonly List<Material> ownedMaterials = new();

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

    private void OnEnable() { filter = GetComponent<MeshFilter>(); Active.Add(this); }
    private void OnDisable() => Active.Remove(this);

    //Unity 对超出子网格数量的材质重复绘制最后一个子网格。
    internal void Draw(Camera camera)
    {
        if (!isActiveAndEnabled || filter == null || filter.sharedMesh == null) return;
        Mesh mesh = filter.sharedMesh;
        if (mesh.subMeshCount == 0) return;
        Matrix4x4 matrix = transform.localToWorldMatrix;
        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            Material material = sharedMaterials[i];
            if (material == null) continue;
            int submesh = Math.Min(i, mesh.subMeshCount - 1);
            if (Diagnostics.ParticleDebugWindow.Capturing)
                Diagnostics.RendererDebugCapture.Capture(this, mesh, material, "MeshRenderer", 1, Array.Empty<Color>());
            RimUnity.Graphics.DrawMesh(mesh, matrix, material, gameObject.layer, submesh);
        }
    }

    private void OnDestroy()
    {
        Active.Remove(this);
        foreach (Material owned in ownedMaterials) Destroy(owned);
    }
}
