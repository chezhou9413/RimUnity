using System;

namespace RimUnity.UI;

//完成排版后裁去不可见行，省略号占用实际字形宽度。
public sealed partial class TextMeshProUGUI
{
    private void ApplyOverflow(float width, float height, float size)
    {
        if (overflowMode is not (TextOverflowModes.Truncate or TextOverflowModes.Ellipsis)) return;
        int lines = lineBottoms.FindIndex(bottom => bottom > height + .001f);
        if (lines < 0) lines = lineBottoms.Count;
        if (lines == 0) { quads.Clear(); measured = Vector2.zero; return; }
        bool verticalOverflow = lines < lineBottoms.Count;
        quads.RemoveAll(q => q.line >= lines);
        for (int line = 0; line < lines; line++)
        {
            bool overflow = lineWidths[line] > width || verticalOverflow && line == lines - 1;
            if (!overflow) continue;
            int currentLine = line;
            quads.RemoveAll(q => q.line == currentLine && q.rect.xMax > width);
            if (overflowMode == TextOverflowModes.Ellipsis)
            {
                if (!font.TryGlyph(0x2026, out var glyph, out var owner))
                    throw new InvalidOperationException("TMP 省略模式需要字体包含 U+2026。");
                float scale = size / owner.pointSize * owner.scale * glyph.scale;
                float advance = glyph.advance * scale;
                quads.RemoveAll(q => q.line == currentLine && q.rect.xMax + advance > width);
                int last = quads.FindLastIndex(q => q.line == currentLine);
                float x = last >= 0 ? quads[last].rect.xMax : 0;
                if (advance <= width)
                {
                    var atlas = owner.atlasTextures[glyph.atlasIndex];
                    float pad = owner.atlasPadding;
                    float top = line == 0 ? 0 : lineBottoms[line - 1] + lineSpacing;
                    quads.Add(new GlyphQuad { line = line, atlas = atlas, color = last >= 0 ? quads[last].color : Color.white,
                        rect = new Rect(x + (glyph.bearingX - pad) * scale, top + (owner.ascentLine - glyph.bearingY - pad) * scale,
                            (glyph.width + 2 * pad) * scale, (glyph.height + 2 * pad) * scale),
                        uv = new Rect((glyph.rect.x - pad) / atlas.width, (glyph.rect.y - pad) / atlas.height,
                            (glyph.rect.width + 2 * pad) / atlas.width, (glyph.rect.height + 2 * pad) / atlas.height) });
                    lineWidths[line] = x + advance;
                }
                else lineWidths[line] = 0;
            }
            else
            {
                int last = quads.FindLastIndex(q => q.line == currentLine);
                lineWidths[line] = last >= 0 ? quads[last].rect.xMax : 0;
            }
        }
        float maximum = 0;
        for (int line = 0; line < lines; line++) maximum = Math.Max(maximum, lineWidths[line]);
        measured = new Vector2(maximum, lineBottoms[lines - 1]);
    }
}
