using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Brutal.ImGuiApi;

namespace RimUnity.UI.Runtime;

//按字形度量和富文本样式计算文字排版。
internal static partial class UguiTextLayout
{
    internal readonly record struct Run(int Start, int Length, Color Color, FontStyle Style, float Size, float X, float Width, bool IsSpace);
    internal readonly record struct Line(int PieceStart, int PieceCount, float Width, float Top, float Ascent, float FirstLine, float Pitch);
    internal sealed record Layout(string Source, int FontIndex, Vector2 Size, Line[] Lines, Run[] Runs);
    private readonly record struct Key(string Text, int Font, int Size, FontStyle Style, bool Rich, bool Wrap, float Width, float Spacing);
    private sealed class Cache { internal Key Key; internal Layout Value; }
    private static readonly ConditionalWeakTable<Text, Cache> Cached = new();
    private readonly record struct Letter(int Start, int Length, uint Code, Color Color, FontStyle Style, float Size, float Width, float Ascent, float Descent)
    {
        internal bool Space => Code is ' ' or '\t' or 0x3000;
        internal bool Breakable => Space || Code >= 0x2e80 || Code is '-' or '\n';
    }

    //缓存按内容、样式和可用宽度失效，几何变换不会触发重新分词。
    internal static Layout Build(Text text, UiTextStyle style, float width)
    {
        if (!float.IsFinite(text.lineSpacing) || text.lineSpacing < 0) throw new InvalidOperationException("Text 行距必须是有限非负数。");
        int font = text.font != null ? text.font.GameFontIndex : 1;
        var key = new Key(text.text ?? "", font, style.fontSize, style.fontStyle, text.supportRichText, style.wordWrap, width, text.lineSpacing);
        Cache cache = Cached.GetOrCreateValue(text);
        if (cache.Value != null && cache.Key == key) return cache.Value;
        List<Letter> letters = Parse(key);
        var lines = new List<Line>();
        var runs = new List<Run>();
        var normal = Font(font, style.fontStyle, Math.Max(1, style.fontSize));
        float defaultAscent = normal.Ascent, defaultDescent = -normal.Descent;
        float x = 0, top = 0, ascent = defaultAscent, descent = defaultDescent, maxWidth = 0;
        int start = 0;
        bool wordStart = true;
        bool wrap = style.wordWrap && width > 0 && float.IsFinite(width);
        void EndLine()
        {
            float height = ascent + descent;
            lines.Add(new Line(start, runs.Count - start, x, top, ascent, height, height));
            maxWidth = Math.Max(maxWidth, x); top += height;
            start = runs.Count; x = 0; ascent = defaultAscent; descent = defaultDescent;
        }
        for (int i = 0; i < letters.Count; i++)
        {
            Letter letter = letters[i];
            if (letter.Code == '\n') { EndLine(); wordStart = true; continue; }
            if (wrap && wordStart && !letter.Breakable && x > 0)
            {
                float word = 0;
                for (int j = i; j < letters.Count && !letters[j].Breakable; j++) word += letters[j].Width;
                if (x + word > width) EndLine();
            }
            if (wrap && x > 0 && x + letter.Width > width)
            { EndLine(); if (letter.Space) { wordStart = true; continue; } }
            var run = new Run(letter.Start, letter.Length, letter.Color, letter.Style, letter.Size, x, letter.Width, letter.Space);
            //连续同样式字形合并，避免每个字符都重新取得字号和图集。
            if (runs.Count > start && runs[^1] is var previous && previous.Start + previous.Length == run.Start
                && previous.Color == run.Color && previous.Style == run.Style && previous.Size == run.Size && previous.IsSpace == run.IsSpace)
                runs[^1] = previous with { Length = previous.Length + run.Length, Width = previous.Width + run.Width };
            else runs.Add(run);
            x += letter.Width; ascent = Math.Max(ascent, letter.Ascent); descent = Math.Max(descent, letter.Descent);
            wordStart = letter.Breakable;
        }
        EndLine();
        Line last = lines[^1];
        var result = new Layout(key.Text, font, new Vector2(maxWidth, last.Top * text.lineSpacing + last.FirstLine), lines.ToArray(), runs.ToArray());
        cache.Key = key; cache.Value = result;
        return result;
    }

    //先请求整段文字的字形，图集扩容完成后再读取用于绘制的 UV。
    internal static void Warm(Layout layout)
    {
        foreach (Run run in layout.Runs)
        {
            if (run.IsSpace) continue;
            var baked = Font(layout.FontIndex, run.Style, run.Size);
            for (int i = run.Start; i < run.Start + run.Length; i++)
            {
                uint code = layout.Source[i];
                if (char.IsHighSurrogate(layout.Source[i]) && i + 1 < run.Start + run.Length && char.IsLowSurrogate(layout.Source[i + 1]))
                    code = (uint)char.ConvertToUtf32(layout.Source[i++], layout.Source[i]);
                baked.FindGlyph(code);
            }
        }
    }

    //字形已经由游戏共用的原生 FreeType/ImGui 字体表提供，包括中文回退字体。
    internal static ImFontBakedPtr Font(int fontIndex, FontStyle style, float size) => NativeGuiHost.Font(fontIndex, style, size);
}
