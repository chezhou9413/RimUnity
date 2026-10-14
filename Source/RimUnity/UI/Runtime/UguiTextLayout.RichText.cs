using System;
using System.Collections.Generic;
using System.Globalization;
using Brutal.ImGuiApi;

namespace RimUnity.UI.Runtime;

//传统 Text 的基本富文本标签与普通字符共用一套字形度量。
internal static partial class UguiTextLayout
{
    private static List<Letter> Parse(Key key)
    {
        var result = new List<Letter>();
        var colors = new Stack<Color>();
        var sizes = new Stack<float>();
        Color color = Color.white;
        float size = Math.Max(1, key.Size);
        int bold = 0, italic = 0;
        for (int i = 0; i < key.Text.Length;)
        {
            if (key.Rich && key.Text[i] == '<')
            {
                int end = key.Text.IndexOf('>', i + 1);
                if (end >= 0 && Tag(key.Text.AsSpan(i + 1, end - i - 1), colors, sizes, ref color, ref size, ref bold, ref italic))
                { i = end + 1; continue; }
            }
            int length = char.IsHighSurrogate(key.Text[i]) && i + 1 < key.Text.Length && char.IsLowSurrogate(key.Text[i + 1]) ? 2 : 1;
            uint code = length == 2 ? (uint)char.ConvertToUtf32(key.Text[i], key.Text[i + 1]) : key.Text[i];
            if (code == '\r') { i += length; continue; }
            FontStyle style = (FontStyle)((int)key.Style | (bold > 0 ? 1 : 0) | (italic > 0 ? 2 : 0));
            var baked = Font(key.Font, style, size);
            float advance = 0;
            if (code != '\n')
            {
                var glyph = baked.FindGlyph(code == '\t' ? (uint)' ' : code);
                if (!glyph.IsNull()) advance = glyph.AdvanceX * (code == '\t' ? 4 : 1);
            }
            result.Add(new Letter(i, length, code, color, style, size, advance, baked.Ascent, -baked.Descent));
            i += length;
        }
        return result;
    }

    //未识别或不成对的标签按普通字符显示，不吞掉作者的文字内容。
    private static bool Tag(ReadOnlySpan<char> tag, Stack<Color> colors, Stack<float> sizes, ref Color color, ref float size, ref int bold, ref int italic)
    {
        if (tag.SequenceEqual("b")) { bold++; return true; }
        if (tag.SequenceEqual("i")) { italic++; return true; }
        if (tag.SequenceEqual("/b") && bold > 0) { bold--; return true; }
        if (tag.SequenceEqual("/i") && italic > 0) { italic--; return true; }
        if (tag.SequenceEqual("/color") && colors.Count > 0) { color = colors.Pop(); return true; }
        if (tag.SequenceEqual("/size") && sizes.Count > 0) { size = sizes.Pop(); return true; }
        if (tag.StartsWith("color=", StringComparison.Ordinal) && TryColor(tag[6..].Trim("\"'".AsSpan()), out Color next))
        { colors.Push(color); color = next; return true; }
        if (tag.StartsWith("size=", StringComparison.Ordinal)
            && float.TryParse(tag[5..].Trim("\"'".AsSpan()), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && number > 0 && float.IsFinite(number))
        { sizes.Push(size); size = number; return true; }
        return false;
    }

    private static bool TryColor(ReadOnlySpan<char> value, out Color color)
    {
        color = Color.white;
        if (value.Length > 0 && value[0] == '#')
        {
            var hex = value[1..];
            if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba)) return false;
            if (hex.Length == 6) rgba = (rgba << 8) | 255;
            color = new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba); return true;
        }
        switch (value.ToString().ToLowerInvariant())
        {
            case "white": return true;
            case "black": color = Color.black; return true;
            case "red": color = Color.red; return true;
            case "green": color = Color.green; return true;
            case "blue": color = Color.blue; return true;
            case "yellow": color = Color.yellow; return true;
            case "cyan": color = new(0, 1, 1); return true;
            case "magenta": color = new(1, 0, 1); return true;
            case "grey": case "gray": color = Color.gray; return true;
            case "orange": color = new(1, .647f, 0); return true;
            case "clear": color = Color.clear; return true;
            default: return false;
        }
    }
}
