using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//在 Slang 已完成类型转换后展开跨后端不稳定的数学内建函数。
internal static class ShaderMathCompat
{
    private static readonly Regex Calls = new("//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|\"(?:\\\\.|[^\"\\\\])*\"|\\b(?<function>pow|smoothstep)\\s*\\(");

    //只改写编译器生成的内建调用，保留用户函数名、注释、布局及参数的单次求值。
    internal static string Rewrite(string source, ShaderLabManifest manifest)
    {
        var powers = new SortedSet<int>();
        bool smooth = false;
        string rewritten = RewriteCalls(source, powers, ref smooth);
        if (powers.Count == 0 && !smooth) return source;
        var helpers = new StringBuilder();
        foreach (string type in new[] { "float", "vec2", "vec3", "vec4" })
        {
            foreach (int exponent in powers)
            {
                helpers.AppendLine($"{type} RU_CompatPow{exponent}({type} x) {{");
                helpers.AppendLine($"    {type} result = {type}(1.0);");
                int remaining = exponent;
                while (remaining > 0)
                {
                    if ((remaining & 1) != 0) helpers.AppendLine("    result *= x;");
                    remaining >>= 1;
                    if (remaining > 0) helpers.AppendLine("    x *= x;");
                }
                helpers.AppendLine("    return result;\n}");
            }
            if (!smooth) continue;
            helpers.AppendLine($"{type} RU_CompatSmoothstep({type} low, {type} high, {type} x) {{");
            helpers.AppendLine($"    {type} t = clamp((x - low) / (high - low), {type}(0.0), {type}(1.0));");
            helpers.AppendLine("    return t * t * (3.0 - 2.0 * t);\n}");
            if (type != "float")
                helpers.AppendLine($"{type} RU_CompatSmoothstep(float low, float high, {type} x) {{ return RU_CompatSmoothstep({type}(low), {type}(high), x); }}");
        }
        //辅助函数放在版本和扩展声明之后，不改变后续 #line 的源位置。
        Match header = Regex.Match(rewritten, @"\A\s*#version[^\r\n]*(?:\r?\n)(?:\s*#extension[^\r\n]*(?:\r?\n))*");
        if (!header.Success) throw new InvalidDataException("数学兼容转换需要 GLSL #version 声明。");
        if (powers.Count > 0)
            Record(manifest, "常量正整数幂 2～64 使用乘法展开；负底数按整数幂计算，属于旧 Shader 写法的兼容处理，并非 HLSL pow 的负底数语义保证。");
        if (smooth)
            Record(manifest, "smoothstep 按 HLSL 饱和 Hermite 公式展开，支持反向边界；相等边界仍要求作者避免除零。");
        return rewritten.Insert(header.Length, helpers.ToString());
    }

    //递归处理嵌套调用，参数中的构造器、下标和逗号不会拆成外层参数。
    private static string RewriteCalls(string source, SortedSet<int> powers, ref bool smooth)
    {
        var result = new StringBuilder();
        int cursor = 0;
        foreach (Match call in Calls.Matches(source))
        {
            if (call.Index < cursor || !call.Groups["function"].Success) continue;
            int open = call.Index + call.Length - 1;
            var arguments = Arguments(source, open, out int end);
            for (int i = 0; i < arguments.Count; i++) arguments[i] = RewriteCalls(arguments[i], powers, ref smooth);
            string function = call.Groups["function"].Value;
            string replacement;
            if (function == "smoothstep" && arguments.Count == 3)
            {
                smooth = true;
                replacement = "RU_CompatSmoothstep(" + string.Join(",", arguments) + ")";
            }
            else if (function == "pow" && arguments.Count == 2 && IntegerExponent(arguments[1], out int exponent))
            {
                powers.Add(exponent);
                replacement = $"RU_CompatPow{exponent}({arguments[0]})";
            }
            else replacement = function + "(" + string.Join(",", arguments) + ")";
            result.Append(source, cursor, call.Index - cursor).Append(replacement);
            cursor = end + 1;
        }
        return result.Append(source, cursor, source.Length - cursor).ToString();
    }

    //输入已通过 Slang 语法检查，仍对截断输出明确报告错误。
    private static List<string> Arguments(string source, int open, out int end)
    {
        var arguments = new List<string>();
        int depth = 0, start = open + 1;
        for (int i = open + 1; i < source.Length; i++)
        {
            char c = source[i];
            if (c == ')' && depth == 0)
            {
                arguments.Add(source[start..i]); end = i; return arguments;
            }
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == ',' && depth == 0) { arguments.Add(source[start..i]); start = i + 1; }
        }
        throw new InvalidDataException("Slang 数学函数调用括号未闭合。");
    }

    //只接受标量常量或各分量相同的向量常量，动态指数与分数幂保持原函数。
    private static bool IntegerExponent(string source, out int exponent)
    {
        exponent = 0;
        string value = source.Trim();
        Match vector = Regex.Match(value, @"^vec[234]\(([^()]*)\)$");
        string[] parts = vector.Success ? vector.Groups[1].Value.Split(',') : new[] { value };
        double expected = 0;
        foreach (string part in parts)
        {
            if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                || number < 2 || number > 64 || number != Math.Truncate(number)) return false;
            if (expected != 0 && number != expected) return false;
            expected = number;
        }
        exponent = (int)expected;
        return true;
    }

    //转换清单与命令行均可追踪兼容处理，不把数学改写隐藏为普通编译成功。
    private static void Record(ShaderLabManifest manifest, string message)
    {
        if (manifest != null && manifest.approximations.Contains(message)) return;
        manifest?.approximations.Add(message);
        Console.WriteLine("[数学兼容] " + message);
    }
}
