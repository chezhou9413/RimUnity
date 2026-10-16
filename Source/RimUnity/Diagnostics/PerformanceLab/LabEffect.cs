using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimUnity.Assets;
using RimUnity.Serialization;
using Verse;

namespace RimUnity.Diagnostics;

//索引只保存路径和描述，选择之前不实例化特效或加载贴图。
internal sealed class LabEffect
{
    internal ModContentPack Mod;
    internal string Path;
    internal int Systems;
    internal string[] Warnings = Array.Empty<string>();
    internal bool Selected = true;
    private string key;
    internal string Key => key ??= Mod.PackageIdPlayerFacing + "/" + Path;
    internal string Label => Mod.Name + " / " + Path;

    //只收录导出清单中确实包含粒子系统的预制体。
    internal static List<LabEffect> Discover()
    {
        var result = new List<LabEffect>();
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            string root = System.IO.Path.Combine(mod.RootDir, "1.7", "Data", "RimUnity");
            if (!File.Exists(System.IO.Path.Combine(root, "catalog.json"))) continue;
            AssetCatalog catalog;
            try { catalog = AssetLoader.GetCatalog(root); }
            catch (Exception ex) { Log.Error($"[RimUnity] 测试场无法读取 {mod.PackageIdPlayerFacing}：{ex}"); continue; }
            foreach (var asset in catalog.Manifest.assets)
            {
                if (asset.type != "Prefab" || asset.path.StartsWith("__assets/", StringComparison.Ordinal)) continue;
                try
                {
                    var data = catalog.Read<PrefabData>(asset.file);
                    PrefabStructure.Validate(data);
                    int count = data.nodes.Sum(n => n.components.Count(c => c.type == typeof(ParticleSystem).FullName));
                    if (count > 0) result.Add(new LabEffect { Mod = mod, Path = asset.path, Systems = count, Warnings = data.warnings.ToArray() });
                }
                catch (Exception ex) { Log.Error($"[RimUnity] 测试场索引失败 {mod.PackageIdPlayerFacing}/{asset.path}：{ex}"); }
            }
        }
        return result.OrderBy(e => e.Mod.Name).ThenBy(e => e.Path).ToList();
    }
}
