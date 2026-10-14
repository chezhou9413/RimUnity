using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//为原有 AB 调用保留包名和资源查找习惯，载荷仅来自 RimUnity 明文清单。
public sealed class AssetBundle
{
    private readonly AssetCatalog catalog;
    private readonly AssetRecord[] entries;
    public string name { get; }

    //包视图共享进程级资源模板，地图实例仍由调用者创建和释放。
    private AssetBundle(AssetCatalog catalog, string name, AssetRecord[] entries)
    {
        this.catalog = catalog;
        this.name = name;
        this.entries = entries;
    }

    //一个内容模组的清单可以同时包含多个原始 AB 包。
    public static IReadOnlyList<AssetBundle> LoadDirectory(string dataDirectory)
    {
        AssetCatalog catalog = AssetLoader.GetCatalog(dataDirectory);
        return catalog.Manifest.assets.Where(a => a.path.StartsWith("Bundles/", StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => a.path.Split('/')[1], StringComparer.OrdinalIgnoreCase)
            .Select(g => new AssetBundle(catalog, g.Key, g.ToArray())).ToArray();
    }

    //优先匹配工程路径；短名重名时明确报错，避免随机选择外观不同的资源。
    public T LoadAsset<T>(string assetName) where T : RimUnity.Object
    {
        string path = assetName.Replace('\\', '/');
        string prefix = "Bundles/" + name + "/";
        var candidates = entries.Where(a => MatchesType<T>(a.type)).ToArray();
        var exact = candidates.Where(a => string.Equals(a.path.Substring(prefix.Length), path, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 0)
            exact = candidates.Where(a => string.Equals(a.name, path, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(a.path), path, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(a.path), path, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 0) return null;
        if (exact.Length > 1) throw new InvalidDataException($"RimUnity 包 {name} 中资源名称不唯一：{assetName}；请使用完整工程路径。");
        return (T)catalog.Get(exact[0].id);
    }

    //只筛选声明类型，查询不会实例化无关预制体。
    private static bool MatchesType<T>(string type) => typeof(T) == typeof(RimUnity.Object)
        || type == (typeof(T) == typeof(GameObject) ? "Prefab" : typeof(T).Name);
}
