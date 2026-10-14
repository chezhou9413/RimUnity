using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//给标准导出的 UI 程序加入与原生 UI 相同的遮罩覆盖率入口。
internal static class UiMaskAdapter
{
    internal static string Apply(string source, string vertex)
    {
        if (source.Contains("_UiMaskCount")) throw new InvalidDataException("自定义 Shader 占用了 RimUnity UI 遮罩保留名称。");
        Match block = Regex.Matches(source, @"layout\(([^)]*)\)\s*uniform\s+\w+\s*\{[^{}]*\}\s*(\w+)\s*;").Cast<Match>()
            .SingleOrDefault(m => Regex.IsMatch(m.Groups[1].Value, @"\bset\s*=\s*2\b") && Regex.IsMatch(m.Groups[1].Value, @"\bbinding\s*=\s*0\b"));
        string instance = "ruUiMaterial";
        const string fields = "\n    float _UiMaskCount; // @default = 0.0\n    float _UiRectMaskCount; // @default = 0.0\n";
        if (block != null)
        {
            instance = block.Groups[2].Value;
            source = source.Replace(block.Value, block.Value.Insert(block.Value.IndexOf('}'), fields));
        }
        else source = source.Insert(source.IndexOf('\n') + 1,
            "layout(set = 2, binding = 0) uniform RU_UiMaterial {" + fields + "} " + instance + ";\n");
        //仅顶点阶段使用的 VAT/VTO 贴图随后也会合入片元，分配遮罩前必须预留两阶段所有槽位。
        int nextBinding = Regex.Matches(vertex + "\n" + source, @"layout\(([^)]*)\)").Cast<Match>()
            .Where(m => Regex.IsMatch(m.Groups[1].Value, @"\bset\s*=\s*2\b"))
            .Select(m => Regex.Match(m.Groups[1].Value, @"\bbinding\s*=\s*(\d+)")).Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max() + 1;
        using var stream = typeof(UiMaskAdapter).Assembly.GetManifestResourceStream("RimUnity.ShaderTool.UiMasks.glsl")
            ?? throw new InvalidOperationException("导出工具缺少 UI 遮罩程序。");
        string code = new StreamReader(stream).ReadToEnd().Replace("material.", instance + ".");
        //原程序用满宿主的十六个纹理槽时，遮罩透明度改用存储缓冲，仍保留相同的过滤与环绕算法。
        int textures = Regex.Matches(vertex + "\n" + source, @"uniform\s+sampler(?:2D|Cube)\s+(\w+)\s*;").Cast<Match>()
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Count();
        if (textures >= 16)
        {
            code = code.Replace("layout(set = 2, binding = 3) uniform sampler2D _UiMaskAtlas;",
                "layout(std430, set = 2, binding = 3) readonly buffer _UiMaskPixels { uint values[]; } maskPixels;\n"
                + "float maskFetch(ivec2 p, int width) { uint n = uint(p.y * width + p.x); return float((maskPixels.values[n / 4u] >> ((n % 4u) * 8u)) & 255u) / 255.0; }");
            code = Regex.Replace(code, @"texelFetch\(_UiMaskAtlas, (.*), 0\)\.a", "maskFetch($1, int(options.z))");
        }
        code = Regex.Replace(code, @"binding = (\d+)", m => "binding = " + (nextBinding + int.Parse(m.Groups[1].Value) - 2));
        Match output = Regex.Match(source, @"layout\(location\s*=\s*0\)\s*out\s+vec4\s+(\w+)\s*;");
        if (!output.Success) throw new NotSupportedException("UI 遮罩需要 location 0 的 vec4 颜色输出。");
        Match main = Regex.Match(source, @"\bvoid\s+main\s*\(\s*\)");
        if (!main.Success) throw new InvalidDataException("UI 片元缺少 main 函数。");
        source = source.Remove(main.Index, main.Length).Insert(main.Index, "void ruUiOriginalMain()");
        return source + "\n" + code + "\nvoid main() { applyUiMasks(); ruUiOriginalMain(); " + output.Groups[1].Value + ".a *= rectCoverage(); }\n";
    }
}
