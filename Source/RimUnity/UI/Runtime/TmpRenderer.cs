using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RimUnity.UI.Runtime;

//按字体图集批量提交 SDF 字形，沿用 UI 变换、颜色和矩形裁切。
internal static class TmpRenderer
{
    private static readonly List<GuiVertex> vertices = new();
    internal static void Draw(UguiRuntime.Entry entry, TextMeshProUGUI text, Color tint)
    {
        Rect content = entry.Rect.rect;
        content.xMin += text.margin.x; content.xMax -= text.margin.z; content.yMax -= text.margin.y; content.yMin += text.margin.w;
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
            Color color = tint * glyph.color;
            Add(entry, rect, uv, Vector2.zero, color); Add(entry, rect, uv, Vector2.up, color); Add(entry, rect, uv, Vector2.one, color);
            Add(entry, rect, uv, Vector2.zero, color); Add(entry, rect, uv, Vector2.one, color); Add(entry, rect, uv, Vector2.right, color);
        }
        if (vertices.Count > 0) UiMeshSubmission.Draw(CollectionsMarshal.AsSpan(vertices), atlas, text.SdfMaterial(atlas));
    }
    private static void Add(UguiRuntime.Entry entry, Rect rect, Rect uv, Vector2 corner, Color color)
        => vertices.Add(new GuiVertex { position = RectTransformUtility.ToGui(entry.Canvas, entry.Rect.TransformPoint(rect.min + Vector2.Scale(rect.size, corner))), uv = uv.min + Vector2.Scale(uv.size, corner), color = color });
}
