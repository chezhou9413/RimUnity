using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace RimUnity.Assets;

//预制体和独立资源共用同一清单缓存，避免材质与依赖重复创建。
public static partial class AssetLoader
{
    private static readonly Dictionary<string, AssetCatalog> Catalogs = new(StringComparer.OrdinalIgnoreCase);

    //场景结束后释放模板间接持有的显存，下一次使用时从磁盘恢复。
    internal static void EvictTextures()
    {
        foreach (var catalog in Catalogs.Values) catalog.EvictTextures();
    }

    //列出本模组指定资源类型的公开路径，供内容侧建立明确且无歧义的索引。
    public static string[] GetPaths(ModContentPack mod, string resourceType)
        => GetCatalog(CatalogLoadPolicy.RequireRoot(mod)).Manifest.assets
            .Where(a => a.type == resourceType && !a.path.StartsWith("__assets/", StringComparison.Ordinal))
            .Select(a => a.path).ToArray();

    //按导出逻辑路径加载材质、贴图、动画或预制体模板。
    public static T Load<T>(ModContentPack mod, string path) where T : RimUnity.Object
        => LoadDirectory<T>(CatalogLoadPolicy.RequireRoot(mod), path);
    //短期图片缓存独立持有纹理，调用者释放时不会销毁预制体共享的资源。
    public static Texture2D LoadOwnedTexture(ModContentPack mod, string path)
    {
        var catalog = GetCatalog(CatalogLoadPolicy.RequireRoot(mod));
        var record = catalog.Find(path);
        if (record.type != "Texture2D") throw new InvalidDataException("独立贴图入口需要 Texture2D：" + path);
        return (Texture2D)ResourceLoader.Load(catalog, record);
    }

    //AB 入口保留包名与 Unity 工程路径，内容仍由明文清单加载。
    public static T LoadBundleAsset<T>(ModContentPack mod, string bundle, string assetPath) where T : RimUnity.Object
        => Load<T>(mod, "Bundles/" + bundle + "/" + assetPath.Replace('\\', '/'));

    //工具使用明确的内容目录，不跨模组搜索同名资源。
    public static T LoadDirectory<T>(string dataDirectory, string path) where T : RimUnity.Object
    {
        AssetCatalog catalog = GetCatalog(dataDirectory);
        var record = catalog.Find(path);
        RimUnity.Object value = catalog.Get(record.id);
        if (value is not T typed) throw new InvalidDataException($"{path} 是 {value.GetType().FullName}，不能作为 {typeof(T).FullName} 加载。");
        return typed;
    }

    //各加载入口共用进程生命周期的只读清单与资源模板。
    internal static AssetCatalog GetCatalog(string dataDirectory)
    {
        string root = Path.GetFullPath(dataDirectory);
        if (!Catalogs.TryGetValue(root, out AssetCatalog catalog)) Catalogs.Add(root, catalog = new AssetCatalog(root, ResolveOwner));
        return catalog;
    }

    //仅从已启用模组解析 packageId，缺失依赖直接给出可定位错误。
    private static string ResolveOwner(string owner)
    {
        var mod = LoadedModManager.RunningModsListForReading.SingleOrDefault(m =>
            string.Equals(m.PackageIdPlayerFacing, owner, StringComparison.OrdinalIgnoreCase));
        if (mod == null) throw new FileNotFoundException("RimUnity 共享资源依赖未启用：" + owner);
        return CatalogLoadPolicy.RequireRoot(mod);
    }
}
