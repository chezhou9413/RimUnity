using System;

namespace RimUnity;

//显式线段只复用条带生成与材质提交，不进入拖尾的位置采样和寿命模拟。
public sealed partial class TrailRenderer
{
    internal void DrawLine(Vector3[] positions, bool useWorldSpace, Camera camera)
    {
        visible.Clear();
        if (positions.Length < 2 || sharedMaterials.Length == 0) return;
        if (textureMode == LineTextureMode.RepeatPerSegment && positions.Length != 2)
            throw new NotSupportedException("RepeatPerSegment 当前只接通正式资源使用的单段直线。");
        foreach (Vector3 position in positions)
        {
            Vector3 world = useWorldSpace ? position : transform.TransformPoint(position);
            if (visible.Count == 0 || (visible[^1].position - world).sqrMagnitude > 1e-12f)
                visible.Add(new TrailPoint { position = world });
        }
        if (visible.Count < 2) return;
        Build(camera);
        if (indices.Count == 0) return;
        batch ??= new Mesh { name = "RimUnity LineRenderer", indexFormat = IndexFormat.UInt32 };
        FillMesh(batch);
        foreach (Material material in sharedMaterials)
        {
            if (material == null) continue;
            RimUnity.Graphics.DrawMesh(batch, Matrix4x4.identity, material, gameObject.layer);
        }
    }
}
