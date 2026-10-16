using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace RimUnity.Embedded;

//从未加载的容器读取载荷，避免同名程序集绑定到第一个副本。
internal sealed class RuntimePayload
{
    internal string Path, Hash;
    internal byte[] Bytes;
    internal Version Version;
    internal int Protocol;

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
        using var json = JsonDocument.Parse(zip.GetEntry("runtime.json").Open());
        return new RuntimePayload { Path = path, Bytes = bytes, Hash = Convert.ToHexString(SHA256.HashData(bytes)),
            Version = Version.Parse(json.RootElement.GetProperty("version").GetString()),
            Protocol = json.RootElement.GetProperty("protocol").GetInt32() };
    }

    //哈希目录只容纳一份完整资源；缓存损坏必须明确报错。
    internal string Extract(string cache)
    {
        string target = System.IO.Path.Combine(cache, Hash);
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
