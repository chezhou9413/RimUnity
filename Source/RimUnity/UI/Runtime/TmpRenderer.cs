using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RimUnity.UI.Runtime;

//按字体图集批量提交 SDF 字形，沿用 UI 变换、颜色和矩形裁切。
internal static class TmpRenderer
{
    private static readonly List<GuiVertex> vertices = new();
    internal static void Draw(UguiRuntime.Entry entry, TextMeshProUGUI text, Color tint, Color multiplier)
    {
        Rect content = entry.Rect.rect;
        content.xMin += text.margin.x; content.xMax -= text.margin.z; content.yMax -= text.margin.y; content.yMin += text.margin.w;
        TMP_InputField input = text.GetComponentInParent<TMP_InputField>();
        if (input != null && input.textComponent == text && input.isFocused)
        {
            Rect decoration = input.Decoration(out bool selection, out bool visible);
            if (!input.readOnly)
            {
                //RimWorld 1.7.5014 rev1195 的 UnityStubs.Internal.ImeComposition 已实现候选窗物理坐标上报（避免依赖可能未缩放的 GUI.matrix）。
                Vector2 candidateGui = RectTransformUtility.ToGui(entry.Canvas,
                    entry.Rect.TransformPoint(new Vector3(selection ? decoration.xMax : decoration.xMin, decoration.yMin, 0f)));
                UnityStubs.Internal.ImeComposition.SetCandidatePos(new UnityEngine.Vector2(candidateGui.x * Verse.Prefs.UIScale, candidateGui.y * Verse.Prefs.UIScale));
            }
            if (visible)
            {
                Color color = selection ? input.selectionColor : input.customCaretColor ? input.caretColor : text.color;
                color.a *= entry.Alpha;
                vertices.Clear();
                Add(entry, decoration, new Rect(0, 0, 1, 1), Vector2.zero, color); Add(entry, decoration, new Rect(0, 0, 1, 1), Vector2.up, color);
                Add(entry, decoration, new Rect(0, 0, 1, 1), Vector2.one, color); Add(entry, decoration, new Rect(0, 0, 1, 1), Vector2.zero, color);
                Add(entry, decoration, new Rect(0, 0, 1, 1), Vector2.one, color); Add(entry, decoration, new Rect(0, 0, 1, 1), Vector2.right, color);
                UiMeshSubmission.Draw(CollectionsMarshal.AsSpan(vertices), Texture2D.whiteTexture, UiMaskData.DefaultMaterial());
            }
            content.xMin -= input.ScrollOffset; content.xMax -= input.ScrollOffset;
        }
        text.Build(content.width, content.height);
        Texture2D atlas = null; vertices.Clear();
        foreach (var glyph in text.quads)
        {
            if (atlas != glyph.atlas && vertices.Count > 0) { UiMeshSubmission.Draw(CollectionsMarshal.AsSpan(vertices), atlas, text.SdfMaterial(atlas)); vertices.Clear(); }
            atlas = glyph.atlas;
            Vector2 offset = text.Offset(glyph.line, content);
            Rect rect = new(offset.x + glyph.rect.x, offset.y - glyph.rect.y - glyph.rect.height, glyph.rect.width, glyph.rect.height);
            Rect uv = glyph.uv;
            if (text.overflowMode == TextOverflowModes.Masking)
            {
                Rect visible = Rect.MinMaxRect(Mathf.Max(rect.xMin, content.xMin), Mathf.Max(rect.yMin, content.yMin), Mathf.Min(rect.xMax, content.xMax), Mathf.Min(rect.yMax, content.yMax));
                if (visible.width <= 0 || visible.height <= 0) continue;
                uv = new Rect(uv.x + (visible.xMin - rect.xMin) / rect.width * uv.width, uv.y + (visible.yMin - rect.yMin) / rect.height * uv.height,
                    visible.width / rect.width * uv.width, visible.height / rect.height * uv.height);
                rect = visible;
            }
            //对齐 TMP 3.0.6 SaveGlyphVertexInfo 与 UGUI CanvasRenderer：富文本替换 RGB，显式 Alpha 取 min(字体 Alpha, 标签 Alpha)，再乘外层淡出/状态系数。
            Color color;
            if (glyph.hasColor)
            {
                float glyphAlpha = glyph.hasExplicitAlpha ? Mathf.Min(text.color.a, glyph.color.a) : text.color.a;
                color = new Color(glyph.color.r * multiplier.r, glyph.color.g * multiplier.g, glyph.color.b * multiplier.b, glyphAlpha * multiplier.a);
            }
            else
            {
                color = tint;
            }
            Add(entry, rect, uv, Vector2.zero, color, glyph.bottomShear); Add(entry, rect, uv, Vector2.up, color, glyph.topShear); Add(entry, rect, uv, Vector2.one, color, glyph.topShear);
            Add(entry, rect, uv, Vector2.zero, color, glyph.bottomShear); Add(entry, rect, uv, Vector2.one, color, glyph.topShear); Add(entry, rect, uv, Vector2.right, color, glyph.bottomShear);
        }
        if (vertices.Count > 0) UiMeshSubmission.Draw(CollectionsMarshal.AsSpan(vertices), atlas, text.SdfMaterial(atlas));
    }
    private static void Add(UguiRuntime.Entry entry, Rect rect, Rect uv, Vector2 corner, Color color, float shear = 0)
        => vertices.Add(new GuiVertex { position = RectTransformUtility.ToGui(entry.Canvas, entry.Rect.TransformPoint(rect.min + Vector2.Scale(rect.size, corner) + new Vector2(shear, 0))), uv = uv.min + Vector2.Scale(uv.size, corner), color = color });
}
