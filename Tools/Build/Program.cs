using System.Diagnostics;
using System.Text.Json;

namespace RimUnity.Build;

//构建入口只依赖仓库已经要求的 .NET SDK，不启动游戏或 Unity。
internal static class Program
{
    internal static readonly string[] RuntimeFiles = ["RimUnity.Core.dll", "RimUnity.dll", "RimUnity.Spine.dll"];
    internal static string Root { get; private set; } = "";

    //解析命令并将子进程失败作为整个构建失败报告。
    private static int Main(string[] args)
    {
        try
        {
            Root = FindRoot();
            if (args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("""
                    RimUnity build (.NET 10 SDK; no PowerShell required)
                    Windows: build.cmd [build|pack|docs] [options]
                    Linux/macOS: sh build.sh [build|pack|docs] [options]

                    build (default)  Build runtime, embedded package and ShaderTool.
                    pack             Package existing outputs; does not build or deploy.
                    docs             Synchronize the offline documentation mirror.
                    --game PATH      RimWorld 1.7 installation / assembly directory.
                    --harmony PATH   Harmony 1.7/Assemblies/0Harmony.dll.
                    --skip-embedded   Build standalone runtime and ShaderTool only.
                    --output PATH    Destination for pack (default: artifacts).

                    Paths: command line > build.local.json > existing environment > Steam detection.
                    Copy build.example.json to build.local.json to save non-Steam paths.
                    Local configuration paths are relative to the repository; CLI paths to your shell.
                    Outputs: 1.7/Assemblies, artifacts/Embedded, Tools/bin/ShaderTool.
                    Advanced PowerShell verification/deployment tools remain optional in Tools/.
                    """);
                return 0;
            }
            var options = Parse(args, out string command);
            if (command == "docs") { Manual.Sync(); return 0; }
            if (command == "pack") { Packages.Create(options.GetValueOrDefault("--output")); return 0; }
            var paths = GamePaths.Resolve(Root, options);
            Console.WriteLine($"Game: {paths.Game}\nHarmony: {paths.Harmony}");
            if (command == "embedded")
                Embedded.Build(paths, options.GetValueOrDefault("--runtime"), options.GetValueOrDefault("--output"), options.GetValueOrDefault("--built-at"));
            else
            {
                BuildProject("Source/RimUnity.Spine/RimUnity.Spine.csproj", paths, "-p:SkipEmbedded=true");
                if (!options.ContainsKey("--skip-embedded")) Embedded.Build(paths);
                BuildProject("Source/RimUnity.ShaderTool/RimUnity.ShaderTool.csproj", paths);
                Console.WriteLine("Build complete: 1.7/Assemblies, artifacts/Embedded (unless skipped), Tools/bin/ShaderTool.");
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("RimUnity build failed: " + error.Message);
            return 1;
        }
    }

    //各命令只接受自身使用的参数，拼写错误和无效组合直接报错。
    private static Dictionary<string, string> Parse(string[] args, out string command)
    {
        int index = 0;
        command = args.Length > 0 && !args[0].StartsWith('-') ? args[index++] : "build";
        string[] allowed = command switch
        {
            "build" => ["--game", "--harmony", "--skip-embedded"],
            "embedded" => ["--game", "--harmony", "--runtime", "--output", "--built-at"],
            "pack" => ["--output"],
            "docs" => [],
            _ => throw new ArgumentException("Unknown command: " + command + ". Use --help.")
        };
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        while (index < args.Length)
        {
            string option = args[index++];
            if (!allowed.Contains(option)) throw new ArgumentException("Unknown option for " + command + ": " + option);
            string value = "true";
            if (option != "--skip-embedded")
            {
                if (index == args.Length || args[index].StartsWith("--") || string.IsNullOrWhiteSpace(args[index]))
                    throw new ArgumentException("Missing value for " + option);
                value = args[index++];
            }
            if (!options.TryAdd(option, value)) throw new ArgumentException("Duplicate option: " + option);
        }
        return options;
    }

    //从构建工具位置定位仓库，允许在其他工作目录调用根入口。
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Source/RimUnity/RimUnity.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot find the RimUnity source repository.");
    }

    //为所有源项目传递同一组已验证的引用路径。
    internal static void BuildProject(string project, GamePaths paths, params string[] properties)
        => Run("dotnet", ["build", Path.Combine(Root, project), "-c", "Release", "--nologo",
            "-p:RimWorldDir=" + paths.Game, "-p:HarmonyPath=" + paths.Harmony, .. properties]);

    //使用参数列表直接启动工具，路径中的空格不需要拼接 shell 引号。
    internal static void Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Root, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + executable);
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} exited with code {process.ExitCode}.");
    }

    //版本号沿用 Unity 包与运行时共用的发布号。
    internal static string Version()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "UnityPackage/package.json")));
        return manifest.RootElement.GetProperty("version").GetString()!;
    }
}
