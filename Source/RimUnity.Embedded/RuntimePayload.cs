using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Verse;

namespace RimUnity.Embedded;

//从未加载的容器读取载荷，避免同名程序集绑定到第一个副本。
internal sealed class RuntimePayload
{
    internal string Path, Hash;
    internal byte[] Bytes;
    internal Version Version;
    internal int Protocol;
    internal DateTimeOffset BuiltAtUtc = DateTimeOffset.MinValue;

    //只读取 PE 资源，不执行候选运行时的代码。
    internal static RuntimePayload Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var resource = metadata.ManifestResources.Select(metadata.GetManifestResource)
            .Single(r => metadata.GetString(r.Name) == "RimUnity.Payload.zip" && r.Implementation.IsNil);
        var block = pe.GetSectionData(pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress)
            .GetReader(checked((int)resource.Offset), checked(pe.PEHeaders.CorHeader.ResourcesDirectory.Size - (int)resource.Offset));
        var bytes = block.ReadBytes(block.ReadInt32());
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var json = JsonDocument.Parse((zip.GetEntry("runtime.json") ?? throw new InvalidDataException("Missing runtime.json: " + path)).Open());
        //将载荷清单格式问题统一为候选数据错误，避免缺字段异常越过候选回退边界。
        var manifest = json.RootElement;
        if (manifest.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("runtime.json must be an object: " + path);
        if (!manifest.TryGetProperty("version", out var versionField) || versionField.ValueKind != JsonValueKind.String ||
            !System.Version.TryParse(versionField.GetString(), out var version))
            throw new InvalidDataException("Missing or invalid runtime version: " + path);
        if (!manifest.TryGetProperty("protocol", out var protocolField) || protocolField.ValueKind != JsonValueKind.Number ||
            !protocolField.TryGetInt32(out int protocol))
            throw new InvalidDataException("Missing or invalid runtime protocol: " + path);
        var builtAtUtc = DateTimeOffset.MinValue;
        if (manifest.TryGetProperty("builtAtUtc", out var builtAt) &&
            (builtAt.ValueKind != JsonValueKind.String || !BuildMetadata.TryTime(builtAt.GetString(), out builtAtUtc)))
        {
            builtAtUtc = DateTimeOffset.MinValue;
            Log.Warning("[RimUnity] Invalid builtAtUtc; treating as unknown: " + path);
        }
        return new RuntimePayload { BuiltAtUtc = builtAtUtc, Path = path, Bytes = bytes, Hash = Convert.ToHexString(SHA256.HashData(bytes)),
            Version = version, Protocol = protocol };
    }

    //在 CLR 装载和原生挂载之前校验完整的资源契约，不执行候选代码。
    internal string Prepare(string cache)
    {
        using (var zip = new ZipArchive(new MemoryStream(Bytes)))
        {
            var paths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                Inside(cache, entry.FullName);
                if (!paths.Add(entry.FullName.Replace('\\', '/')))
                    throw new InvalidDataException("Duplicate payload entry: " + entry.FullName);
            }
            foreach (string required in new[] { "Assemblies/RimUnity.Core.dll", "Assemblies/RimUnity.dll",
                "Assemblies/RimUnity.Spine.dll", "Defs/RimUnityShaders.xml" })
                if (zip.GetEntry(required) is not { Length: > 0 })
                    throw new InvalidDataException("Missing runtime resource: " + required);
            if (!zip.Entries.Any(e => e.FullName.StartsWith("Shaders/RimUnity/", StringComparison.Ordinal) && e.Length > 0))
                throw new InvalidDataException("Missing runtime shaders");
        }
        string root = Extract(cache);
        foreach (string name in new[] { "RimUnity.Core", "RimUnity", "RimUnity.Spine" })
            if (System.Reflection.AssemblyName.GetAssemblyName(System.IO.Path.Combine(root, "Assemblies", name + ".dll")).Name != name)
                throw new InvalidDataException("Runtime assembly identity mismatch: " + name);
        return root;
    }

    //哈希目录只容纳一份完整资源；缓存损坏必须明确报错。
    internal string Extract(string cache)
    {
        string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(cache, Hash));
        using var zip = new ZipArchive(new MemoryStream(Bytes));
        if (Directory.Exists(target))
        {
            foreach (var entry in zip.Entries)
            {
                using var expected = entry.Open();
                using var actual = File.OpenRead(Inside(target, entry.FullName));
                if (!SHA256.HashData(expected).SequenceEqual(SHA256.HashData(actual)))
                    throw new InvalidDataException("内置缓存损坏 / Corrupt embedded cache: " + target);
            }
            return target;
        }
        string stage = target + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var entry in zip.Entries)
            {
                string file = Inside(stage, entry.FullName);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                entry.ExtractToFile(file);
            }
            Directory.Move(stage, target);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        return target;
    }

    //限制归档路径在本次缓存目录内。
    private static string Inside(string root, string relative)
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (!path.StartsWith(System.IO.Path.GetFullPath(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid embedded resource path: " + relative);
        return path;
    }
}
