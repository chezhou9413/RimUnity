using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace RimUnity.ShaderTool;

//编译工具只从指定游戏目录解析依赖，不发布游戏 DLL。
internal static class Program
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);

    //设置解析器后再进入引用 Waylith 的代码，避免 JIT 提前解析外部程序集。
    private static int Main(string[] args)
    {
        //与 Unity 导出器的 UTF-8 管道保持一致，保留中文诊断。
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        try
        {
            if (args.Length < 3) throw new ArgumentException("用法：RimUnity.ShaderTool <gameRoot> verify <shader.glsl>... | hlsl <source.hlsl> <entry> <Vertex|Fragment> <output.glsl> | shaderlab <source.shader> <outputDir> <world|ui> [keywords逗号分隔] [pass] [Gamma|Linear] | linear <vertex.glsl> <fragment.glsl> <outputDir>");
            string root = Path.GetFullPath(args[0]);
            AssemblyLoadContext.Default.Resolving += (_, name) => Resolve(root, name);
            if (!SetDllDirectory(root)) throw new InvalidOperationException("无法设置 Slang 原生依赖目录。");
            return Run(args);
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { SetDllDirectory(null); }
    }

    //使用游戏同目录的版本，不能用开发机另一份依赖自动替代。
    private static Assembly Resolve(string root, AssemblyName name)
    {
        string path = Path.Combine(root, name.Name + ".dll");
        return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        if (args[1] == "preprocess" && args.Length == 3) ShaderPreprocessor.Write(args[2]);
        else if (args[1] == "inspect" && args.Length == 4)
        {
            string libraryPath = Path.GetFullPath(args[2]);
            //静态资源检查使用目标模组随附的 Core，游戏目录不提供该程序集。
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(libraryPath), "RimUnity.Core.dll"));
            Assembly library = AssemblyLoadContext.Default.LoadFromAssemblyPath(libraryPath);
            library.GetType("RimUnity.Assets.PrefabInspector", true).GetMethod("ValidateDirectory").Invoke(null, new object[] { args[3] });
        }
        else if (args[1] == "verify")
        {
            for (int i = 2; i < args.Length; i++) ShaderCompiler.Verify(args[i]);
        }
        else if (args[1] == "hlsl" && args.Length == 6) ShaderCompiler.Hlsl(args[2], args[3], args[4], args[5]);
        else if (args[1] == "linear" && args.Length == 5)
        {
            var (vertex, fragment) = RimUnity.Rendering.ShaderColorSpace.Convert(File.ReadAllText(args[2]), File.ReadAllText(args[3]));
            Directory.CreateDirectory(args[4]);
            string vertexPath = Path.Combine(args[4], "shader.vert.glsl"), fragmentPath = Path.Combine(args[4], "shader.frag.glsl");
            File.WriteAllText(vertexPath, vertex, new System.Text.UTF8Encoding(false));
            File.WriteAllText(fragmentPath, fragment, new System.Text.UTF8Encoding(false));
            ShaderCompiler.Verify(vertexPath); ShaderCompiler.Verify(fragmentPath);
        }
        else if (args[1] == "shaderlab" && args.Length is >= 5 and <= 8)
            ShaderLabConverter.Convert(args[2], args[3], args[4], args.Length >= 6 ? args[5].Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>(), args.Length >= 7 ? int.Parse(args[6]) : -1, args.Length == 8 ? args[7] : "Gamma");
        else throw new ArgumentException("无效命令。");
        return 0;
    }
}
