using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    public sealed partial class RimUnityBuildWindow
    {
        private string bundleFilter = "";
        private string batchBundle = "";

        private void RefreshDuplicates()
        {
            var duplicates = ResourceOwnership.Scan(entries);
            var assigned = new HashSet<string>((profile.resourceOwners ?? Array.Empty<RimUnityWorkflow.ResourceOwner>()).Select(p => p.path), StringComparer.OrdinalIgnoreCase);
            int unresolved = duplicates.Count(d => !assigned.Contains(d.path));
            if (unresolved > 0) Debug.LogWarning("[RimUnity] " + unresolved + " 个跨 AB 包重复引用的资源尚未指定归属。可在资源清点页选择资源，拖到左侧 AB 包指定归属。");
        }
        private void SaveOwnership()
        {
            File.WriteAllText(RimUnityWorkflow.ProfilePath, JsonConvert.SerializeObject(profile, Formatting.Indented));
        }
        private void MigrateResource(string path, RimUnityWorkflow.Mapping mapping)
            => MigrateResources(new[] { path }, mapping);

        private void MigrateResources(IEnumerable<string> paths, RimUnityWorkflow.Mapping mapping)
        {
            var selected = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (mapping == null || string.IsNullOrWhiteSpace(mapping.bundle)
                || profile.mappings.Count(m => m.bundle == mapping.bundle) != 1)
                throw new InvalidOperationException("迁移目标必须对应唯一的 AB 输出映射。");
            if (selected.Any(p => !p.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new InvalidOperationException("只能修改 Assets 下工程资源的归属；Packages 资源保持只读。");
            //先核对整批资源，再改标签；保存与依赖扫描各执行一次。
            var importers = selected.Select(path => {
                if (AssetDatabase.LoadMainAssetAtPath(path) == null) throw new InvalidOperationException("源资源不存在：" + path);
                var importer = AssetImporter.GetAtPath(path);
                if (importer == null) throw new InvalidOperationException("源资源没有导入器：" + path);
                return (path, importer, bundle: importer.assetBundleName, variant: importer.assetBundleVariant);
            }).ToArray();
            var oldOwners = profile.resourceOwners;
            var oldManual = manual.ToArray();
            string oldProfile = File.Exists(RimUnityWorkflow.ProfilePath) ? File.ReadAllText(RimUnityWorkflow.ProfilePath) : null;
            string oldPreset = File.Exists(PresetPath) ? File.ReadAllText(PresetPath) : null;
            int changed = 0;
            try
            {
                var owners = (oldOwners ?? Array.Empty<RimUnityWorkflow.ResourceOwner>()).Where(p => !selected.Contains(p.path, StringComparer.OrdinalIgnoreCase)).ToList();
                foreach (var item in importers)
                {
                    changed++;
                    item.importer.SetAssetBundleNameAndVariant(mapping.bundle, "");
                    owners.Add(new RimUnityWorkflow.ResourceOwner { path = item.path, bundle = mapping.bundle });
                    manual.RemoveAll(e => string.Equals(e.path, item.path, StringComparison.OrdinalIgnoreCase));
                    manual.Add(new RimUnityWorkflow.Entry { path = item.path, bundle = mapping.bundle, mod = mapping.mod, status = "pending",
                        ui = entries.FirstOrDefault(e => string.Equals(e.path, item.path, StringComparison.OrdinalIgnoreCase))?.ui ?? false });
                }
                profile.resourceOwners = owners.ToArray();
                SaveOwnership(); SavePreset(); RefreshEntries();
                Log("已迁移 " + selected.Length + " 项 AB 归属 → " + mapping.bundle + "；完整发布后内容只保存在目标模组。依赖声明由使用者维护。");
            }
            catch
            {
                foreach (var item in importers.Take(changed)) item.importer.SetAssetBundleNameAndVariant(item.bundle, item.variant);
                profile.resourceOwners = oldOwners;
                manual.Clear(); manual.AddRange(oldManual);
                if (oldProfile != null) File.WriteAllText(RimUnityWorkflow.ProfilePath, oldProfile); else if (File.Exists(RimUnityWorkflow.ProfilePath)) File.Delete(RimUnityWorkflow.ProfilePath);
                if (oldPreset != null) File.WriteAllText(PresetPath, oldPreset); else if (File.Exists(PresetPath)) File.Delete(PresetPath);
                RefreshEntries();
                throw;
            }
        }
    }
}
