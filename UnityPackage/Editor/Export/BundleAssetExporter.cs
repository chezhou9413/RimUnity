using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //以 Unity 实际 AB 入口为边界逐项导出，失败入口保留明确原因。
    public static class BundleAssetExporter
    {
        //每项复用现有事务，失败时不会发布不完整的资源依赖。
        public static void Export(string modDirectory, string reportFile, string[] bundleNames = null, string[] selectedAssets = null)
        {
            var results = new List<BundleAssetResult>();
            string[] names = bundleNames == null || bundleNames.Length == 0 ? AssetDatabase.GetAllAssetBundleNames() : bundleNames;
            var selected = selectedAssets == null ? null : new HashSet<string>(selectedAssets, StringComparer.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile)));
            foreach (string bundle in names.OrderBy(n => n, StringComparer.Ordinal))
            {
                string[] paths = AssetDatabase.GetAssetPathsFromAssetBundle(bundle).Where(p => !AssetDatabase.IsValidFolder(p)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                if (paths.Length == 0) throw new ArgumentException("AB 没有资源入口：" + bundle);
                foreach (string path in paths)
                {
                    if (selected != null && !selected.Contains(path)) continue;
                    var result = new BundleAssetResult { bundle = bundle, path = path };
                    try
                    {
                        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                        if (asset == null) throw new InvalidDataException("Unity 未能导入此资源。");
                        result.type = asset.GetType().FullName;
                        if (asset is MonoScript)
                        {
                            result.status = "script-requires-adapted-assembly";
                            result.error = "脚本引用不包含可执行程序集；需将对应逻辑适配并编译进模组。";
                        }
                        else
                        {
                            PrefabExporter.Export(modDirectory, new ExportRequest { asset = asset,
                                path = "Bundles/" + bundle + "/" + path, description = "AB：" + bundle + "\n源资源：" + path,
                                ui = asset is Material material && (material.shader.name.StartsWith("UI/", StringComparison.Ordinal)
                                    || string.Equals(material.GetTag("CanUseSpriteAtlas", false, ""), "True", StringComparison.OrdinalIgnoreCase)) });
                            result.status = "exported";
                            if (asset is GameObject prefab && prefab.GetComponentsInChildren<Component>(true).Any(c => c == null))
                                result.status = "exported-with-missing-script";
                        }
                    }
                    catch (Exception error)
                    {
                        result.status = "failed";
                        result.error = error.ToString();
                        Debug.LogError("[RimUnity] AB 入口导出失败：" + bundle + "/" + path + "\n" + error.Message);
                    }
                    results.Add(result);
                    File.WriteAllText(reportFile, JsonConvert.SerializeObject(results, Formatting.Indented), new System.Text.UTF8Encoding(false));
                }
            }
            Debug.Log("[RimUnity] AB 入口导出结束：" + results.Count(r => r.status == "exported") + "/" + results.Count + " 项完整导出；详情：" + reportFile);
        }

        //选择已有 Mod 目录即可导出项目中所有打包入口。
        [MenuItem("Tools/RimUnity/导出所有 AB 入口")]
        public static void ExportAll()
        {
            string mod = EditorUtility.OpenFolderPanel("选择目标 Mod（需包含 About/About.xml）", "", "");
            if (string.IsNullOrEmpty(mod)) return;
            Export(mod, Path.Combine(mod, "RimUnity-bundle-export-report.json"));
        }

        //批处理显式指定工具链，结束后恢复用户的编辑器配置。
        public static void Batch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Argument(string name)
            {
                int index = Array.IndexOf(args, name);
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("缺少参数：" + name);
                return args[index + 1];
            }
            string oldTool = EditorPrefs.GetString("RimUnity.ShaderTool", ""), oldGame = EditorPrefs.GetString("RimUnity.GameRoot", "");
            try
            {
                EditorPrefs.SetString("RimUnity.ShaderTool", Argument("-rimUnityShaderTool"));
                EditorPrefs.SetString("RimUnity.GameRoot", Argument("-rimUnityGame"));
                string[] bundles = args.Contains("-rimUnityBundleNames") ? Argument("-rimUnityBundleNames").Split(',') : null;
                string[] selected = args.Contains("-rimUnityAssetList") ? JsonConvert.DeserializeObject<string[]>(File.ReadAllText(Argument("-rimUnityAssetList"))) : null;
                Export(Argument("-rimUnityMod"), Argument("-rimUnityReport"), bundles, selected);
            }
            finally { EditorPrefs.SetString("RimUnity.ShaderTool", oldTool); EditorPrefs.SetString("RimUnity.GameRoot", oldGame); }
        }
    }

    //导出状态不代表游戏播放或 Shader 画面已经验收。
    [Serializable]
    public sealed class BundleAssetResult
    {
        public string bundle, path, type, status, error;
    }
}
