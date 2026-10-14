using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//为循环加上独立作用域，保持初始化变量的词法生命周期。
internal static class ShaderLoopScopes
{
    internal static string Adapt(string source)
    {
        var starts = new HashSet<int>();
        var ends = new Dictionary<int, int>();
        foreach (Match match in Regex.Matches(source, @"(?:\[\s*(?:unroll|loop|fastopt|allow_uav_condition)\b[^\]\r\n]*\]\s*)*\bfor\s*\("))
        {
            int close = Matching(source, source.IndexOf('(', match.Index), '(', ')');
            if (close < 0) continue; //语法错误交给编译器报告。
            int body = close + 1;
            while (body < source.Length && char.IsWhiteSpace(source[body])) body++;
            if (body >= source.Length || source[body] != '{') continue;
            int end = Matching(source, body, '{', '}');
            if (end < 0) continue;
            starts.Add(match.Index);
            ends[end + 1] = ends.TryGetValue(end + 1, out int count) ? count + 1 : 1;
        }
        var result = new StringBuilder(source.Length + starts.Count * 4);
        for (int index = 0; index <= source.Length; index++)
        {
            if (ends.TryGetValue(index, out int count)) result.Append('}', count);
            if (starts.Contains(index)) result.Append('{');
            if (index < source.Length) result.Append(source[index]);
        }
        return result.ToString();
    }

    private static int Matching(string source, int start, char open, char close)
    {
        int depth = 0;
        for (int index = start; index < source.Length; index++)
        {
            //引号内的路径不参与括号匹配。
            if (source[index] == '"' || source[index] == '\'')
            {
                char quote = source[index];
                while (++index < source.Length)
                {
                    if (source[index] == '\\') index++;
                    else if (source[index] == quote) break;
                }
                continue;
            }
            if (source[index] == open) depth++;
            else if (source[index] == close && --depth == 0) return index;
        }
        return -1;
    }
}
