using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimUnity.Editor;
using UnityEditor;
using UnityEngine;

//读取工程配置并收集待导出的资源入口。
namespace RimUnity.Editor { public static class RimUnityWorkflow
{
    public const string ProfilePath = "ProjectSettings/RimUnity.json";
    public const string WindowPresetPath = "ProjectSettings/RimUnityWindow.json";
    [Serializable] public sealed class Profile
    {
        public string repository, shaderTool, gameRoot;
        public bool auditOnReload, disableCastShadows, disableReceiveShadows, disableLightProbes, disableReflectionProbes;
        public Mapping[] mappings;
    }
    [Serializable] public sealed class Mapping { public string bundle, mod; }
    [Serializable] public sealed class Entry
    {
        public string path, bundle, mod, type, status, error;
        public bool ui, catalogued;
        public int missingScripts;
        public string[] components, shaders;
        public int[] layers;
    }
    public static Profile Load()
    {
        if (!File.Exists(ProfilePath)) return new Profile { repository = Path.GetFullPath("."), shaderTool = EditorPrefs.GetString("RimUnity.ShaderTool", ""), gameRoot = EditorPrefs.GetString("RimUnity.GameRoot", ""), mappings = Array.Empty<Mapping>() };
        var profile = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(ProfilePath));
        if (profile == null || profile.mappings == null || !Directory.Exists(profile.repository))
            throw new InvalidOperationException("RimUnity 工程配置无效：" + ProfilePath);
        return profile;
    }
    public static List<Entry> Discover(Profile profile)
    {
        var catalogs = new Dictionary<string, HashSet<string>>();
        foreach (var mapping in profile.mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.mod)) continue;
            string catalog = Path.Combine(profile.repository, mapping.mod, "1.7/Data/RimUnity/catalog.json");
            if (!catalogs.ContainsKey(mapping.mod)) catalogs[mapping.mod] = File.Exists(catalog)
                ? new HashSet<string>(((JArray)JObject.Parse(File.ReadAllText(catalog))["assets"]).Select(a => (string)a["path"]))
                : new HashSet<string>();
        }
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in profile.mappings)
            if (!string.IsNullOrWhiteSpace(mapping.bundle))
            foreach (string path in AssetDatabase.GetAssetPathsFromAssetBundle(mapping.bundle))
                if (!AssetDatabase.IsValidFolder(path)) selected[path] = mapping.bundle;
        //以资源包标签及手动选项作为入口，依赖由导出器收集。
        var preset = File.Exists(WindowPresetPath) ? JObject.Parse(File.ReadAllText(WindowPresetPath)) : new JObject();
        foreach (JObject item in preset["manual"] as JArray ?? new JArray())
            if (!string.IsNullOrWhiteSpace((string)item["path"])) selected[(string)item["path"]] = (string)item["bundle"];
        var uiPaths = new HashSet<string>((preset["uiPaths"] as JArray ?? new JArray()).Values<string>(), StringComparer.OrdinalIgnoreCase);
        var entries = new List<Entry>();
        foreach (var pair in selected.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var mapping = profile.mappings.FirstOrDefault(m => m.bundle == pair.Value);
            var entry = new Entry { path = pair.Key, bundle = pair.Value, mod = mapping?.mod,
                ui = uiPaths.Contains(pair.Key), status = "pending" };
            var asset = AssetDatabase.LoadMainAssetAtPath(pair.Key);
            entry.type = asset?.GetType().FullName;
            if (asset == null) { entry.status = "missing-asset"; entry.error = "Unity 未能载入源资源"; }
            else if (mapping == null || string.IsNullOrWhiteSpace(mapping.mod)) { entry.status = "unassigned-owner"; entry.error = "资源包尚未配置导出位置"; }
            else if (asset is MonoScript) { entry.status = "script-requires-adaptation"; entry.error = "脚本需在游戏侧适配和编译"; }
            if (pair.Key.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
            { entry.status = "unsupported-shadergraph"; entry.error = "RimUnity 尚未实现 Shader Graph 转换"; }
            if (asset is GameObject prefab)
            {
                var components = prefab.GetComponentsInChildren<Component>(true);
                entry.missingScripts = components.Count(c => c == null);
                entry.components = components.Where(c => c != null).Select(c => c.GetType().FullName).Distinct().OrderBy(n => n).ToArray();
                entry.layers = components.OfType<Transform>().Select(t => t.gameObject.layer).Distinct().OrderBy(n => n).ToArray();
                entry.shaders = components.OfType<Renderer>().SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null).Select(m => m.shader == null ? "<missing>" : m.shader.name).Distinct().OrderBy(n => n).ToArray();
            }
            else if (asset is Material material) entry.shaders = new[] { material.shader == null ? "<missing>" : material.shader.name };
            if (mapping != null && catalogs.TryGetValue(mapping.mod ?? "", out var paths))
            {
                entry.catalogued = paths.Contains("Bundles/" + mapping.bundle + "/" + pair.Key);
            }
            entries.Add(entry);
        }
        return entries;
    }
    private static string Save(Profile profile, List<Entry> entries, string name)
    {
        string directory = Path.GetFullPath("Library/RimUnity/Reports");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".json");
        File.WriteAllText(path, JsonConvert.SerializeObject(new { generatedUtc = DateTime.UtcNow,
            unity = Application.unityVersion, project = Path.GetFullPath("."), entries }, Formatting.Indented));
        return path;
    }
    [MenuItem("RimUnity/检查全部特效资源")]
    public static void Audit()
    {
        var profile = Load();
        var entries = Discover(profile);
        string report = Save(profile, entries, "inventory");
        Debug.Log($"[RimUnity] {entries.Count} 个入口，{entries.Count(e => !e.catalogued)} 个尚未对应当前清单，" +
            $"{entries.Count(e => e.missingScripts > 0)} 个含缺失脚本。检查报告：{report}。清单存在不代表游戏验收通过。");
    }
    [InitializeOnLoadMethod]
    private static void AuditAfterInstall()
    {
        if (!File.Exists(ProfilePath) || !Load().auditOnReload) return;
        const string key = "RimUnity.InitialAudit";
        if (SessionState.GetBool(key, false)) return;
        SessionState.SetBool(key, true);
        EditorApplication.delayCall += () => { try { Audit(); } catch (Exception error) { Debug.LogException(error); } };
    }
    [MenuItem("RimUnity/导出全部 1.7 特效资源")]
    public static void ExportAll()
    {
        var profile = Load();
        if (!File.Exists(profile.shaderTool) || !File.Exists(Path.Combine(profile.gameRoot, "Waylith.dll")))
            throw new InvalidOperationException("请先配置有效的 RimUnity ShaderTool 和 RimWorld 1.7 路径。");
        var entries = Discover(profile);
        try
        {
            RimUnityPublisher.Export(profile, entries, true, Debug.Log);
            foreach (var entry in entries) entry.status = "exported";
        }
        finally
        {
            Debug.Log("[RimUnity] 导出报告：" + Save(profile, entries, "export"));
        }
        int failures = entries.Count(e => e.status != "exported" && e.status != "excluded-by-profile");
        if (failures != 0) throw new InvalidOperationException($"{failures} 个入口未成功导出；已保留原成功清单与具体失败原因。");
    }
}

}
