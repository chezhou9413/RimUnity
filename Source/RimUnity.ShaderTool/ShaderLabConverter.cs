using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//描述文件最后写入，调用方只发布两个阶段都成功的转换目录。
internal static class ShaderLabConverter
{
    //保留适配后的 HLSL，编译失败可直接定位原函数和明确绑定。
    internal static void Convert(string sourceFile, string directory, string profile, string[] keywords, int pass = -1, string colorSpace = "Gamma")
    {
        string path = Path.GetFullPath(sourceFile);
        if (!path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("自动转换只接受 .shader 源文件；ShaderGraph 需先人工迁移。");
        string generatedGraph = BuiltInGraphLowering.Lower(File.ReadAllText(path));
        if (generatedGraph != null && profile != "world") throw new NotSupportedException("BuiltIn Lit Graph 需要世界绘制配置。");
        string source = generatedGraph ?? ExpandIncludes(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        source = UnlitSurfaceLowering.Lower(source);
        ShaderLabSource shader = ShaderLabParser.Parse(source, profile, keywords, pass);
        if (colorSpace is not ("Gamma" or "Linear")) throw new ArgumentException("颜色空间必须为 Gamma 或 Linear。");
        shader.Manifest.colorSpace = colorSpace;
        if (generatedGraph != null) shader.Manifest.approximations.Add("BuiltIn Lit 使用地图太阳、月光与天空环境色；不使用 Unity 烘焙光照、局部灯光和反射探针。");
        if (colorSpace == "Linear") shader.Manifest.approximations.Add("程序按 Linear 计算后编码到宿主 UNorm 场景；透明叠加沿用宿主的 Gamma 混合，HDR 超出场景格式范围的部分仍会截断。");
        string adapted = ShaderLabBindings.Adapt(shader);
        Directory.CreateDirectory(directory);
        string descriptor = Path.Combine(directory, "shader.json");
        if (File.Exists(descriptor)) File.Delete(descriptor);
        string hlsl = Path.Combine(directory, "shader.hlsl");
        File.WriteAllText(hlsl, adapted, new UTF8Encoding(false));
        ShaderCompiler.Hlsl(hlsl, shader.Manifest.vertexEntry, "Vertex", Path.Combine(directory, shader.Manifest.vertexFile), shader.Manifest, false);
        ShaderCompiler.Hlsl(hlsl, shader.Manifest.fragmentEntry, "Fragment", Path.Combine(directory, shader.Manifest.fragmentFile), shader.Manifest, false);
        ShaderLabStages.Complete(directory, shader.Manifest);
        File.WriteAllText(descriptor, JsonSerializer.Serialize(shader.Manifest,
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
        Console.WriteLine("ShaderLab 转换完成：" + shader.Manifest.shaderName + " [" + profile + "]");
    }

    //工程包含按文件相对路径展开，Unity 包包含由对应的 HLSL 定义处理。
    private static string ExpandIncludes(string path, HashSet<string> stack)
    {
        if (!stack.Add(path)) throw new InvalidDataException("Shader 包含形成循环：" + path);
        try
        {
            string source = ShaderLabTokens.WithoutComments(File.ReadAllText(path));
            return Regex.Replace(source, @"(?m)^\s*#include\s+""([^""]+)""[^\r\n]*", match =>
            {
                string include = match.Groups[1].Value;
                string candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), include));
                return File.Exists(candidate) ? ExpandIncludes(candidate, stack) : match.Value;
            });
        }
        finally { stack.Remove(path); }
    }
}
