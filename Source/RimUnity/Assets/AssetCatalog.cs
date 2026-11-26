using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//每个内容模组独立持有资源缓存；实例组件不进入共享缓存。
internal sealed class AssetCatalog : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    internal readonly string Root;
    internal readonly ICatalogStore Store;
    internal readonly Manifest Manifest;
    private readonly Dictionary<string, AssetRecord> records;
    private readonly Dictionary<string, AssetRecord> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RimUnity.Object> resources = new();
    private readonly HashSet<string> loading = new();
    private readonly Func<string, string> ownerRoot;
    //传入时存储由调用方缓存并持有；未传入时本清单自行打开并在释放时关闭。
    private readonly Func<string, ICatalogStore> _storeProvider;
    private readonly Dictionary<string, ICatalogStore> _sharedStores = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ICatalogStore> _ownedStores = new();
    private readonly HashSet<string> _verifiedSharedEntries = new(StringComparer.Ordinal);

    //清单在首次加载时校验，失败直接报告内容模组及资源路径。
    internal AssetCatalog(string root, Func<string, string> ownerRoot = null, Func<string, ICatalogStore> storeProvider = null)
    {
        this.ownerRoot = ownerRoot;
        _storeProvider = storeProvider;
        Root = Path.GetFullPath(root);
        Store = _OpenStore(Root);
        try
        {
            Manifest = Read<Manifest>(ContentFiles.CATALOG_FILE_NAME);
            if (Manifest.formatVersion is not (1 or 2)) throw new NotSupportedException($"RimUnity 格式 {Manifest.formatVersion} 不受支持：{root}");
            records = Manifest.assets.ToDictionary(a => a.id, StringComparer.Ordinal);
            foreach (AssetRecord record in Manifest.assets)
            {
                _ValidateEntry(record.file);
                if (!paths.TryAdd(record.path, record)) throw new InvalidDataException($"清单逻辑路径重复：{record.path}");
                foreach (string dependency in record.dependencies)
                {
                    if (!records.ContainsKey(dependency)) throw new InvalidDataException($"{record.path} 缺少依赖 {dependency}");
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    //所有相对路径在进入具体 ICatalogStore 前先由框架层完成越界与共享校验。
    internal ICatalogStore ResolveStore(string relative, out string normalized)
    {
        normalized = ContentFiles.NormalizeRelativePath(relative);
        if (Manifest != null && (Manifest.sharedFiles.TryGetValue(normalized, out string owner) || Manifest.sharedFiles.TryGetValue(relative, out owner)))
        {
            ContentFiles.RequireBlob(normalized);
            if (!_sharedStores.TryGetValue(owner, out ICatalogStore target))
            {
                if (ownerRoot == null) throw new InvalidDataException($"{Manifest.owner} 需要共享资源所属模组目录：{owner}");
                //只打开所属模组的存储，不构造其完整清单，模组之间互相共享时不会递归。
                target = _OpenStore(ownerRoot(owner));
                _sharedStores.Add(owner, target);
            }
            if (!_verifiedSharedEntries.Contains(normalized))
            {
                if (!target.Exists(normalized))
                    throw new FileNotFoundException($"{Manifest.owner} 缺少 {owner} 的共享资源；请安装配套版本：{normalized}", target.Describe(normalized));
                _verifiedSharedEntries.Add(normalized);
            }
            return target;
        }
        return Store;
    }

    private void _ValidateEntry(string relative)
    {
        string normalized = ContentFiles.NormalizeRelativePath(relative);
        if (Manifest.sharedFiles.ContainsKey(normalized) || Manifest.sharedFiles.ContainsKey(relative))
        {
            //扫描清单时可能尚不能解析所属模组，读取条目时再检查共享载荷是否存在。
            ContentFiles.RequireBlob(normalized);
            return;
        }
        if (Store.TryGetFilePath(normalized) == null && !Store.Exists(normalized))
            throw new FileNotFoundException($"资源归档缺少清单条目：{normalized}", Store.Describe(normalized));
    }

    //散文件模式返回磁盘路径，归档或自定义容器返回 null 以改走流式读取。
    internal string TryGetFilePath(string relative) => ResolveStore(relative, out string normalized).TryGetFilePath(normalized);

    internal string Describe(string relative) => ResolveStore(relative, out string normalized).Describe(normalized);

    internal bool Exists(string relative) => ResolveStore(relative, out string normalized).Exists(normalized);

    internal Stream OpenRead(string relative) => ResolveStore(relative, out string normalized).OpenRead(normalized);

    internal byte[] ReadAllBytes(string relative) => ResolveStore(relative, out string normalized).ReadAllBytes(normalized);

    internal string ReadAllText(string relative) => ResolveStore(relative, out string normalized).ReadAllText(normalized);

    //所有载荷均由共享格式定义解码。
    internal T Read<T>(string file)
    {
        Stream stream;
        using (Diagnostics.ParticleSpikeCapture.Measure("Asset.OpenJson", file))
            stream = ContentFiles.OpenJson(OpenRead(file), file);
        using (stream)
        using (Diagnostics.ParticleSpikeCapture.Measure("Asset.DeserializeJson", file))
            return JsonSerializer.Deserialize<T>(stream, Json) ?? throw new InvalidDataException($"空资源：{file}");
    }

    private ICatalogStore _OpenStore(string root)
    {
        if (_storeProvider != null) return _storeProvider(root);
        ICatalogStore store = CatalogStoreRegistry.Open(root);
        _ownedStores.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (ICatalogStore store in _ownedStores)
        {
            store.Dispose();
        }
        _ownedStores.Clear();
    }

    //逻辑路径不暴露源 Unity 工程的文件结构。
    internal AssetRecord Find(string path) => paths.TryGetValue(path, out var record) ? record
        : throw new FileNotFoundException($"模组 {Manifest.owner} 没有 RimUnity 资源：{path}");

    //离开存档时只回收可恢复的设备贴图，保留模板和材质的对象身份。
    internal void EvictTextures()
    {
        foreach (var resource in resources.Values)
            if (resource is Texture2D texture && !texture.isExternallyOwned) texture.CoreTexture.Evict();
            else if (resource is RenderTexture target) target.Evict();
    }

    //同一依赖只解码一次，并明确拒绝资源循环引用。
    internal RimUnity.Object Get(string id)
    {
        if (resources.TryGetValue(id, out var cached)) return cached;
        if (!records.TryGetValue(id, out var record)) throw new InvalidDataException($"未知资源 ID：{id}");
        if (!loading.Add(id)) throw new InvalidDataException($"资源依赖形成循环：{record.path}");
        try
        {
            RimUnity.Object value = record.type == "Prefab" ? PrefabBuilder.Template(this, Read<PrefabData>(record.file)) : ResourceLoader.Load(this, record);
            resources.Add(id, value);
            return value;
        }
        finally { loading.Remove(id); }
    }
}
