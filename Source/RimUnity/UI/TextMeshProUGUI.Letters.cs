using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimUnity.UI;

//富文本先变成带样式的字形序列，换行计算可以跨标签查看同一个单词。
public sealed partial class TextMeshProUGUI
{
    private struct Letter
    {
        internal uint code;
        internal TMP_Glyph glyph;
        internal TMP_FontAsset owner;
        internal float size;
        internal Color color;
        internal bool Breakable => code == '\n' || code == ' ' || code == '-' || code >= 0x2E80 && code <= 0xA4CF || code >= 0xAC00 && code <= 0xD7AF || code >= 0xF900 && code <= 0xFAFF;
    }
    private readonly List<Letter> letters = new();
    //基础标签只影响后续字形，未知标签直接指出不受支持的语义。
    private List<Letter> ParseLetters(float defaultSize)
    {
        letters.Clear();
        float size = defaultSize; Color color = Color.white;
        var colors = new Stack<Color>(); var sizes = new Stack<float>();
        string source = text ?? "";
        for (int i = 0; i < source.Length; i++)
        {
            if (richText && source[i] == '<')
            {
                int end = source.IndexOf('>', i + 1);
                if (end >= 0)
                {
                    string tag = source.Substring(i + 1, end - i - 1), lower = tag.ToLowerInvariant();
                    if (lower == "br" || lower == "br/") letters.Add(new Letter { code = '\n' });
                    else if (lower.StartsWith("color=") || lower.StartsWith("#"))
                    { colors.Push(color); color = ParseColor((lower.StartsWith("#") ? tag : tag[6..]).Trim('"', '\'')); }
                    else if (lower == "/color") { if (colors.Count == 0) throw new FormatException("TMP color 标签不匹配。"); color = colors.Pop(); }
                    else if (lower.StartsWith("size="))
                    { sizes.Push(size); string value = tag[5..].Trim('"', '\''); size = value.EndsWith("%") ? defaultSize * float.Parse(value[..^1], CultureInfo.InvariantCulture) / 100 : float.Parse(value, CultureInfo.InvariantCulture); }
                    else if (lower == "/size") { if (sizes.Count == 0) throw new FormatException("TMP size 标签不匹配。"); size = sizes.Pop(); }
                    else throw new NotSupportedException("TMP 富文本标签尚未支持：" + tag);
                    i = end; continue;
                }
            }
            uint code = source[i];
            if (char.IsHighSurrogate(source[i]) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1])) { code = (uint)char.ConvertToUtf32(source[i], source[i + 1]); i++; }
            if (code == '\r') continue;
            if (code == '\n') { letters.Add(new Letter { code = code }); continue; }
            if (!font.TryGlyph(code, out var glyph, out var owner)) throw new InvalidOperationException($"TMP 图集缺字 U+{code:X}，请在 Unity 字体资产中加入该字形后重新导出。");
            letters.Add(new Letter { code = code, glyph = glyph, owner = owner, size = size, color = color });
        }
        return letters;
    }
    //前后配对记录分别作用到当前字形；不同 fallback 字体之间不混用字形索引。
    private Vector4 Adjustment(List<Letter> sequence, int index)
    {
        if (!enableKerning) return Vector4.zero;
        var current = sequence[index]; Vector4 adjustment = Vector4.zero;
        if (index > 0 && sequence[index - 1].owner == current.owner)
            adjustment += current.owner.Pair(sequence[index - 1].glyph.index, current.glyph.index)?.secondAdjustment ?? Vector4.zero;
        if (index + 1 < sequence.Count && sequence[index + 1].owner == current.owner)
            adjustment += current.owner.Pair(current.glyph.index, sequence[index + 1].glyph.index)?.firstAdjustment ?? Vector4.zero;
        return adjustment;
    }
}
