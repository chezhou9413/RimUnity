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
        private ResourceOwnership.Duplicate[] duplicates = Array.Empty<ResourceOwnership.Duplicate>();
        private readonly Dictionary<string, string> migrationTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool showDuplicates;
        private string batchBundle = "";
        private readonly HashSet<string> migrationSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void RefreshDuplicates()
        {
            duplicates = ResourceOwnership.Scan(entries);
            migrationSelection.IntersectWith(duplicates.Select(d => d.path));
            var assigned = new HashSet<string>((profile.resourceOwners ?? Array.Empty<RimUnityWorkflow.ResourceOwner>()).Select(p => p.path), StringComparer.OrdinalIgnoreCase);
            int unresolved = duplicates.Count(d => !assigned.Contains(d.path));
            if (unresolved > 0) Debug.LogWarning("[RimUnity] " + unresolved + " 个跨 AB 包重复引用的资源尚未指定归属。可在构建窗口的重复资源区迁移。");
        }
        private bool MatchesBundle(string bundle) => string.IsNullOrEmpty(bundleFilter) || bundle == bundleFilter;
        private void DrawBundleFilter()
        {
            var bundles = profile.mappings.Select(m => m.bundle).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().OrderBy(b => b).ToArray();
            var options = new[] { "全部 AB 包" }.Concat(bundles).ToArray();
            int index = Array.IndexOf(bundles, bundleFilter) + 1;
            int chosen = EditorGUILayout.Popup("AB 包筛选", Math.Max(0, index), options);
            bundleFilter = chosen == 0 ? "" : bundles[chosen - 1];
            showDuplicates = EditorGUILayout.Foldout(showDuplicates, "重复引用资源 · " + duplicates.Length + " 项", true);
        }
        private void DrawDuplicates()
        {
            if (!showDuplicates) return;
            var mappings = profile.mappings.Where(m => !string.IsNullOrWhiteSpace(m.bundle) && !string.IsNullOrWhiteSpace(m.mod))
                .GroupBy(m => m.bundle).Where(g => g.Count() == 1).Select(g => g.Single()).OrderBy(m => m.bundle).ToArray();
            string[] labels = mappings.Select(m => m.bundle + " → " + m.mod).ToArray();
            var visible = duplicates.Where(d => (string.IsNullOrEmpty(bundleFilter) || d.bundles.Contains(bundleFilter)) &&
                (string.IsNullOrWhiteSpace(search) || (d.path + " " + string.Join(" ", d.bundles)).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全选当前筛选", GUILayout.Width(110))) migrationSelection.UnionWith(visible.Select(d => d.path));
            if (GUILayout.Button("清空选择", GUILayout.Width(85))) migrationSelection.Clear();
            GUILayout.Label("已选 " + migrationSelection.Count + " 项（含筛选外选择）", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
            if (mappings.Length > 0)
            {
                int target = Math.Max(0, Array.FindIndex(mappings, m => m.bundle == batchBundle));
                target = EditorGUILayout.Popup("批量迁移目标", target, labels);
                batchBundle = mappings[target].bundle;
                using (new EditorGUI.DisabledScope(migrationSelection.Count == 0))
                    if (GUILayout.Button("将勾选的 " + migrationSelection.Count + " 项迁移到 " + batchBundle))
                    {
                        try { MigrateResources(migrationSelection.ToArray(), mappings[target]); migrationSelection.Clear(); }
                        catch (Exception exception) { Log(exception.ToString()); Debug.LogException(exception); }
                        GUIUtility.ExitGUI();
                    }
            }
            foreach (var duplicate in visible)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                bool chosen = GUILayout.Toggle(migrationSelection.Contains(duplicate.path), "加入批量迁移");
                if (chosen) migrationSelection.Add(duplicate.path); else migrationSelection.Remove(duplicate.path);
                if (GUILayout.Button(new GUIContent(duplicate.path, duplicate.path), EditorStyles.label))
                    Ping(new RimUnityWorkflow.Entry { path = duplicate.path });
                GUILayout.Label("引用包：" + string.Join("、", duplicate.bundles), EditorStyles.wordWrappedMiniLabel);
                var pin = (profile.resourceOwners ?? Array.Empty<RimUnityWorkflow.ResourceOwner>()).FirstOrDefault(p => p.path == duplicate.path);
                if (pin != null) GUILayout.Label("已指定内容归属：" + pin.bundle + "（其他包仍保留加载引用）", EditorStyles.miniLabel);
                GUILayout.BeginHorizontal();
                if (mappings.Length > 0)
                {
                    if (!migrationTargets.TryGetValue(duplicate.path, out string selected)) selected = pin?.bundle ?? duplicate.bundles[0];
                    int index = Math.Max(0, Array.FindIndex(mappings, m => m.bundle == selected));
                    index = EditorGUILayout.Popup(index, labels);
                    migrationTargets[duplicate.path] = mappings[index].bundle;
                    if (GUILayout.Button("迁移归属", GUILayout.Width(85)))
                    {
                        try { MigrateResource(duplicate.path, mappings[index]); }
                        catch (Exception exception) { Log(exception.ToString()); Debug.LogException(exception); }
                        GUIUtility.ExitGUI();
                    }
                    if (pin != null && GUILayout.Button("取消指定", GUILayout.Width(75)))
                    {
                        profile.resourceOwners = profile.resourceOwners.Where(p => p.path != duplicate.path).ToArray();
                        SaveOwnership();
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.Space(8);
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