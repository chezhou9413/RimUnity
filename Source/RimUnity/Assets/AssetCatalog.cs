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
    private readonly Dictionary<string, RimUnity.Object> resources = new();
    private readonly HashSet<string> loading = new();

    //清单在首次加载时校验，失败直接报告内容模组及资源路径。
    internal AssetCatalog(string root)
    {
        Root = Path.GetFullPath(root);
        Manifest = Read<Manifest>("catalog.json");
        if (Manifest.formatVersion != 1) throw new NotSupportedException($"RimUnity 格式 {Manifest.formatVersion} 不受支持：{root}");
        records = Manifest.assets.ToDictionary(a => a.id, StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetRecord record in Manifest.assets)
        {
            Resolve(record.file);
            if (!paths.Add(record.path)) throw new InvalidDataException($"清单逻辑路径重复：{record.path}");
            foreach (string dependency in record.dependencies)
                if (!records.ContainsKey(dependency)) throw new InvalidDataException($"{record.path} 缺少依赖 {dependency}");
        }
    }

    //禁止资源清单跳出内容目录，也不允许绝对路径。
    internal string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException($"资源必须使用相对路径：{relative}");
        string full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"资源路径超出目录：{relative}");
        return full;
    }

    //所有载荷均由共享格式定义解码。
    internal T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(Resolve(file)), Json)
        ?? throw new InvalidDataException($"空资源：{file}");

    //逻辑路径不暴露源 Unity 工程的文件结构。
    internal AssetRecord Find(string path) => Manifest.assets.SingleOrDefault(a => string.Equals(a.path, path, StringComparison.OrdinalIgnoreCase))
        ?? throw new FileNotFoundException($"模组 {Manifest.owner} 没有 RimUnity 资源：{path}");

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
