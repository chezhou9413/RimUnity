using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using RimUnity.Serialization;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //批量导出的公开入口；catalog 是唯一提交点，失败不会替换上次成功的资源。
    public static class PrefabExporter
    {
        //一次导出可包含多个预制体，并共享依赖去重。
        public static void Export(string modDirectory, params ExportRequest[] requests) => Export(modDirectory, new ExportOptions(), requests);

        public static void Export(string modDirectory, ExportOptions options, params ExportRequest[] requests)
        {
            if (requests.Length == 0) throw new ArgumentException("至少选择一个预制体。");
            string about = Path.Combine(modDirectory, "About", "About.xml");
            string owner = XDocument.Load(about).Root.Element("packageId")?.Value;
            if (string.IsNullOrWhiteSpace(owner)) throw new InvalidDataException("目标目录必须含有效 About/About.xml 和 packageId。");
            string root = Path.Combine(modDirectory, "1.7", "Data", "RimUnity");
            Directory.CreateDirectory(root);
            string generation = "exports/" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(root, generation);
            Directory.CreateDirectory(directory);
            string catalogFile = Path.Combine(root, "catalog.json");
            var records = new Dictionary<string, AssetRecord>();
            var resources = new ResourceWriter(directory, generation, records, options);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ExportRequest request in requests)
                {
                    string logical = request.path.Replace('\\', '/').Trim('/');
                    if (string.IsNullOrWhiteSpace(logical) || logical.Split('/').Any(p => p == ".." || p == ".") || logical.StartsWith("__assets/", StringComparison.Ordinal)) throw new ArgumentException("无效逻辑路径：" + logical);
                    if (!paths.Add(logical)) throw new ArgumentException("批次中逻辑路径重复：" + logical);
                    UnityEngine.Object asset = request.asset != null ? request.asset : request.prefab;
                    if (asset == null) throw new ArgumentException("导出入口没有资源：" + logical);
                    if (asset is GameObject prefab)
                    {
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
                        if (string.IsNullOrEmpty(guid)) throw new ArgumentException("必须导出工程资源，不能导出场景对象：" + logical);
                        string id = guid + "_prefab";
                        if (records.ContainsKey(id)) throw new ArgumentException("同一 Prefab 不能在同一批次使用多个逻辑路径。");
                        resources.Begin();
                        PrefabWriter.Read(prefab, resources, id, logical, (data, context) =>
                        {
                        string file = id + ".json";
                        ExportJson.Write(Path.Combine(directory, file), data);
                        context.OutputFile = Path.Combine(directory, file);
                        records.Add(id, new AssetRecord { id = id, sourcePath = AssetDatabase.GetAssetPath(prefab), name = prefab.name, description = (request.description ?? "")
                            + (data.warnings.Count == 0 ? "" : "\n[降级导出] " + string.Join("\n", data.warnings)), path = logical,
                            file = generation + "/" + file, type = "Prefab", dependencies = resources.End() });
                        });
                    }
                    else
                    {
                        string id = resources.Add(asset, logical, request.ui);
                        AssetRecord record = records[id];
                        if (!record.path.StartsWith("__assets/", StringComparison.Ordinal)) throw new ArgumentException("同一资源不能使用多个逻辑路径：" + logical);
                        record.path = logical;
                        record.description = request.description ?? "";
                    }
                }
                //提交期间持有排他锁；另一个编辑器正在提交时明确报错。
                using var commit = new FileStream(Path.Combine(root, ".export.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                Manifest previous = File.Exists(catalogFile) ? JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(catalogFile)) : new Manifest { owner = owner };
                if ((previous.formatVersion != 1 && previous.formatVersion != 2) || previous.owner != owner) throw new InvalidDataException("现有清单版本或所属模组不匹配。");
                //保留未重新导出的入口，但同 ID 的共享资源统一指向本次版本。
                foreach (AssetRecord old in previous.assets)
                {
                    if (!records.TryGetValue(old.id, out AssetRecord current))
                    { if (!paths.Contains(old.path)) records.Add(old.id, old); }
                    else if (current.path.StartsWith("__assets/", StringComparison.Ordinal) && !old.path.StartsWith("__assets/", StringComparison.Ordinal) && !paths.Contains(old.path))
                    { current.path = old.path; current.description = old.description; }
                }
                //只让仍有入口使用的依赖进入清单，历史目录继续保留以便检查。
                var reachable = new HashSet<string>();
                void Visit(string id) { if (!reachable.Add(id)) return; foreach (string dependency in records[id].dependencies) Visit(dependency); }
                foreach (AssetRecord record in records.Values.Where(a => !a.path.StartsWith("__assets/", StringComparison.Ordinal))) Visit(record.id);
                var manifest = new Manifest { owner = owner, formatVersion = previous.formatVersion, sharedFiles = previous.sharedFiles,
                    assets = records.Values.Where(a => reachable.Contains(a.id)).OrderBy(a => a.path, StringComparer.Ordinal).ToList() };
                string pending = Path.Combine(root, "catalog." + Guid.NewGuid().ToString("N") + ".tmp");
                ExportJson.Write(pending, manifest);
                if (File.Exists(catalogFile)) File.Replace(pending, catalogFile, null);
                else File.Move(pending, catalogFile);
                Debug.Log("[RimUnity] 已提交 " + requests.Length + " 个资源入口：" + catalogFile);
            }
            catch (Exception error)
            {
                //失败目录保留诊断，清单仍指向上次成功产物。
                File.WriteAllText(Path.Combine(directory, "FAILED.txt"), error.ToString(), new System.Text.UTF8Encoding(false));
                throw;
            }
        }
    }

    //逻辑路径与编辑器路径分离，描述进入可分发清单。
    [Serializable]
    public sealed class ExportRequest
    {
        public GameObject prefab;
        public UnityEngine.Object asset;
        //独立材质或 Shader 用于屏幕 UI 时，生成宿主 GUI 顶点布局。
        public bool ui;
        public string path, description;
    }
}
