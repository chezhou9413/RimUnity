using System;
using System.IO;
using HarmonyLib;
using Verse;

namespace RimUnity.Assets;

//原版只统计内置资源目录；有效的桥接清单也属于非翻译模组内容。
[HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.AnyNonTranslationContentLoaded))]
internal static class CatalogContentPatch
{
    //只识别通过格式和依赖检查的非空清单，不掩盖其他空模组或损坏清单。
    private static void Postfix(ModContentPack __instance, ref bool __result)
    {
        if (__result) return;
        if (!CatalogLoadPolicy.HasLoadFolders(__instance)) return;
        string root = Path.Combine(__instance.RootDir, "1.7", "Data", "RimUnity");
        if (!File.Exists(Path.Combine(root, "catalog.json"))) return;
        try { __result = new AssetCatalog(root).Manifest.assets.Count > 0; }
        catch (Exception exception)
        {
            Log.Error($"[RimUnity] 模组 {__instance.PackageId} 的资源清单无效：{root}\n{exception}");
        }
    }
}
