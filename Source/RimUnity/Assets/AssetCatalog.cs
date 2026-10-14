using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//每个内容模组独立持有资源缓存；实例组件不进入共享缓存。
internal sealed class AssetCatalog
{
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    internal readonly string Root;
    internal readonly Manifest Manifest;
    private readonly Dictionary<string, AssetRecord> records;
    private readonly Dictionary<string, AssetRecord> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RimUnity.Object> resources = new();
    private readonly HashSet<string> loading = new();
    private readonly Func<string, string> ownerRoot;
    private readonly Dictionary<string, string> sharedPaths = new(StringComparer.Ordinal);

    //清单在首次加载时校验，失败直接报告内容模组及资源路径。
    internal AssetCatalog(string root, Func<string, string> ownerRoot = null)
    {
        this.ownerRoot = ownerRoot;
        Root = Path.GetFullPath(root);
        Manifest = Read<Manifest>("catalog.json");
        if (Manifest.formatVersion is not (1 or 2)) throw new NotSupportedException($"RimUnity 格式 {Manifest.formatVersion} 不受支持：{root}");
        records = Manifest.assets.ToDictionary(a => a.id, StringComparer.Ordinal);
        foreach (AssetRecord record in Manifest.assets)
        {
            Resolve(record.file);
            if (!paths.TryAdd(record.path, record)) throw new InvalidDataException($"清单逻辑路径重复：{record.path}");
            foreach (string dependency in record.dependencies)
                if (!records.ContainsKey(dependency)) throw new InvalidDataException($"{record.path} 缺少依赖 {dependency}");
        }
    }

    //禁止资源清单跳出内容目录，也不允许绝对路径。
    internal string Resolve(string relative)
    {
        if (Manifest != null && Manifest.sharedFiles.TryGetValue(relative, out string owner))
        {
            if (sharedPaths.TryGetValue(relative, out string cached)) return cached;
            ContentFiles.RequireBlob(relative);
            if (ownerRoot == null) throw new InvalidDataException($"{Manifest.owner} 需要共享资源所属模组目录：{owner}");
            string full = ContentFiles.Inside(ownerRoot(owner), relative);
            if (!File.Exists(full)) throw new FileNotFoundException($"{Manifest.owner} 缺少 {owner} 的共享资源；请安装配套版本：{relative}", full);
            sharedPaths.Add(relative, full);
            return full;
        }
        return ContentFiles.Inside(Root, relative);
    }

    //所有载荷均由共享格式定义解码。
    internal T Read<T>(string file)
    {
        using var stream = ContentFiles.OpenJson(Resolve(file));
        return JsonSerializer.Deserialize<T>(stream, Json) ?? throw new InvalidDataException($"空资源：{file}");
    }

    //逻辑路径不暴露源 Unity 工程的文件结构。
    internal AssetRecord Find(string path) => paths.TryGetValue(path, out var record) ? record
        : throw new FileNotFoundException($"模组 {Manifest.owner} 没有 RimUnity 资源：{path}");

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
