using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//资源目录的只读存储抽象；散文件目录、catalog.zip 与第三方自定义容器均实现此接口。
public interface ICatalogStore : IDisposable
{
    string Root { get; }
    string TryGetFilePath(string relative);
    string Describe(string relative);
    bool Exists(string relative);
    Stream OpenRead(string relative);
    string GetTextureCacheKey(string relative);

    byte[] ReadAllBytes(string relative)
    {
        using Stream stream = OpenRead(relative);
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    string ReadAllText(string relative)
    {
        using StreamReader reader = new(OpenRead(relative), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

//按资源目录匹配并打开对应的只读存储实现；每次 Open 必须返回可独立释放的存储实例。
public interface ICatalogStoreProvider
{
    string Name { get; }
    //探测所有模组的资源目录；只判断是否匹配，不打开或验证资源。
    bool CanOpen(string root);
    ICatalogStore Open(string root);
}

//统一管理内置与模组显式注册的存储提供程序；同一目录命中多个提供程序时明确报错。
public static class CatalogStoreRegistry
{
    private static readonly object s_gate = new();
    private static readonly List<ICatalogStoreProvider> s_providers = new()
    {
        new DirectoryCatalogStoreProvider(),
        new ZipCatalogStoreProvider()
    };
    private static readonly HashSet<Type> s_providerTypes = new()
    {
        typeof(DirectoryCatalogStoreProvider),
        typeof(ZipCatalogStoreProvider)
    };

    public static IReadOnlyList<ICatalogStoreProvider> Providers => _CopyProviders();

    //供模组在启动阶段显式注册自定义资源存储，按提供程序类型去重。
    public static void Register(ICatalogStoreProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (s_gate)
        {
            if (s_providerTypes.Add(provider.GetType()))
                s_providers.Add(provider);
        }
    }

    public static bool HasCatalog(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        string fullRoot = Path.GetFullPath(root);
        foreach (ICatalogStoreProvider provider in _CopyProviders())
        {
            if (_CanOpen(provider, fullRoot)) return true;
        }
        return false;
    }

    public static ICatalogStore Open(string root)
    {
        string fullRoot = Path.GetFullPath(root);
        List<ICatalogStoreProvider> matched = null;
        foreach (ICatalogStoreProvider provider in _CopyProviders())
        {
            if (!_CanOpen(provider, fullRoot)) continue;
            matched ??= new List<ICatalogStoreProvider>(2);
            matched.Add(provider);
        }
        if (matched == null || matched.Count == 0)
            throw new FileNotFoundException($"资源目录未找到有效 RimUnity 清单：{fullRoot}", Path.Combine(fullRoot, ContentFiles.CATALOG_FILE_NAME));
        if (matched.Count > 1)
            throw new InvalidDataException($"资源目录同时命中多个存储提供程序（{string.Join("、", matched.Select(p => p.Name))}），请只保留一种格式：{fullRoot}");
        ICatalogStoreProvider selected = matched[0];
        ICatalogStore store = selected.Open(fullRoot);
        if (store == null)
            throw new InvalidDataException($"存储提供程序 {selected.Name} 返回了空存储实例：{fullRoot}");
        return store;
    }

    //单个提供程序探测失败不能阻断其他模组；命中的存储打开失败仍直接报错。
    private static bool _CanOpen(ICatalogStoreProvider provider, string root)
    {
        try
        {
            return provider.CanOpen(root);
        }
        catch (Exception error)
        {
            var providerType = provider.GetType();
            Verse.Log.ErrorOnce($"[RimUnity] 存储提供程序探测失败：{providerType.FullName}（{root}）\n{error}",
                providerType.GetHashCode());
            return false;
        }
    }

    private static ICatalogStoreProvider[] _CopyProviders()
    {
        lock (s_gate)
        {
            return s_providers.ToArray();
        }
    }
}

//散文件目录提供程序，识别包含 catalog.json 的资源目录。
public class DirectoryCatalogStoreProvider : ICatalogStoreProvider
{
    public virtual string Name => ContentFiles.CATALOG_FILE_NAME;
    public virtual bool CanOpen(string root) => File.Exists(Path.Combine(root, ContentFiles.CATALOG_FILE_NAME));
    public virtual ICatalogStore Open(string root) => new DirectoryCatalogStore(root);
}

//单文件归档提供程序，识别包含 catalog.zip 的资源目录。
public class ZipCatalogStoreProvider : ICatalogStoreProvider
{
    public virtual string Name => ContentFiles.ARCHIVE_FILE_NAME;
    public virtual bool CanOpen(string root) => File.Exists(Path.Combine(root, ContentFiles.ARCHIVE_FILE_NAME));
    public virtual ICatalogStore Open(string root) => new ZipCatalogStore(root, ContentFiles.Inside(root, ContentFiles.ARCHIVE_FILE_NAME));
}

//基于散文件目录的只读资源存储实现，可供下游模组直接复用或继承包装；派生类若覆写 OpenRead 变换内容，应同步覆写 TryGetFilePath 返回 null。
public class DirectoryCatalogStore : ICatalogStore
{
    public string Root { get; }

    public DirectoryCatalogStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("资源根目录不能为空。", nameof(root));
        Root = Path.GetFullPath(root);
    }

    private string _ResolvePath(string relative)
        => ContentFiles.Inside(Root, ContentFiles.NormalizeRelativePath(relative));

    public virtual string TryGetFilePath(string relative) => _ResolvePath(relative);
    public virtual string Describe(string relative) => _ResolvePath(relative);
    public virtual bool Exists(string relative) => File.Exists(_ResolvePath(relative));
    public virtual Stream OpenRead(string relative) => File.OpenRead(_ResolvePath(relative));

    public virtual byte[] ReadAllBytes(string relative)
    {
        using Stream stream = OpenRead(relative);
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public virtual string ReadAllText(string relative)
    {
        using StreamReader reader = new(OpenRead(relative), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public virtual string GetTextureCacheKey(string relative) => _ResolvePath(relative);
    public virtual void Dispose() { }
}

//基于 ZIP 归档的只读资源存储实现，支持自定义归档路径或由下游模组继承扩展。
public class ZipCatalogStore : ICatalogStore
{
    private readonly object _gate = new();
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public string Root { get; }
    public string ArchivePath { get; }

    public ZipCatalogStore(string root, string archivePath)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("资源根目录不能为空。", nameof(root));
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("资源归档路径不能为空。", nameof(archivePath));
        Root = Path.GetFullPath(root);
        ArchivePath = Path.GetFullPath(archivePath);
        FileStream stream = new(ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            _archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (ZipArchiveEntry entry in _archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)
                    && (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal)))
                    continue;
                string normalized = ContentFiles.NormalizeRelativePath(entry.FullName);
                if (!_entries.TryAdd(normalized, entry))
                    throw new InvalidDataException($"资源归档包含重复条目：{entry.FullName}（{ArchivePath}）");
            }
        }
        catch
        {
            if (_archive != null) _archive.Dispose();
            else stream.Dispose();
            throw;
        }
    }

    public virtual string TryGetFilePath(string relative)
    {
        ContentFiles.NormalizeRelativePath(relative);
        return null;
    }

    public virtual string Describe(string relative)
        => ArchivePath + "!" + ContentFiles.NormalizeRelativePath(relative);

    public virtual bool Exists(string relative)
        => _entries.ContainsKey(ContentFiles.NormalizeRelativePath(relative));

    public virtual Stream OpenRead(string relative)
    {
        byte[] bytes = _ReadEntryBytes(relative);
        return new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
    }

    public virtual byte[] ReadAllBytes(string relative)
    {
        using Stream stream = OpenRead(relative);
        if (stream is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> segment)
            && memory.Position == 0 && segment.Offset == 0 && segment.Count == segment.Array.Length)
        {
            return segment.Array;
        }
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public virtual string ReadAllText(string relative)
    {
        using StreamReader reader = new(OpenRead(relative), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private byte[] _ReadEntryBytes(string relative)
    {
        string normalized = ContentFiles.NormalizeRelativePath(relative);
        if (!_entries.TryGetValue(normalized, out ZipArchiveEntry entry))
            throw new FileNotFoundException($"资源归档缺少条目：{normalized}", Describe(normalized));
        if (entry.Length > int.MaxValue)
            throw new InvalidDataException($"资源归档条目过大：{Describe(normalized)}");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using Stream stream = entry.Open();
            byte[] buffer = new byte[(int)entry.Length];
            stream.ReadExactly(buffer);
            if (stream.ReadByte() != -1)
                throw new InvalidDataException($"资源归档条目长度超出声明：{Describe(normalized)}");
            return buffer;
        }
    }

    public virtual string GetTextureCacheKey(string relative)
    {
        string normalized = ContentFiles.NormalizeRelativePath(relative);
        if (!_entries.TryGetValue(normalized, out ZipArchiveEntry entry))
            throw new FileNotFoundException($"资源归档缺少条目：{normalized}", Describe(normalized));
        if (ContentFiles.IsBlob(normalized))
            return "rimunity:v1:blob:" + normalized;
        return $"rimunity:v1:zip:{ArchivePath}|{normalized}|{entry.Crc32:x8}|{entry.Length}";
    }

    public virtual void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _archive.Dispose();
        }
    }
}
