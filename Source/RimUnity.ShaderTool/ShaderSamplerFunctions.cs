using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//旧 Cg 的采样器函数参数按实际纹理生成同义函数，保留循环和采样表达式。
internal static class ShaderSamplerFunctions
{
    internal static string Adapt(string source)
    {
        var signature = new Regex(@"\b(?:inline\s+)?((?:float|half|fixed)[1-4]?)\s+(\w+)\s*\(\s*sampler2D\s+(\w+)\s*,([^)]*)\)\s*\{");
        for (Match match = signature.Match(source); match.Success; match = signature.Match(source))
        {
            int end = match.Index + match.Length, depth = 1;
            while (end < source.Length && depth > 0) { char c = source[end++]; if (c == '{') depth++; else if (c == '}') depth--; }
            if (depth != 0) throw new InvalidOperationException("采样器函数括号未闭合：" + match.Groups[2].Value);
            string body = source.Substring(match.Index + match.Length, end - match.Index - match.Length - 1);
            string name = match.Groups[2].Value;
            source = source.Remove(match.Index, end - match.Index);
            var calls = Regex.Matches(source, @"\b" + Regex.Escape(name) + @"\s*\(\s*(\w+)\s*,");
            var clones = new StringBuilder();
            foreach (string texture in calls.Select(m => m.Groups[1].Value).Distinct())
            {
                if (!Regex.IsMatch(source, @"\bsampler2D\s+" + Regex.Escape(texture) + @"\s*;"))
                    throw new NotSupportedException("采样器函数需要静态纹理实参：" + name + "(" + texture + ")");
                string cloneName = "RU_" + name + texture;
                clones.AppendLine(match.Groups[1].Value + " " + cloneName + "(" + match.Groups[4].Value + ") {"
                    + Regex.Replace(body, @"\b" + Regex.Escape(match.Groups[3].Value) + @"\b", texture) + "}");
                source = Regex.Replace(source, @"\b" + Regex.Escape(name) + @"\s*\(\s*" + Regex.Escape(texture) + @"\s*,", cloneName + "(");
            }
            if (Regex.IsMatch(source, @"\b" + Regex.Escape(name) + @"\s*\(")) throw new NotSupportedException("采样器函数仍有未适配调用：" + name);
            source = source.Insert(match.Index, clones.ToString());
        }
        return source;
    }
}
