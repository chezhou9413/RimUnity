using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimUnity.UI;

//富文本先变成带样式的字形序列，对齐 TMP 3.0.6 容错解析与缺字回退规则。
public sealed partial class TextMeshProUGUI
{
    private struct Letter
    {
        internal uint code;
        internal TMP_Glyph glyph;
        internal TMP_FontAsset owner;
        internal float size;
        internal Color color;
        internal bool hasColor;
        internal bool hasExplicitAlpha;
        internal bool italic;
        internal bool Breakable => code == '\n' || code == 0x200B || code == ' ' || code == '-' || code >= 0x2E80 && code <= 0xA4CF || code >= 0xAC00 && code <= 0xD7AF || code >= 0xF900 && code <= 0xFAFF;
    }

    private readonly List<Letter> letters = new();
    private readonly List<Letter> _measureLetters = new();

    private List<Letter> _ParseLetters(string source, float defaultSize, List<Letter> output)
    {
        output.Clear();
        if (string.IsNullOrEmpty(source) || font == null)
        {
            return output;
        }

        float size = defaultSize;
        Color color = Color.white;
        bool hasColor = false;
        bool hasExplicitAlpha = false;
        int italicDepth = 0;
        bool noParse = false;

        Stack<(Color color, bool hasColor, bool hasExplicitAlpha)> colorStack = new();
        Stack<float> sizeStack = new();

        for (int index = 0; index < source.Length; index++)
        {
            if (richText && source[index] == '<')
            {
                int tagEnd = _FindTagEnd(source, index + 1);
                if (tagEnd >= 0)
                {
                    string tagText = source.Substring(index + 1, tagEnd - index - 1);
                    string lowerTag = tagText.ToLowerInvariant();

                    if (noParse)
                    {
                        if (lowerTag == "/noparse")
                        {
                            noParse = false;
                            index = tagEnd;
                            continue;
                        }
                    }
                    else if (_TryConsumeTag(tagText, lowerTag, defaultSize, ref size, ref color, ref hasColor, ref hasExplicitAlpha,
                        ref italicDepth, ref noParse, colorStack, sizeStack, output))
                    {
                        index = tagEnd;
                        continue;
                    }
                }
            }

            uint codePoint = source[index];
            if (char.IsHighSurrogate(source[index]) && index + 1 < source.Length && char.IsLowSurrogate(source[index + 1]))
            {
                codePoint = (uint)char.ConvertToUtf32(source[index], source[index + 1]);
                index++;
            }
            if (codePoint == '\r')
            {
                continue;
            }
            //零宽空格允许断行，但没有可绘制字形或字距。
            if (codePoint == '\n' || codePoint == 0x200B)
            {
                output.Add(new Letter { code = codePoint });
                continue;
            }
            if (!_TryResolveGlyph(codePoint, out TMP_Glyph glyph, out TMP_FontAsset owner))
            {
                continue;
            }
            output.Add(new Letter
            {
                code = codePoint,
                glyph = glyph,
                owner = owner,
                size = size,
                color = color,
                hasColor = hasColor,
                hasExplicitAlpha = hasExplicitAlpha,
                italic = italicDepth > 0
            });
        }
        return output;
    }

    //标签内遇到新的 '<' 即停止，保证 "a < b && c > d" 不会被误当成富文本标签。
    private static int _FindTagEnd(string source, int startIndex)
    {
        for (int index = startIndex; index < source.Length; index++)
        {
            char character = source[index];
            if (character == '>')
            {
                return index;
            }
            if (character == '<')
            {
                return -1;
            }
        }
        return -1;
    }

    private static bool _TryConsumeTag(
        string tagText,
        string lowerTag,
        float defaultSize,
        ref float size,
        ref Color color,
        ref bool hasColor,
        ref bool hasExplicitAlpha,
        ref int italicDepth,
        ref bool noParse,
        Stack<(Color color, bool hasColor, bool hasExplicitAlpha)> colorStack,
        Stack<float> sizeStack,
        List<Letter> output)
    {
        if (lowerTag == "noparse") { noParse = true; return true; }
        if (lowerTag == "/noparse") { noParse = false; return true; }
        if (lowerTag == "br" || lowerTag == "br/" || lowerTag.Replace(" ", "") == "br/") { output.Add(new Letter { code = '\n' }); return true; }
        if (lowerTag == "b" || lowerTag == "/b" || lowerTag == "u" || lowerTag == "/u" || lowerTag == "s" || lowerTag == "/s") return true;
        if (lowerTag == "i") { italicDepth++; return true; }
        if (lowerTag == "/i") { if (italicDepth > 0) italicDepth--; return true; }

        if (lowerTag.StartsWith("color=", StringComparison.Ordinal) || lowerTag.StartsWith("#", StringComparison.Ordinal))
        {
            string colorArgument = (lowerTag.StartsWith("#", StringComparison.Ordinal) ? tagText : tagText[6..]).Trim('"', '\'');
            if (!_TryParseColor(colorArgument, out Color parsedColor, out bool explicitAlpha))
            {
                return false;
            }
            colorStack.Push((color, hasColor, hasExplicitAlpha));
            color = parsedColor;
            hasColor = true;
            hasExplicitAlpha = explicitAlpha;
            return true;
        }
        if (lowerTag == "/color")
        {
            if (colorStack.Count > 0)
            {
                (color, hasColor, hasExplicitAlpha) = colorStack.Pop();
            }
            return true;
        }
        if (lowerTag.StartsWith("size=", StringComparison.Ordinal))
        {
            string sizeArgument = tagText[5..].Trim('"', '\'');
            if (!_TryParseSize(sizeArgument, defaultSize, out float parsedSize))
            {
                return false;
            }
            sizeStack.Push(size);
            size = parsedSize;
            return true;
        }
        if (lowerTag == "/size")
        {
            if (sizeStack.Count > 0)
            {
                size = sizeStack.Pop();
            }
            return true;
        }
        //未识别的标签按 TMP 3.0.6 行为作为普通文本原样渲染，不抛异常中断整个界面绘制。
        return false;
    }

    private static bool _TryParseSize(string value, float defaultSize, out float parsedSize)
    {
        parsedSize = defaultSize;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        if (value.EndsWith("%", StringComparison.Ordinal))
        {
            if (!float.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percentage) || !float.IsFinite(percentage))
            {
                return false;
            }
            float computed = defaultSize * percentage * 0.01f;
            if (!float.IsFinite(computed))
            {
                return false;
            }
            parsedSize = Math.Clamp(computed, 0.1f, 5000f);
            return true;
        }
        if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        {
            if (!float.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out float emUnits) || !float.IsFinite(emUnits))
            {
                return false;
            }
            float computed = defaultSize * emUnits;
            if (!float.IsFinite(computed))
            {
                return false;
            }
            parsedSize = Math.Clamp(computed, 0.1f, 5000f);
            return true;
        }
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2];
        }
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pixelSize) || !float.IsFinite(pixelSize))
        {
            return false;
        }
        parsedSize = Math.Clamp(pixelSize, 0.1f, 5000f);
        return true;
    }

    private bool _TryResolveGlyph(uint codePoint, out TMP_Glyph glyph, out TMP_FontAsset owner)
    {
        if (font.TryGlyph(codePoint, out glyph, out owner)) return true;
        if (font.TryGlyph(0x25A1, out glyph, out owner)) return true;
        if (font.TryGlyph('?', out glyph, out owner)) return true;
        if (font.TryGlyph(' ', out glyph, out owner)) return true;
        glyph = null;
        owner = null;
        return false;
    }

    //前后配对记录分别作用到当前字形；不同 fallback 字体之间不混用字形索引。
    private Vector4 Adjustment(List<Letter> sequence, int index)
    {
        if (!enableKerning) return Vector4.zero;
        var current = sequence[index]; Vector4 adjustment = Vector4.zero;
        if (index > 0 && sequence[index - 1].owner == current.owner && sequence[index - 1].glyph != null)
            adjustment += current.owner.Pair(sequence[index - 1].glyph.index, current.glyph.index)?.secondAdjustment ?? Vector4.zero;
        if (index + 1 < sequence.Count && sequence[index + 1].owner == current.owner && sequence[index + 1].glyph != null)
            adjustment += current.owner.Pair(current.glyph.index, sequence[index + 1].glyph.index)?.firstAdjustment ?? Vector4.zero;
        return adjustment;
    }
}
