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

//按资源目录匹配并打开对应的只读存储实现。
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

    private sealed class DirectoryCatalogStoreProvider : ICatalogStoreProvider
    {
        public string Name => ContentFiles.CATALOG_FILE_NAME;
        public bool CanOpen(string root) => File.Exists(Path.Combine(root, ContentFiles.CATALOG_FILE_NAME));
        public ICatalogStore Open(string root) => new DirectoryCatalogStore(root);
    }

    private sealed class ZipCatalogStoreProvider : ICatalogStoreProvider
    {
        public string Name => ContentFiles.ARCHIVE_FILE_NAME;
        public bool CanOpen(string root) => File.Exists(Path.Combine(root, ContentFiles.ARCHIVE_FILE_NAME));
        public ICatalogStore Open(string root) => new ZipCatalogStore(root, ContentFiles.Inside(root, ContentFiles.ARCHIVE_FILE_NAME));
    }

    private sealed class DirectoryCatalogStore : ICatalogStore
    {
        public string Root { get; }

        internal DirectoryCatalogStore(string root) => Root = Path.GetFullPath(root);

        public string TryGetFilePath(string relative) => ContentFiles.Inside(Root, relative);
        public string Describe(string relative) => ContentFiles.Inside(Root, relative);
        public bool Exists(string relative) => File.Exists(ContentFiles.Inside(Root, relative));
        public Stream OpenRead(string relative) => File.OpenRead(ContentFiles.Inside(Root, relative));
        public byte[] ReadAllBytes(string relative) => File.ReadAllBytes(ContentFiles.Inside(Root, relative));
        public string ReadAllText(string relative) => File.ReadAllText(ContentFiles.Inside(Root, relative));
        public string GetTextureCacheKey(string relative) => ContentFiles.Inside(Root, relative);
        public void Dispose() { }
    }

    private sealed class ZipCatalogStore : ICatalogStore
    {
        private readonly object _gate = new();
        private readonly string _archivePath;
        private readonly ZipArchive _archive;
        private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public string Root { get; }

        internal ZipCatalogStore(string root, string archivePath)
        {
            Root = Path.GetFullPath(root);
            _archivePath = archivePath;
            FileStream stream = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
                        throw new InvalidDataException($"资源归档包含重复条目：{entry.FullName}（{archivePath}）");
                }
            }
            catch
            {
                if (_archive != null) _archive.Dispose();
                else stream.Dispose();
                throw;
            }
        }

        public string TryGetFilePath(string relative)
        {
            ContentFiles.NormalizeRelativePath(relative);
            return null;
        }

        public string Describe(string relative)
            => _archivePath + "!" + ContentFiles.NormalizeRelativePath(relative);

        public bool Exists(string relative)
            => _entries.ContainsKey(ContentFiles.NormalizeRelativePath(relative));

        public Stream OpenRead(string relative)
            => new MemoryStream(ReadAllBytes(relative), writable: false);

        public byte[] ReadAllBytes(string relative)
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

        public string GetTextureCacheKey(string relative)
        {
            string normalized = ContentFiles.NormalizeRelativePath(relative);
            if (!_entries.TryGetValue(normalized, out ZipArchiveEntry entry))
                throw new FileNotFoundException($"资源归档缺少条目：{normalized}", Describe(normalized));
            if (normalized.StartsWith("blobs/", StringComparison.Ordinal))
                return "rimunity:v1:blob:" + normalized;
            return $"rimunity:v1:zip:{_archivePath}|{normalized}|{entry.Crc32:x8}|{entry.Length}";
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _archive.Dispose();
            }
        }
    }
}
