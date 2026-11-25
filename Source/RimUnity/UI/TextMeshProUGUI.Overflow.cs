using System;

namespace RimUnity.UI;

//完成排版后裁去不可见行，省略号按真实步进宽度裁剪并在缺失 U+2026 时安全降级为截断。
public sealed partial class TextMeshProUGUI
{
    private void ApplyOverflow(float width, float height, float size)
    {
        if (overflowMode is not (TextOverflowModes.Truncate or TextOverflowModes.Ellipsis)) return;
        if (lineBottoms.Count == 0) return;

        //使用可见字高（ascent - descent）判断垂直溢出，避免紧凑单行框因 lineGap 被整行清空。
        int lines = _lineVisibleBottoms.FindIndex(bottom => bottom > height + 0.001f);
        if (lines < 0) lines = lineBottoms.Count;
        if (lines == 0 && height > 0f && lineBottoms.Count == 1) lines = 1;
        if (lines == 0) { quads.Clear(); measured = Vector2.zero; _hasVisualBounds = false; return; }

        bool verticalOverflow = lines < lineBottoms.Count;
        quads.RemoveAll(q => q.line >= lines);

        float emLineSpacing = lineSpacing * size * 0.01f;
        bool ellipsisPlaced = false;

        for (int line = 0; line < lines; line++)
        {
            if (ellipsisPlaced)
            {
                int skipLine = line;
                quads.RemoveAll(q => q.line == skipLine);
                lineWidths[line] = 0f;
                continue;
            }

            bool overflow = lineWidths[line] > width + 0.001f || (verticalOverflow && line == lines - 1);
            if (!overflow) continue;

            int currentLine = line;
            quads.RemoveAll(q => q.line == currentLine && q.advanceEnd > width + 0.001f);

            if (overflowMode == TextOverflowModes.Ellipsis && font.TryGlyph(0x2026, out var glyph, out var owner))
            {
                float scale = size / owner.pointSize * owner.scale * glyph.scale;
                float advance = glyph.advance * scale;
                quads.RemoveAll(q => q.line == currentLine && q.advanceEnd + advance > width + 0.001f);
                int last = quads.FindLastIndex(q => q.line == currentLine);
                float x = last >= 0 ? quads[last].advanceEnd : 0f;
                if (advance <= width + 0.001f)
                {
                    var atlas = owner.atlasTextures[glyph.atlasIndex];
                    float pad = owner.atlasPadding;
                    float top = line == 0 ? 0f : lineBottoms[line - 1] + emLineSpacing;
                    float visualTop = top + (owner.ascentLine - glyph.bearingY) * scale;
                    float visualBottom = visualTop + glyph.height * scale;
                    quads.Add(new GlyphQuad
                    {
                        line = line,
                        atlas = atlas,
                        color = last >= 0 ? quads[last].color : Color.white,
                        hasColor = last >= 0 && quads[last].hasColor,
                        hasExplicitAlpha = last >= 0 && quads[last].hasExplicitAlpha,
                        rect = new Rect(x + (glyph.bearingX - pad) * scale, visualTop - pad * scale,
                            (glyph.width + 2 * pad) * scale, (glyph.height + 2 * pad) * scale),
                        uv = new Rect((glyph.rect.x - pad) / atlas.width, (glyph.rect.y - pad) / atlas.height,
                            (glyph.rect.width + 2 * pad) / atlas.width, (glyph.rect.height + 2 * pad) / atlas.height),
                        advanceEnd = x + advance,
                        visualTop = visualTop,
                        visualBottom = visualBottom
                    });
                    lineWidths[line] = x + advance;
                    ellipsisPlaced = true;
                    lines = line + 1;
                    quads.RemoveAll(q => q.line >= lines);
                }
                else
                {
                    lineWidths[line] = 0f;
                }
            }
            else
            {
                int last = quads.FindLastIndex(q => q.line == currentLine);
                lineWidths[line] = last >= 0 ? quads[last].advanceEnd : 0f;
            }
        }

        float maximum = 0f;
        for (int line = 0; line < lines; line++) maximum = Math.Max(maximum, lineWidths[line]);
        measured = new Vector2(maximum, lineBottoms[lines - 1]);

        _hasVisualBounds = false;
        _visualMinY = float.PositiveInfinity;
        _visualMaxY = float.NegativeInfinity;
        for (int index = 0; index < quads.Count; index++)
        {
            _visualMinY = Math.Min(_visualMinY, quads[index].visualTop);
            _visualMaxY = Math.Max(_visualMaxY, quads[index].visualBottom);
            _hasVisualBounds = true;
        }
    }
}
