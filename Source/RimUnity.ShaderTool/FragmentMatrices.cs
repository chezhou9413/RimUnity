using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//宿主对象块只绑定顶点阶段，片元需要的矩阵按列通过常量插值传递。
internal static class FragmentMatrices
{
    //保留原矩阵值与乘法方向，不为片元额外声明不受支持的描述符。
    internal static (string Vertex, string Fragment) Adapt(string vertex, string fragment)
    {
        string pattern = @"layout\([^)]*\)\s*uniform\s+block_RU_(CameraData|ObjectData)_\d+\s*\{\s*mat4\s+(\w+)\s*;\s*\}\s*(\w+)\s*;";
        var blocks = Regex.Matches(fragment, pattern).Cast<Match>().ToArray();
        if (blocks.Length == 0) return (vertex, fragment);
        int location = Regex.Matches(vertex, @"layout\(location\s*=\s*(\d+)\)\s*(?:flat\s+)?out\b")
            .Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;
        var assignments = new StringBuilder();
        foreach (Match block in blocks)
        {
            string kind = block.Groups[1].Value;
            Match input = Regex.Matches(vertex, pattern).Cast<Match>().SingleOrDefault(m => m.Groups[1].Value == kind);
            if (input == null) { vertex = Insert(vertex, block.Value); input = block; }
            string prefix = "RU_Fragment" + kind;
            for (int column = 0; column < 4; column++)
            {
                if (location >= 32) throw new NotSupportedException("片元矩阵传递超出阶段插值容量。");
                string name = prefix + column;
                vertex = Insert(vertex, $"layout(location = {location}) flat out vec4 {name};");
                fragment = Insert(fragment, $"layout(location = {location++}) flat in vec4 {name};");
                assignments.AppendLine(name + " = " + input.Groups[3].Value + "." + input.Groups[2].Value + "[" + column + "];");
            }
            fragment = fragment.Replace(block.Value, "");
            fragment = Regex.Replace(fragment, @"\b" + block.Groups[3].Value + @"\." + block.Groups[2].Value + @"\b",
                "mat4(" + string.Join(",", Enumerable.Range(0, 4).Select(i => prefix + i)) + ")");
        }
        vertex = Regex.Replace(vertex, @"\bvoid\s+main\s*\(\s*\)\s*\{", m => m.Value + "\n" + assignments);
        return (vertex, fragment);
    }

    //接口声明放到版本指令后。
    private static string Insert(string source, string declaration) => source.Insert(source.IndexOf('\n') + 1, declaration + "\n");
}
