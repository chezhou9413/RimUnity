using System;
using Verse;

namespace RimUnity.UI.Runtime;

//片元阶段按局部矩形精确裁切，保留旋转、镜像和羽化，不依赖屏幕轴向剪刀框。
internal static class UiRectMaskData
{
    private static readonly Vector4[] rows = new Vector4[32];
    private static readonly ComputeBuffer buffer = new(32, 16);
    internal static Material Apply(Material material)
    {
        var entry = UiMaskData.Current;
        int count = entry?.RectMasks.Length ?? 0;
        if (count == 0) { if (material != null && material.HasProperty("_UiRectMaskCount")) material.SetFloat("_UiRectMaskCount", 0); return material; }
        if (count > 8) throw new NotSupportedException("RectMask2D 嵌套不能超过八层。");
        material ??= UiMaskData.DefaultMaterial();
        if (!material.HasProperty("_UiRectMaskCount")) throw new NotSupportedException("UI Shader 缺少 RectMask2D 裁切入口：" + material.shader.name);
        material.SetFloat("_UiRectMaskCount", count);
        for (int i = 0; i < count; i++)
        {
            var mask = entry.RectMasks[i]; var transform = (RectTransform)mask.transform; Rect rect = transform.rect;
            rect.xMin += mask.padding.x; rect.yMin += mask.padding.y; rect.xMax -= mask.padding.z; rect.yMax -= mask.padding.w;
            if (rect.width <= 0 || rect.height <= 0) { rows[i * 4] = new Vector4(0, 0, -1, 0); rows[i * 4 + 1] = Vector4.zero; rows[i * 4 + 2] = new Vector4(0, 0, 1, 0); rows[i * 4 + 3] = Vector4.zero; continue; }
            Vector2 P(float x, float y) => RectTransformUtility.ToGui(entry.Canvas, transform.TransformPoint(new Vector3(x, y))) * Prefs.UIScale;
            Matrix4x4 inverse = UiMaskData.Homography(P(rect.xMin, rect.yMin), P(rect.xMax, rect.yMin), P(rect.xMax, rect.yMax), P(rect.xMin, rect.yMax)).inverse;
            rows[i * 4] = new Vector4(inverse.m00, inverse.m01, inverse.m03, 0);
            rows[i * 4 + 1] = new Vector4(inverse.m10, inverse.m11, inverse.m13, 0);
            rows[i * 4 + 2] = new Vector4(inverse.m30, inverse.m31, inverse.m33, 0);
            rows[i * 4 + 3] = new Vector4(Mathf.Max(0, mask.softness.x) / rect.width, Mathf.Max(0, mask.softness.y) / rect.height, 0, 0);
        }
        buffer.SetData(rows); material.SetBuffer("_UiRectMasks", buffer); return material;
    }
}
