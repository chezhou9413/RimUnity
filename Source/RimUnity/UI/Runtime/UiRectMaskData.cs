using System;
using Verse;

namespace RimUnity.UI.Runtime;

//片元阶段按局部矩形精确裁切，保留旋转、镜像和羽化；
//RimWorld 1.7.5014 rev1195 的 ImGuiMaterialRenderer 对单帧自定义材质绘制存在硬上限（MaterialDrawsPerFrame = 256，MaxSets = 512，
//带 PrecompiledStages 的 UI 着色器每次占 2 个 UBO 与 2 个 DescriptorSet；超限后 TryRecord 失败并回退默认 ImGui 管线，导致 TMP 直接画出裸 SDF 图集）。
//因此轴对齐且无羽化的 ScreenSpaceOverlay RectMask2D 直接沿用 NativeGuiHost.PushClip 的硬件 Scissor 裁切，不再升级为自定义材质占用槽位。
internal static class UiRectMaskData
{
    private static readonly Vector4[] rows = new Vector4[32];
    private static readonly ComputeBuffer buffer = new(32, 16);

    internal static bool NeedsShader(UguiRuntime.Entry entry)
    {
        if (entry == null || entry.RectMasks.Length == 0) return false;
        foreach (var mask in entry.RectMasks)
            if (!CoveredByClip(entry, mask)) return true;
        return false;
    }

    private static bool CoveredByClip(UguiRuntime.Entry entry, RectMask2D mask)
    {
        if (WorldUiSubmission.Current != null || entry.Canvas.EffectiveMode != RenderMode.ScreenSpaceOverlay) return false;
        if (mask.softness.x > 0f || mask.softness.y > 0f) return false;
        if (mask.transform is not RectTransform rectTransform || !rectTransform.IsChildOf(entry.Canvas.rootCanvas.transform)) return false;
        Matrix4x4 world = rectTransform.localToWorldMatrix;
        return Mathf.Abs(world.m01) <= 0.0001f && Mathf.Abs(world.m10) <= 0.0001f;
    }

    internal static Material Apply(Material material)
    {
        var entry = UiMaskData.Current;
        var rectMasks = entry?.RectMasks;
        int shaderCount = 0;
        if (rectMasks != null)
            foreach (var mask in rectMasks)
                if (!CoveredByClip(entry, mask)) shaderCount++;
        if (shaderCount == 0) { if (material != null && material.HasProperty("_UiRectMaskCount")) material.SetFloat("_UiRectMaskCount", 0); return material; }
        if (shaderCount > 8) throw new NotSupportedException("RectMask2D 嵌套不能超过八层。");
        material ??= UiMaskData.DefaultMaterial();
        if (!material.HasProperty("_UiRectMaskCount")) throw new NotSupportedException("UI Shader 缺少 RectMask2D 裁切入口：" + material.shader.name);
        material.SetFloat("_UiRectMaskCount", shaderCount);
        UguiProjection projection = new(entry.Canvas);
        int slot = 0;
        for (int i = 0; i < rectMasks.Length; i++)
        {
            var mask = rectMasks[i];
            if (CoveredByClip(entry, mask)) continue;
            var transform = (RectTransform)mask.transform; Rect rect = transform.rect;
            rect.xMin += mask.padding.x; rect.yMin += mask.padding.y; rect.xMax -= mask.padding.z; rect.yMax -= mask.padding.w;
            if (rect.width <= 0 || rect.height <= 0) { rows[slot * 4] = new Vector4(0, 0, -1, 0); rows[slot * 4 + 1] = Vector4.zero; rows[slot * 4 + 2] = new Vector4(0, 0, 1, 0); rows[slot * 4 + 3] = Vector4.zero; slot++; continue; }
            Matrix4x4 world = transform.localToWorldMatrix;
            Vector2 P(float x, float y) => projection.ToGui(world.MultiplyPoint3x4(new Vector3(x, y))) * Prefs.UIScale;
            Matrix4x4 inverse = UiMaskData.Homography(P(rect.xMin, rect.yMin), P(rect.xMax, rect.yMin), P(rect.xMax, rect.yMax), P(rect.xMin, rect.yMax)).inverse;
            rows[slot * 4] = new Vector4(inverse.m00, inverse.m01, inverse.m03, 0);
            rows[slot * 4 + 1] = new Vector4(inverse.m10, inverse.m11, inverse.m13, 0);
            rows[slot * 4 + 2] = new Vector4(inverse.m30, inverse.m31, inverse.m33, 0);
            rows[slot * 4 + 3] = new Vector4(Mathf.Max(0, mask.softness.x) / rect.width, Mathf.Max(0, mask.softness.y) / rect.height, 0, 0);
            slot++;
        }
        buffer.SetData(rows); material.SetBuffer("_UiRectMasks", buffer); return material;
    }
}
