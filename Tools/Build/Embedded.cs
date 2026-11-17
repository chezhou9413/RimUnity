using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimUnity.Build;

//生成与现有内置加载器协议一致的载荷，构建时间和内容共同确定容器身份。
internal static class Embedded
{
    //固定输入与时间时，归档顺序和条目时间保持一致。
    internal static void Build(GamePaths paths, string? runtimeDirectory = null, string? outputDirectory = null, string? builtAt = null)
    {
        string root = Program.Root;
        string runtime = Path.GetFullPath(runtimeDirectory ?? Path.Combine(root, "1.7/Assemblies"));
        string output = Path.GetFullPath(outputDirectory ?? Path.Combine(root, "artifacts/Embedded"));
        DateTimeOffset time = DateTimeOffset.UtcNow;
        if (builtAt != null && !DateTimeOffset.TryParseExact(builtAt,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time))
            throw new ArgumentException("--built-at must be an ISO 8601 UTC time ending in Z.");
        string timestamp = time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in Program.RuntimeFiles) files.Add("Assemblies/" + name, Path.Combine(runtime, name));
        foreach (string folder in new[] { "Defs", "Shaders", "Languages" })
        {
            if (!Directory.Exists(Path.Combine(root, "1.7", folder))) continue;
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "1.7", folder), "*", SearchOption.AllDirectories))
                files.Add(folder + "/" + Path.GetRelativePath(Path.Combine(root, "1.7", folder), file).Replace('\\', '/'), file);
        }
        files.Add("LICENSE", Path.Combine(root, "LICENSE"));
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "ThirdParty"), "*", SearchOption.AllDirectories))
            files.Add(Path.GetRelativePath(root, file).Replace('\\', '/'), file);
        foreach (string file in files.Values)
            if (!File.Exists(file)) throw new FileNotFoundException("Missing embedded input; build the runtime first: " + file);
        Directory.CreateDirectory(output);
        string payload = Path.Combine(output, "payload.zip");
        using (var zip = new ZipArchive(File.Create(payload), ZipArchiveMode.Create))
        {
            foreach (var pair in files)
            {
                using var input = File.OpenRead(pair.Value);
                using var target = Entry(zip, pair.Key).Open();
                input.CopyTo(target);
            }
            using var writer = new StreamWriter(Entry(zip, "runtime.json").Open(), new UTF8Encoding(false));
            writer.Write(JsonSerializer.Serialize(new { version = Program.Version(), protocol = 1, builtAtUtc = timestamp, coordinatorRevision = 1 }));
        }
        var identity = new StringBuilder(Hash(payload));
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "Source/RimUnity.Embedded")).OrderBy(Path.GetFileName, StringComparer.Ordinal))
            identity.Append(Path.GetFileName(file)).Append(Hash(file));
        string container = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())));
        Program.BuildProject("Source/RimUnity.Embedded/RimUnity.Embedded.csproj", paths,
            "-p:PayloadPath=" + payload, "-p:ContainerId=" + container, "-p:RimUnityBuiltAtUtc=" + timestamp,
            "-p:OutputPath=" + output.Replace('\\', '/') + "/");
        File.Copy(Path.Combine(root, "Tools/RimUnity.Embedded.targets"), Path.Combine(output, "RimUnity.Embedded.targets"), true);
        Directory.CreateDirectory(Path.Combine(output, "References"));
        foreach (string name in Program.RuntimeFiles) File.Copy(Path.Combine(runtime, name), Path.Combine(output, "References", name), true);
        Console.WriteLine("Embedded runtime: " + output);
    }

    //固定 ZIP 时间，避免同一载荷仅因文件复制时间不同而改变身份。
    private static ZipArchiveEntry Entry(ZipArchive zip, string name)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return entry;
    }

    //文件按流计算身份，不把整个载荷留在内存中。
    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
