using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimUnity.UI;

//布局缓存只在文字和尺寸参数变化时重建，变换与画布透明度在提交时应用。
public sealed partial class TextMeshProUGUI
{
    internal struct GlyphQuad
    {
        internal Rect rect, uv;
        internal Texture2D atlas;
        internal Color color;
        internal bool hasColor;
        internal bool hasExplicitAlpha;
        internal int line;
        internal float topShear, bottomShear;
        internal float advanceEnd;
        internal float visualTop, visualBottom;
    }

    internal readonly List<GlyphQuad> quads = new();
    private readonly List<float> lineWidths = new();
    private readonly List<float> lineBottoms = new();
    private readonly List<float> _lineVisibleBottoms = new();
    private Vector2 measured;
    private float _visualMinY;
    private float _visualMaxY;
    private float _firstLineAscent;
    private bool _hasVisualBounds;
    private string cached;
    private TMP_FontAsset cachedFont;
    private Vector4 cachedBounds;
    private int cachedOptions;

    internal void Build(float width, float height)
    {
        if (font == null || !(font.pointSize > 0f) || !float.IsFinite(font.lineHeight))
        {
            quads.Clear();
            lineWidths.Clear();
            lineBottoms.Clear();
            _lineVisibleBottoms.Clear();
            measured = Vector2.zero;
            _hasVisualBounds = false;
            return;
        }
        int options = HashCode.Combine(
            HashCode.Combine(alignment, enableWordWrapping, richText, enableAutoSizing, characterSpacing, lineSpacing, fontSizeMin, fontSizeMax),
            enableKerning, fontSpacing, italicSlant, overflowMode);
        Vector4 bounds = new(width, height, fontSize, 0);
        if (cached == text && cachedFont == font && cachedBounds == bounds && cachedOptions == options) return;
        cached = text; cachedFont = font; cachedBounds = bounds; cachedOptions = options;
        float size = enableAutoSizing ? fontSizeMax : fontSize;
        Layout(text, size, width);
        if (enableAutoSizing && (measured.x > width || measured.y > height))
        {
            float low = fontSizeMin, high = fontSizeMax;
            for (int i = 0; i < 10; i++)
            {
                float mid = (low + high) * 0.5f;
                Layout(text, mid, width);
                if (measured.x <= width && measured.y <= height) low = mid;
                else high = mid;
            }
            size = low;
            Layout(text, size, width);
        }
        ApplyOverflow(width, height, size);
    }

    //无副作用测量给定字符串的首选尺寸（供 TMP_InputField / Layout 调用，不改写 quads 与布局脏标记）。
    private Vector2 _MeasureValues(string source, float width, float height)
    {
        if (font == null || !(font.pointSize > 0f) || !float.IsFinite(font.lineHeight))
        {
            return Vector2.zero;
        }
        float size = enableAutoSizing ? fontSizeMax : fontSize;
        if (string.IsNullOrEmpty(source))
        {
            return new Vector2(0f, font.lineHeight * size / font.pointSize);
        }
        Vector2 result = _MeasureLayout(source, size, width);
        if (enableAutoSizing && (result.x > width || result.y > height))
        {
            float low = fontSizeMin;
            float high = fontSizeMax;
            for (int step = 0; step < 10; step++)
            {
                float mid = (low + high) * 0.5f;
                result = _MeasureLayout(source, mid, width);
                if (result.x <= width && result.y <= height)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }
            result = _MeasureLayout(source, low, width);
        }
        return result;
    }

    private Vector2 _MeasureLayout(string source, float defaultSize, float width)
    {
        float emLineSpacing = lineSpacing * defaultSize * 0.01f;
        float baseRowHeight = font.lineHeight * defaultSize / font.pointSize;
        List<Letter> sequence = _ParseLetters(source, defaultSize, _measureLetters);
        if (sequence.Count == 0)
        {
            return new Vector2(0f, baseRowHeight);
        }

        float cursorX = 0f;
        float visibleX = 0f;
        float cursorY = 0f;
        float rowHeight = baseRowHeight;
        float maxWidth = 0f;
        int lineIndex = 0;
        bool isWordStart = true;

        void AdvanceNewline()
        {
            maxWidth = Math.Max(maxWidth, visibleX);
            cursorX = 0f;
            visibleX = 0f;
            cursorY += rowHeight + emLineSpacing;
            rowHeight = baseRowHeight;
            lineIndex++;
        }

        for (int index = 0; index < sequence.Count; index++)
        {
            Letter letter = sequence[index];
            if (letter.code == '\n')
            {
                AdvanceNewline();
                isWordStart = true;
                continue;
            }
            if (letter.code == 0x200B)
            {
                isWordStart = true;
                continue;
            }
            TMP_Glyph glyph = letter.glyph;
            TMP_FontAsset owner = letter.owner;
            if (owner == null || !(owner.pointSize > 0f))
            {
                continue;
            }
            float scale = letter.size / owner.pointSize * owner.scale * glyph.scale;
            Vector4 adjustment = Adjustment(sequence, index);
            float advance = (glyph.advance + adjustment.z) * scale + (characterSpacing + fontSpacing) * letter.size * 0.01f;
            if (enableWordWrapping && isWordStart && !letter.Breakable && cursorX > 0f)
            {
                float wordWidth = 0f;
                for (int lookahead = index; lookahead < sequence.Count && !sequence[lookahead].Breakable; lookahead++)
                {
                    Letter item = sequence[lookahead];
                    if (item.owner == null || !(item.owner.pointSize > 0f))
                    {
                        continue;
                    }
                    wordWidth += (item.glyph.advance + Adjustment(sequence, lookahead).z) * item.size / item.owner.pointSize * item.owner.scale * item.glyph.scale
                        + (characterSpacing + fontSpacing) * item.size * 0.01f;
                }
                if (cursorX + wordWidth > width)
                {
                    AdvanceNewline();
                }
            }
            if (enableWordWrapping && cursorX > 0f && cursorX + advance > width)
            {
                AdvanceNewline();
                if (letter.code == ' ')
                {
                    isWordStart = true;
                    continue;
                }
            }
            rowHeight = Math.Max(rowHeight, owner.lineHeight * scale);
            cursorX += advance;
            if (letter.code != ' ')
            {
                visibleX = cursorX;
            }
            isWordStart = letter.Breakable;
        }

        bool trailingEmptyLine = lineIndex > 0 && cursorX == 0f && sequence[^1].code == '\n';
        float finalHeight = trailingEmptyLine ? Math.Max(0f, cursorY - emLineSpacing) : cursorY + rowHeight;
        return new Vector2(Math.Max(maxWidth, visibleX), finalHeight);
    }

    private void Layout(string source, float defaultSize, float width)
    {
        quads.Clear(); lineWidths.Clear(); lineBottoms.Clear(); _lineVisibleBottoms.Clear();
        _hasVisualBounds = false;
        _visualMinY = float.PositiveInfinity;
        _visualMaxY = float.NegativeInfinity;
        _firstLineAscent = font.ascentLine * defaultSize / font.pointSize;

        //TMP 的 characterSpacing 与 lineSpacing 均采用 1/100 em 单位（fontSize * 0.01f）。
        float emLineSpacing = lineSpacing * defaultSize * 0.01f;
        float baseRowHeight = font.lineHeight * defaultSize / font.pointSize;
        float baseVisibleHeight = (font.ascentLine - font.descentLine) * defaultSize / font.pointSize;

        if (string.IsNullOrEmpty(source))
        {
            lineWidths.Add(0f);
            lineBottoms.Add(baseRowHeight);
            _lineVisibleBottoms.Add(baseVisibleHeight);
            measured = new Vector2(0f, baseRowHeight);
            return;
        }

        List<Letter> sequence = _ParseLetters(source, defaultSize, letters);
        if (sequence.Count == 0)
        {
            lineWidths.Add(0f);
            lineBottoms.Add(baseRowHeight);
            _lineVisibleBottoms.Add(baseVisibleHeight);
            measured = new Vector2(0f, baseRowHeight);
            return;
        }

        float x = 0f, visibleX = 0f, y = 0f, rowHeight = baseRowHeight, visibleHeight = baseVisibleHeight, maxWidth = 0f;
        int line = 0;
        bool wordStart = true;

        void Newline()
        {
            lineWidths.Add(visibleX);
            lineBottoms.Add(y + rowHeight);
            _lineVisibleBottoms.Add(y + visibleHeight);
            maxWidth = Math.Max(maxWidth, visibleX);
            x = 0f; visibleX = 0f;
            y += rowHeight + emLineSpacing;
            rowHeight = baseRowHeight;
            visibleHeight = baseVisibleHeight;
            line++;
        }

        for (int i = 0; i < sequence.Count; i++)
        {
            var letter = sequence[i];
            if (letter.code == '\n') { Newline(); wordStart = true; continue; }
            if (letter.code == 0x200B) { wordStart = true; continue; }
            var glyph = letter.glyph; var owner = letter.owner;
            if (owner == null || !(owner.pointSize > 0f)) continue;
            float scale = letter.size / owner.pointSize * owner.scale * glyph.scale;
            Vector4 adjustment = Adjustment(sequence, i);
            float advance = (glyph.advance + adjustment.z) * scale + (characterSpacing + fontSpacing) * letter.size * 0.01f;
            //拉丁单词先整体换行；超出整行宽度的长词仍允许按字符断开。
            if (enableWordWrapping && wordStart && !letter.Breakable && x > 0f)
            {
                float wordWidth = 0f;
                for (int j = i; j < sequence.Count && !sequence[j].Breakable; j++)
                {
                    var item = sequence[j];
                    if (item.owner == null || !(item.owner.pointSize > 0f)) continue;
                    wordWidth += (item.glyph.advance + Adjustment(sequence, j).z) * item.size / item.owner.pointSize * item.owner.scale * item.glyph.scale
                        + (characterSpacing + fontSpacing) * item.size * 0.01f;
                }
                if (x + wordWidth > width) Newline();
            }
            if (enableWordWrapping && x > 0f && x + advance > width)
            {
                Newline();
                if (letter.code == ' ') { wordStart = true; continue; }
            }
            rowHeight = Math.Max(rowHeight, owner.lineHeight * scale);
            visibleHeight = Math.Max(visibleHeight, (owner.ascentLine - owner.descentLine) * scale);
            if (line == 0) _firstLineAscent = Math.Max(_firstLineAscent, owner.ascentLine * scale);

            if (glyph.width > 0 && glyph.height > 0)
            {
                Texture2D atlas = owner.atlasTextures[glyph.atlasIndex]; float pad = owner.atlasPadding;
                float visualTop = y + (owner.ascentLine - glyph.bearingY - adjustment.y) * scale;
                float visualBottom = visualTop + glyph.height * scale;
                _visualMinY = Math.Min(_visualMinY, visualTop);
                _visualMaxY = Math.Max(_visualMaxY, visualBottom);
                _hasVisualBounds = true;

                Rect r = new(
                    x + (glyph.bearingX + adjustment.x - pad) * scale,
                    visualTop - pad * scale,
                    (glyph.width + 2 * pad) * scale,
                    (glyph.height + 2 * pad) * scale);
                Rect uv = new(
                    (glyph.rect.x - pad) / atlas.width,
                    (glyph.rect.y - pad) / atlas.height,
                    (glyph.rect.width + 2 * pad) / atlas.width,
                    (glyph.rect.height + 2 * pad) / atlas.height);
                float slant = letter.italic && italicSlant == 0f ? 0.21f : italicSlant;
                float topShear = slant * (glyph.bearingY + pad) * scale;
                float bottomShear = slant * (glyph.bearingY - glyph.height - pad) * scale;
                float adjustmentShear = (topShear - bottomShear) * 0.5f;
                quads.Add(new GlyphQuad
                {
                    rect = r,
                    uv = uv,
                    atlas = atlas,
                    color = letter.color,
                    hasColor = letter.hasColor,
                    hasExplicitAlpha = letter.hasExplicitAlpha,
                    line = line,
                    topShear = topShear - adjustmentShear,
                    bottomShear = bottomShear - adjustmentShear,
                    advanceEnd = x + advance,
                    visualTop = visualTop,
                    visualBottom = visualBottom
                });
            }
            x += advance;
            if (letter.code != ' ') visibleX = x;
            wordStart = letter.Breakable;
        }
        //与原生 TMP 对齐：文本以 '\n' 结尾时不把尾随空行计入视觉总高度，避免居中/底部对齐向上偏移。
        bool trailingEmptyLine = line > 0 && x == 0f && sequence[^1].code == '\n';
        if (!trailingEmptyLine)
        {
            lineWidths.Add(visibleX);
            lineBottoms.Add(y + rowHeight);
            _lineVisibleBottoms.Add(y + visibleHeight);
        }
        measured = new Vector2(Math.Max(maxWidth, visibleX), lineBottoms.Count > 0 ? lineBottoms[^1] : 0f);
    }

    //对齐 TMP 3.0.6 全部水平与垂直对齐标志（含 VerticalAlignmentOptions.Geometry = 4096、Baseline = 2048、Capline = 8192）。
    internal Vector2 Offset(int line, Rect content)
    {
        int horizontalAlign = alignment & 0xFF;
        int verticalAlign = alignment & 0xFF00;
        float horizontalFactor = horizontalAlign is 2 or 32 ? 0.5f : horizontalAlign == 4 ? 1f : 0f;
        float offsetX = content.xMin + horizontalFactor * (content.width - lineWidths[line]);
        float offsetY = verticalAlign switch
        {
            512 => content.yMax - 0.5f * (content.height - measured.y),
            1024 => content.yMax - (content.height - measured.y),
            2048 => content.center.y + _firstLineAscent,
            4096 => _hasVisualBounds
                ? content.center.y + 0.5f * (_visualMinY + _visualMaxY)
                : content.yMax - 0.5f * (content.height - measured.y),
            8192 => content.yMax - 0.5f * (content.height - measured.y),
            _ => content.yMax
        };
        return new Vector2(offsetX, offsetY);
    }

    private static bool _TryParseColor(string colorText, out Color color, out bool explicitAlpha)
    {
        color = Color.white;
        explicitAlpha = false;
        if (string.IsNullOrEmpty(colorText))
        {
            return false;
        }

        if (colorText.StartsWith("#", StringComparison.Ordinal))
        {
            string hex = colorText[1..];
            if (hex.Length == 3 || hex.Length == 4)
            {
                explicitAlpha = hex.Length == 4;
                Span<char> expanded = stackalloc char[8];
                expanded[0] = expanded[1] = hex[0];
                expanded[2] = expanded[3] = hex[1];
                expanded[4] = expanded[5] = hex[2];
                expanded[6] = expanded[7] = explicitAlpha ? hex[3] : 'F';
                hex = new string(expanded);
            }
            else if (hex.Length == 6)
            {
                hex += "FF";
            }
            else if (hex.Length == 8)
            {
                explicitAlpha = true;
            }
            else
            {
                return false;
            }

            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgbaValue))
            {
                return false;
            }
            color = (Color)new Color32((byte)(rgbaValue >> 24), (byte)(rgbaValue >> 16), (byte)(rgbaValue >> 8), (byte)rgbaValue);
            return true;
        }

        switch (colorText.ToLowerInvariant())
        {
            case "red": color = Color.red; return true;
            case "green": color = Color.green; return true;
            case "blue": color = Color.blue; return true;
            case "white": color = Color.white; return true;
            case "black": color = Color.black; return true;
            case "yellow": color = Color.yellow; return true;
            case "cyan": color = new Color(0f, 1f, 1f, 1f); return true;
            case "magenta": color = new Color(1f, 0f, 1f, 1f); return true;
            case "clear": color = Color.clear; explicitAlpha = true; return true;
            case "orange": color = (Color)new Color32(255, 128, 0, 255); return true;
            case "purple": color = (Color)new Color32(160, 32, 240, 255); return true;
            case "lightblue": color = (Color)new Color32(173, 216, 230, 255); return true;
            case "grey":
            case "gray": color = (Color)new Color32(128, 128, 128, 255); return true;
            default: return false;
        }
    }
}
