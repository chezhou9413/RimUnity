using System.Collections.Generic;
using System.Runtime.InteropServices;
using Brutal.ImGuiApi;

namespace RimUnity.UI.Runtime;

//把宿主字形的位置、样式和图集 UV 转为统一材质网格。
internal static partial class UguiTextRenderer
{
    private static readonly List<GuiVertex> GlyphVertices = new();

    //字号和粗斜体字形与宿主排版使用相同字体，按行共享基线。
    private static void MaterialText(RimUnity.UI.Text text, Rect bounds, Color tint, UguiTextLayout.Layout layout, bool shadow)
    {
        UguiTextLayout.Warm(layout);
        GlyphVertices.Clear();
        Material material = text.material;
        if (material != null) material.SetFloat("_ClTime", Time.unscaledTime);
        float top = bounds.y + (bounds.height - layout.Size.y) * ((int)text.alignment / 3) * 0.5f;
        foreach (UguiTextLayout.Line line in layout.Lines)
        {
            float left = bounds.x + (bounds.width - line.Width) * ((int)text.alignment % 3) * 0.5f;
            for (int i = line.PieceStart; i < line.PieceStart + line.PieceCount; i++)
            {
                UguiTextLayout.Run run = layout.Runs[i];
                if (run.IsSpace) continue;
                var baked = UguiTextLayout.Font(layout.FontIndex, run.Style, run.Size);
                float x = left + run.X;
                float y = top + line.Top * text.lineSpacing + line.Ascent - baked.Ascent;
                Color color = RunColor(run.Color, tint, shadow);
                for (int cursor = run.Start; cursor < run.Start + run.Length; cursor++)
                {
                    uint code = layout.Source[cursor];
                    if (char.IsHighSurrogate(layout.Source[cursor]) && cursor + 1 < run.Start + run.Length && char.IsLowSurrogate(layout.Source[cursor + 1]))
                        code = (uint)char.ConvertToUtf32(layout.Source[cursor++], layout.Source[cursor]);
                    var glyph = baked.FindGlyph(code);
                    if (glyph.IsNull()) continue;
                    if (glyph.X1 > glyph.X0 && glyph.Y1 > glyph.Y0)
                    {
                        GlyphQuad(new Rect(x + glyph.X0, y + glyph.Y0, glyph.X1 - glyph.X0, glyph.Y1 - glyph.Y0),
                            new Rect(glyph.U0, 1f - glyph.V1, glyph.U1 - glyph.U0, glyph.V1 - glyph.V0), color);
                    }
                    x += glyph.AdvanceX;
                }
            }
        }
        Texture atlas = NativeFontAtlas.Capture();
        if (GlyphVertices.Count > 0 && atlas == null)
            throw new System.InvalidOperationException("文字图集尚未由宿主生成。");
        UiMeshSubmission.Draw(CollectionsMarshal.AsSpan(GlyphVertices), atlas, material);
    }

    //GUI 顶点从上向下，纹理 UV 仍采用 Unity 的下向上约定。
    private static void GlyphQuad(Rect rect, Rect uv, Color color)
    {
        if (localEntry != null && localClip)
        {
            Rect r = Rect.MinMaxRect(Mathf.Max(0, rect.xMin), Mathf.Max(0, rect.yMin), Mathf.Min(localRect.width, rect.xMax), Mathf.Min(localRect.height, rect.yMax));
            if (r.width <= 0 || r.height <= 0) return;
            float left = (r.xMin - rect.xMin) / rect.width, right = (r.xMax - rect.xMin) / rect.width;
            float top = (r.yMin - rect.yMin) / rect.height, bottom = (r.yMax - rect.yMin) / rect.height;
            uv = Rect.MinMaxRect(uv.x + uv.width * left, uv.y + uv.height * (1 - bottom), uv.x + uv.width * right, uv.y + uv.height * (1 - top)); rect = r;
        }
        GuiVertex a = new() { position = new Vector2(rect.xMin, rect.yMin), uv = new Vector2(uv.xMin, uv.yMax), color = color };
        GuiVertex b = new() { position = new Vector2(rect.xMax, rect.yMin), uv = new Vector2(uv.xMax, uv.yMax), color = color };
        GuiVertex c = new() { position = new Vector2(rect.xMax, rect.yMax), uv = new Vector2(uv.xMax, uv.yMin), color = color };
        GuiVertex d = new() { position = new Vector2(rect.xMin, rect.yMax), uv = new Vector2(uv.xMin, uv.yMin), color = color };
        a.position = LocalPosition(a.position); b.position = LocalPosition(b.position); c.position = LocalPosition(c.position); d.position = LocalPosition(d.position);
        GlyphVertices.Add(a); GlyphVertices.Add(b); GlyphVertices.Add(c);
        GlyphVertices.Add(a); GlyphVertices.Add(c); GlyphVertices.Add(d);
    }
}
