using System;
using System.Collections.Generic;

namespace RimUnity;

//线段保留输入点顺序与坐标空间，通过现有条带几何绘制实际宽度、渐变和材质。
public sealed partial class LineRenderer : MonoBehaviour
{
    internal static readonly HashSet<LineRenderer> Active = new();
    public Vector3[] positions = Array.Empty<Vector3>();
    public Material[] sharedMaterials = Array.Empty<Material>();
    public bool useWorldSpace = true, loop, generateLightingData;
    public float widthMultiplier = 1;
    public AnimationCurve widthCurve = AnimationCurve.Constant(0, 1, 1);
    public Gradient colorGradient = new();
    public LineAlignment alignment;
    public LineTextureMode textureMode;
    public Vector2 textureScale = Vector2.one;
    public int numCornerVertices, numCapVertices, sortingOrder;
    private TrailRenderer geometry;
    public Material sharedMaterial { get => sharedMaterials.Length > 0 ? sharedMaterials[0] : null; set => sharedMaterials = new[] { value }; }
    public int positionCount
    {
        get => positions.Length;
        set { ArgumentOutOfRangeException.ThrowIfNegative(value); Array.Resize(ref positions, value); }
    }

    //动画访问 material 时实例化独立材质，保持所有转换后的 Pass。
    public Material material
    {
        get
        {
            EnsureGeometry(); geometry.sharedMaterials = sharedMaterials;
            Material value = geometry.material; sharedMaterials = geometry.sharedMaterials; return value;
        }
        set => sharedMaterial = value;
    }

    //组件生命周期只登记绘制；点的位置由导出数据、动画或调用方控制。
    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
    public void SetPosition(int index, Vector3 value) => positions[index] = value;
    public Vector3 GetPosition(int index) => positions[index];
    public void SetPositions(Vector3[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Array.Copy(values, positions, Math.Min(values.Length, positions.Length));
    }
    public int GetPositions(Vector3[] values)
    {
        ArgumentNullException.ThrowIfNull(values); int count = Math.Min(values.Length, positions.Length);
        Array.Copy(positions, values, count); return count;
    }

    //禁用辅助组件的 Update 与独立绘制，只由本组件显式调用条带生成。
    private void EnsureGeometry()
    {
        if (geometry != null) return;
        geometry = gameObject.AddComponent<TrailRenderer>(); geometry.enabled = false;
    }
    internal void Draw(Camera camera)
    {
        if (!isActiveAndEnabled || positions.Length < 2 || sharedMaterials.Length == 0) return;
        if (loop) throw new NotSupportedException("当前正式资源未使用闭合 LineRenderer，闭合接缝尚未接通。");
        if (textureMode != LineTextureMode.Stretch && textureMode != LineTextureMode.Tile && textureMode != LineTextureMode.RepeatPerSegment)
            throw new NotSupportedException("不支持的 LineRenderer 贴图模式：" + textureMode);
        EnsureGeometry();
        geometry.sharedMaterials = sharedMaterials; geometry.widthMultiplier = widthMultiplier;
        geometry.widthCurve = widthCurve; geometry.colorGradient = colorGradient;
        geometry.alignment = alignment; geometry.textureMode = textureMode; geometry.textureScale = textureScale;
        geometry.numCornerVertices = numCornerVertices; geometry.numCapVertices = numCapVertices;
        geometry.generateLightingData = generateLightingData;
        geometry.DrawLine(positions, useWorldSpace, camera);
    }
    private void OnDestroy() { Active.Remove(this); if (geometry != null) Destroy(geometry); }
}
