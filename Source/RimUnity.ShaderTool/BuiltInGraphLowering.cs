using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

// Generated BuiltIn metallic graph ABI; no asset identity or prefab-specific rules.
internal static class BuiltInGraphLowering
{
    internal static string Lower(string source)
    {
        if (!Regex.IsMatch(source, "\"ShaderGraphTargetId\"\\s*=\\s*\"BuiltInLitSubTarget\"")) return null;
        int pass = source.IndexOf("\nPass", StringComparison.Ordinal);
        if (pass < 0) throw new InvalidDataException("生成的 Lit Graph 缺少 Pass。");
        int program = source.IndexOf("HLSLPROGRAM", pass, StringComparison.Ordinal);
        int end = program < 0 ? -1 : source.IndexOf("ENDHLSL", program, StringComparison.Ordinal);
        if (program < 0 || end < 0) throw new InvalidDataException("生成的 Lit Graph 缺少前向程序。");
        string body = source[program..end];
        if (!body.Contains("float Metallic;") || !body.Contains("float3 NormalTS;")) throw new NotSupportedException("Lit Graph 当前要求 Metallic 工作流和切线法线。");
        if (Regex.IsMatch(body, @"(?m)^#define _BUILTIN_ALPHATEST_ON\s+1\b") || Regex.IsMatch(body, @"(?m)^#define _BUILTIN_ALPHAMODULATE_ON\s+1\b")) throw new NotSupportedException("Lit Graph AlphaClip/Multiply 尚未接通。");
        if (!body.Contains("// GraphIncludes: <None>")) throw new NotSupportedException("自定义 Graph Include 需要显式适配。");
        string inputs = Structure(body, "SurfaceDescriptionInputs") + Structure(body, "VertexDescriptionInputs");
        CheckInputs(Structure(body, "SurfaceDescriptionInputs"), new[] { "WorldSpaceNormal", "TangentSpaceNormal", "WorldSpaceViewDirection", "uv0", "uv1" });
        CheckInputs(Structure(body, "VertexDescriptionInputs"), new[] { "ObjectSpaceNormal", "ObjectSpaceTangent", "ObjectSpacePosition" });
        string helpers = Between(body, "// Graph Functions", "// Custom interpolators pre vertex");
        string vertex = Structure(body, "VertexDescription") + Function(body, "VertexDescriptionFunction");
        string surface = Structure(body, "SurfaceDescription") + Function(body, "SurfaceDescriptionFunction");
        string fields = Regex.Match(body, @"CBUFFER_START\(UnityPerMaterial\)(.*?)CBUFFER_END", RegexOptions.Singleline).Groups[1].Value;
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        surface = Regex.Replace(surface, @"UnityTexture2D\s+(\w+)\s*=\s*UnityBuildTexture2DStructNoScale\((\w+)\)\s*;", m => { textures.Add(m.Groups[1].Value, m.Groups[2].Value); return ""; });
        foreach (var pair in textures)
        {
            string name = Regex.Escape(pair.Key);
            surface = Regex.Replace(surface, @"SAMPLE_TEXTURE2D\(\s*" + name + @"\.tex\s*,\s*" + name + @"\.samplerstate\s*,\s*" + name + @"\.GetTransformedUV\((\w+)\)\s*\)", m => "tex2D(" + pair.Value + "," + m.Groups[1].Value + ")");
        }
        if (surface.Contains("UnityTexture") || surface.Contains("SAMPLE_TEXTURE")) throw new NotSupportedException("Graph 纹理结构用法尚未适配。");
        surface = surface.Replace("LinearToSRGB(", "RU_GraphLinearToSRGB(");
        string declarations = string.Join("\n", textures.Values.Distinct().Select(t => "sampler2D " + t + ";"));
        int sub = source.IndexOf("SubShader", StringComparison.Ordinal);
        string tags = Regex.Match(source[sub..pass], @"Tags\s*\{([^{}]*)\}", RegexOptions.Singleline).Value;
        string states = Regex.Match(source[pass..program], @"// Render State(.*?)// Debug", RegexOptions.Singleline).Groups[1].Value;
        string defines = Regex.IsMatch(body, @"(?m)^#define _BUILTIN_ALPHAPREMULTIPLY_ON\s+1\b") ? "#define _ALPHAPREMULTIPLY_ON 1\n" : "";
        return source[..sub] + "SubShader { " + tags + " Pass { " + states + " HLSLPROGRAM\n#pragma vertex RU_GraphVert\n#pragma fragment RU_GraphFrag\n#include \"UnityCG.cginc\"\n" + defines + fields + declarations + inputs + helpers + vertex + surface + GraphLighting.Program + "\nENDHLSL\n} } }";
    }
    private static void CheckInputs(string declaration, string[] allowed)
    {
        foreach (Match m in Regex.Matches(declaration, @"\bfloat[1-4]?\s+(\w+)\s*;"))
            if (!allowed.Contains(m.Groups[1].Value)) throw new NotSupportedException("Lit Graph 输入尚未接通：" + m.Groups[1].Value);
    }
    private static string Between(string text, string start, string end)
    {
        int a = text.IndexOf(start, StringComparison.Ordinal);
        int b = a < 0 ? -1 : text.IndexOf(end, a, StringComparison.Ordinal);
        if (a < 0 || b < 0) throw new InvalidDataException("Graph 缺少节点函数区。");
        return text[(a + start.Length)..b];
    }
    private static string Structure(string text, string name)
    {
        Match m = Regex.Match(text, @"\bstruct\s+" + name + @"\s*\{[^{}]*\}\s*;", RegexOptions.Singleline);
        if (!m.Success) throw new InvalidDataException("Graph 缺少结构：" + name);
        return m.Value + "\n";
    }
    private static string Function(string text, string name)
    {
        Match m = Regex.Match(text, @"\b\w+\s+" + name + @"\s*\([^)]*\)\s*\{");
        if (!m.Success) throw new InvalidDataException("Graph 缺少函数：" + name);
        int start = text.IndexOf('{', m.Index), depth = 1, end = start + 1;
        while (end < text.Length && depth > 0) { if (text[end] == '{') depth++; else if (text[end] == '}') depth--; end++; }
        if (depth != 0) throw new InvalidDataException("Graph 函数花括号不完整：" + name);
        return text[m.Index..end] + "\n";
    }
}
