using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace RimUnity.Build;

//打包现有产物，输出清单沿用独立模组、内置接入包与 Unity UPM 的目录约定。
internal static class Packages
{
    //先核对全部输入和手册，再写归档；不编译、部署或启动程序。
    internal static void Create(string? outputDirectory)
    {
        string root = Program.Root, version = Program.Version();
        string output = Path.GetFullPath(outputDirectory ?? Path.Combine(root, "artifacts"));
        string runtime = Path.Combine(root, "1.7/Assemblies"), embedded = Path.Combine(root, "artifacts/Embedded");
        string tool = Path.Combine(root, "Tools/bin/ShaderTool");
        string[] toolNames = [OperatingSystem.IsWindows() ? "RimUnity.ShaderTool.exe" : "RimUnity.ShaderTool",
            "RimUnity.ShaderTool.dll", "RimUnity.ShaderTool.deps.json", "RimUnity.ShaderTool.runtimeconfig.json", "RimUnity.ShaderTool.pdb"];
        foreach (string name in Program.RuntimeFiles)
        {
            Require(Path.Combine(runtime, name));
            Require(Path.Combine(embedded, "References", name));
            if (FileVersionInfo.GetVersionInfo(Path.Combine(runtime, name)).FileVersion != version + ".0")
                throw new InvalidOperationException("Runtime version differs from package.json: " + name + ". Rebuild first.");
            if (Embedded.Hash(Path.Combine(runtime, name)) != Embedded.Hash(Path.Combine(embedded, "References", name)))
                throw new InvalidOperationException("Embedded references differ from runtime: " + name + ". Rebuild first.");
        }
        if (XDocument.Load(Path.Combine(root, "About/About.xml")).Root!.Element("modVersion")!.Value != version)
            throw new InvalidOperationException("About.xml and package.json versions differ.");
        foreach (string name in toolNames) Require(Path.Combine(tool, name));
        Require(Path.Combine(embedded, "RimUnity.Embedded.dll"));
        Require(Path.Combine(embedded, "RimUnity.Embedded.targets"));
        Manual.Sync(check: true);
        string[] names = [$"RimUnity-{version}.zip", $"RimUnity.Embedded-{version}.zip", $"com.chezhou.rimunity-{version}.tgz"];
        foreach (string name in names)
            if (File.Exists(Path.Combine(output, name))) throw new IOException("Archive exists; choose a new --output directory: " + Path.Combine(output, name));
        string stage = Path.Combine(root, "artifacts/Packaging", Guid.NewGuid().ToString("N"));
        string mod = Path.Combine(stage, "RimUnity"), container = Path.Combine(stage, "RimUnity.Embedded"), package = Path.Combine(stage, "package");
        foreach (string file in new[] { "About/About.xml", "About/PublishedFileId.txt", "About/Preview.png", "About/ModIcon.png",
            "LoadFolders.xml", "Analyzer.xml", "LICENSE", "README.md", "README.en.md", "1.6/README.txt" }) Copy(Path.Combine(root, file), Path.Combine(mod, file));
        foreach (string folder in new[] { "1.6/Languages", "1.7/Defs", "1.7/Shaders", "1.7/Languages", "Docs/zh-CN", "Docs/en", "Docs/Images", "ThirdParty" })
            CopyFolder(Path.Combine(root, folder), Path.Combine(mod, folder), file => Path.GetFileName(file) != "PreviewOpaque.frag.glsl");
        foreach (string name in Program.RuntimeFiles) Copy(Path.Combine(runtime, name), Path.Combine(mod, "1.7/Assemblies", name));
        foreach (string name in new[] { "RimUnity.Embedded.dll", "RimUnity.Embedded.targets" }) Copy(Path.Combine(embedded, name), Path.Combine(container, name));
        CopyFolder(Path.Combine(embedded, "References"), Path.Combine(container, "References"));
        Copy(Path.Combine(root, "Docs/zh-CN/EmbeddedRuntime.md"), Path.Combine(container, "README.zh-CN.md"));
        Copy(Path.Combine(root, "Docs/en/EmbeddedRuntime.md"), Path.Combine(container, "README.en.md"));
        Copy(Path.Combine(root, "LICENSE"), Path.Combine(container, "LICENSE"));
        CopyFolder(Path.Combine(root, "ThirdParty"), Path.Combine(container, "ThirdParty"));
        CopyFolder(Path.Combine(root, "UnityPackage"), package,
            file => !Path.GetRelativePath(Path.Combine(root, "UnityPackage"), file).Replace('\\', '/').StartsWith("Tools~/", StringComparison.Ordinal));
        Copy(Path.Combine(root, "LICENSE"), Path.Combine(package, "LICENSE"));
        foreach (string name in toolNames) Copy(Path.Combine(tool, name), Path.Combine(package, "Tools~/ShaderTool", name));
        Directory.CreateDirectory(output);
        ZipFile.CreateFromDirectory(mod, Path.Combine(output, names[0]), CompressionLevel.Optimal, true);
        ZipFile.CreateFromDirectory(container, Path.Combine(output, names[1]), CompressionLevel.Optimal, true);
        using (var file = File.Create(Path.Combine(output, names[2])))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            TarFile.CreateFromDirectory(package, gzip, includeBaseDirectory: true);
        File.WriteAllLines(Path.Combine(output, "SHA256SUMS.txt"), names.Select(name => Embedded.Hash(Path.Combine(output, name)).ToLowerInvariant() + "  " + name), new UTF8Encoding(false));
        foreach (string name in names) Console.WriteLine("Package: " + Path.Combine(output, name));
    }

    //缺少输入时给出重建指引，不生成不完整归档。
    private static void Require(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Missing build output: " + path + ". Run build.cmd / sh build.sh first.");
    }

    //只覆盖此次新建暂存目录中的文件。
    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    //按已有发布目录复制文件，保留相对路径。
    private static void CopyFolder(string source, string destination, Func<string, bool>? include = null)
    {
        if (!Directory.Exists(source)) return;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            if (include == null || include(file)) Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }
}
