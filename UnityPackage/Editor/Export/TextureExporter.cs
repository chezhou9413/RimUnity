using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RimUnity.Serialization;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //刷新已有清单中的贴图，保留预制体、材质变体和公开路径。
    public static class TextureExporter
    {
        //批处理入口要求显式的副本目录；可选发布目录必须是空目录。
        public static void RefreshBatch()
        {
            string root = Environment.GetEnvironmentVariable("RIMUNITY_TEXTURE_EXPORT");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("需要 RIMUNITY_TEXTURE_EXPORT 指定资源目录。");
            Refresh(root);
            string publish = Environment.GetEnvironmentVariable("RIMUNITY_TEXTURE_PUBLISH");
            if (!string.IsNullOrWhiteSpace(publish)) ExportStorage.CopyForPublish(root, publish);
        }

        //只处理 GUID 属于当前 Unity 工程的资源，多工程可依次刷新同一清单。
        public static void Refresh(string dataDirectory)
        {
            string catalog = Path.Combine(dataDirectory, "catalog.json");
            using var commit = new FileStream(Path.Combine(dataDirectory, ".export.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(catalog));
            string generation = "exports/" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(dataDirectory, generation);
            Directory.CreateDirectory(directory);
            var records = new Dictionary<string, AssetRecord>();
            var resources = new ResourceWriter(directory, generation, records);
            int count = 0;
            foreach (var old in manifest.assets.Where(a => a.type == "Texture2D"))
            {
                string guid = AssetDatabase.AssetPathToGUID(old.sourcePath);
                if (string.IsNullOrEmpty(guid) || !old.id.StartsWith(guid + "_", StringComparison.Ordinal)) continue;
                var source = AssetDatabase.LoadAllAssetsAtPath(old.sourcePath).OfType<Texture2D>().SingleOrDefault(t =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t, out string id, out long local) && id + "_" + local == old.id);
                if (source == null) throw new InvalidDataException("源贴图的 local ID 不存在：" + old.sourcePath);
                string key = resources.Add(source, old.path);
                var updated = records[key]; updated.path = old.path; updated.description = old.description;
                count++;
            }
            if (count == 0) throw new InvalidDataException("当前工程没有清单中的贴图：" + dataDirectory);
            manifest.assets = manifest.assets.Select(a => records.TryGetValue(a.id, out var updated) ? updated : a).ToList();
            string pending = catalog + "." + Guid.NewGuid().ToString("N") + ".tmp";
            ExportJson.Write(pending, manifest);
            File.Replace(pending, catalog, null);
            Debug.Log("[RimUnity] 已刷新贴图 " + count + " 张：" + catalog);
        }
    }
}
