using System;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace RimUnity.Assets;

//遵循宿主已经解析的加载目录；不缓存依赖启用状态的判断。
internal static class CatalogLoadPolicy
{
    internal static bool HasLoadFolders(ModContentPack mod)
    {
        if (mod.foldersToLoadDescendingOrder is not { Count: > 0 }) return false;
        //翻译占位目录不应重新启用被条件排除的 1.7 资源；同名条目按 OR 合并。
        var metadata = ModLister.GetModWithIdentifier(mod.PackageId);
        //游戏版本包含构建号（如 1.7.1234），常规 LoadFolders 使用主次版本键。
        var folders = metadata?.LoadFoldersForVersion(VersionControl.CurrentVersionString)
            ?? metadata?.LoadFoldersForVersion(VersionControl.CurrentVersionStringWithoutBuild);
        var versionFolders = folders?.Where(folder => folder.folderName?.TrimEnd('/', '\\') == "1.7").ToArray();
        return versionFolders == null || versionFolders.Length == 0 || versionFolders.Any(folder => folder.ShouldLoad);
    }

    //仅认可当前版本明确声明、且全部被条件排除的目录，保留真正空模组的诊断。
    internal static bool IsConditionallyDisabled(ModContentPack mod)
    {
        if (HasLoadFolders(mod)) return false;
        var metadata = ModLister.GetModWithIdentifier(mod.PackageId);
        //游戏版本包含构建号（如 1.7.1234），常规 LoadFolders 使用主次版本键。
        var folders = metadata?.LoadFoldersForVersion(VersionControl.CurrentVersionString)
            ?? metadata?.LoadFoldersForVersion(VersionControl.CurrentVersionStringWithoutBuild);
        return folders is { Count: > 0 } && folders.All(folder => !folder.ShouldLoad);
    }

    internal static string RequireRoot(ModContentPack mod)
    {
        if (!HasLoadFolders(mod))
            throw new InvalidOperationException($"RimUnity 模组 {mod.PackageIdPlayerFacing} 的加载目录未启用，不能加载资源清单。");
        return Path.Combine(mod.RootDir, "1.7", "Data", "RimUnity");
    }
}
