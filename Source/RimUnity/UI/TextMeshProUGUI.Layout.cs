using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimUnity.UI;

//布局缓存只在文字和尺寸参数变化时重建，变换与画布透明度在提交时应用。
public sealed partial class TextMeshProUGUI
{
    internal struct GlyphQuad { internal Rect rect, uv; internal Texture2D atlas; internal Color color; internal int line; }
    internal readonly List<GlyphQuad> quads = new();
    private readonly List<float> lineWidths = new();
    private Vector2 measured;
    private string cached;
    private TMP_FontAsset cachedFont;
    private Vector4 cachedBounds;
    private int cachedOptions;
    internal void Build(float width, float height)
    {
        if (font == null) throw new InvalidOperationException("TMP 字体未指定：" + name);
        int options = HashCode.Combine(HashCode.Combine(alignment, enableWordWrapping, richText, enableAutoSizing, characterSpacing, lineSpacing, fontSizeMin, fontSizeMax), enableKerning, fontSpacing);
        Vector4 bounds = new(width, height, fontSize, 0);
        if (cached == text && cachedFont == font && cachedBounds == bounds && cachedOptions == options) return;
        cached = text; cachedFont = font; cachedBounds = bounds; cachedOptions = options;
        float size = enableAutoSizing ? fontSizeMax : fontSize;
        Layout(size, width);
        if (enableAutoSizing && (measured.x > width || measured.y > height))
        {
            float low = fontSizeMin, high = fontSizeMax;
            for (int i = 0; i < 10; i++) { float mid = (low + high) * .5f; Layout(mid, width); if (measured.x <= width && measured.y <= height) low = mid; else high = mid; }
            Layout(low, width);
        }
    }
    private void Layout(float defaultSize, float width)
    {
        quads.Clear(); lineWidths.Clear();
        var letters = ParseLetters(defaultSize);
        float x = 0, y = 0, rowHeight = font.lineHeight * defaultSize / font.pointSize, maxWidth = 0;
        int line = 0;
        bool wordStart = true;
        void Newline() { lineWidths.Add(x); maxWidth = Math.Max(maxWidth, x); x = 0; y += rowHeight + lineSpacing; rowHeight = font.lineHeight * defaultSize / font.pointSize; line++; }
        for (int i = 0; i < letters.Count; i++)
        {
            var letter = letters[i];
            if (letter.code == '\n') { Newline(); wordStart = true; continue; }
            var glyph = letter.glyph; var owner = letter.owner;
            float scale = letter.size / owner.pointSize * owner.scale * glyph.scale;
            Vector4 adjustment = Adjustment(letters, i);
            float advance = (glyph.advance + adjustment.z) * scale + characterSpacing + fontSpacing * letter.size * .01f;
            //拉丁单词先整体换行；超出整行宽度的长词仍允许按字符断开。
            if (enableWordWrapping && wordStart && !letter.Breakable && x > 0)
            {
                float wordWidth = 0;
                for (int j = i; j < letters.Count && !letters[j].Breakable; j++)
                {
                    var item = letters[j];
                    wordWidth += (item.glyph.advance + Adjustment(letters, j).z) * item.size / item.owner.pointSize * item.owner.scale * item.glyph.scale + characterSpacing + fontSpacing * item.size * .01f;
                }
                if (x + wordWidth > width) Newline();
            }
            if (enableWordWrapping && x > 0 && x + advance > width)
            { Newline(); if (letter.code == ' ') { wordStart = true; continue; } }
            rowHeight = Math.Max(rowHeight, owner.lineHeight * scale);
            if (glyph.width > 0 && glyph.height > 0)
            {
                Texture2D atlas = owner.atlasTextures[glyph.atlasIndex]; float pad = owner.atlasPadding;
                Rect r = new(x + (glyph.bearingX + adjustment.x - pad) * scale, y + (owner.ascentLine - glyph.bearingY - adjustment.y - pad) * scale, (glyph.width + 2 * pad) * scale, (glyph.height + 2 * pad) * scale);
                Rect uv = new((glyph.rect.x - pad) / atlas.width, (glyph.rect.y - pad) / atlas.height, (glyph.rect.width + 2 * pad) / atlas.width, (glyph.rect.height + 2 * pad) / atlas.height);
                quads.Add(new GlyphQuad { rect = r, uv = uv, atlas = atlas, color = letter.color, line = line });
            }
            x += advance;
            wordStart = letter.Breakable;
        }
        lineWidths.Add(x); measured = new Vector2(Math.Max(maxWidth, x), y + rowHeight);
    }
    internal Vector2 Offset(int line, Rect content)
    {
        int h = alignment & 255, v = alignment & 0xFF00;
        return new Vector2(content.xMin + (h == 2 ? .5f : h == 4 ? 1 : 0) * (content.width - lineWidths[line]),
            content.yMax - (v == 512 ? .5f : v == 1024 ? 1 : 0) * (content.height - measured.y));
    }
    private static Color ParseColor(string text)
    {
        if (text.StartsWith("#"))
        {
            string hex = text[1..]; if (hex.Length == 6) hex += "FF";
            if (hex.Length != 8) throw new FormatException("TMP 颜色需要 #RRGGBB 或 #RRGGBBAA。");
            uint value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (Color)new Color32((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
        return text.ToLowerInvariant() switch { "red" => Color.red, "green" => Color.green, "blue" => Color.blue, "white" => Color.white, "black" => Color.black, "yellow" => Color.yellow, _ => throw new FormatException("TMP 未知颜色：" + text) };
    }
}
