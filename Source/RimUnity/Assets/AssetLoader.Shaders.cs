using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using RimUnity.Serialization;
using Verse;

namespace RimUnity.Assets;

//把同步 Shader 编译放到加载界面，避免首次发射粒子时才编译变体。
public static partial class AssetLoader
{
    //只准备程序与材质引用的颜色变体，贴图和预制体继续按需加载。
    internal static IEnumerable PrepareShaders()
    {
        var watch = Stopwatch.StartNew();
        long nextYield = 8;
        int prepared = 0, failures = 0;
        foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
        {
            string root = Path.Combine(mod.RootDir, "1.7", "Data", "RimUnity");
            if (!File.Exists(Path.Combine(root, "catalog.json"))) continue;
            AssetCatalog catalog;
            try { catalog = GetCatalog(root); }
            catch (Exception ex)
            {
                failures++;
                Log.Error($"[RimUnity] 准备 Shader 时无法读取 {mod.PackageIdPlayerFacing} 的清单：{ex}");
                continue;
            }
            foreach (AssetRecord record in catalog.Manifest.assets)
            {
                if (record.type is not ("Shader" or "Material")) continue;
                try
                {
                    if (record.type == "Shader") catalog.Get(record.id);
                    else
                    {
                        var data = catalog.Read<ResourceData>(record.file);
                        if (data.formatVersion != 1 || data.type != "Material")
                            throw new InvalidDataException("材质格式不匹配：" + record.path);
                        ResourceLoader.ResolveMaterialShader(catalog, data);
                    }
                    prepared++;
                }
                catch (Exception ex)
                {
                    failures++;
                    Log.Error($"[RimUnity] Shader 准备失败：{mod.PackageIdPlayerFacing}/{record.path}：{ex}");
                }
                if (watch.ElapsedMilliseconds < nextYield) continue;
                yield return null;
                nextYield = watch.ElapsedMilliseconds + 8;
            }
        }
        Log.Message($"[RimUnity] Shader 准备结束：{prepared} 项，失败 {failures} 项，用时 {watch.Elapsed.TotalSeconds:F2} 秒。");
    }
}
