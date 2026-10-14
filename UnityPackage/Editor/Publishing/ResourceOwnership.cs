using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using RimUnity.Serialization;
using UnityEditor;

namespace RimUnity.Editor
{
    //显式归属优先于自动共享；仅修改本次发布的准备目录。
    internal static class ResourceOwnership
    {
        internal sealed class Duplicate
        {
            public string path;
            public string[] bundles;
        }

        internal static Duplicate[] Scan(IEnumerable<RimUnityWorkflow.Entry> entries)
        {
            var usage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in entries.Where(e => !string.IsNullOrWhiteSpace(e.bundle)).GroupBy(e => e.bundle))
                foreach (string path in AssetDatabase.GetDependencies(group.Select(e => e.path).Distinct().ToArray(), true))
                {
                    if ((!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal))
                        || AssetDatabase.IsValidFolder(path) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!usage.TryGetValue(path, out var bundles)) usage.Add(path, bundles = new HashSet<string>(StringComparer.Ordinal));
                    bundles.Add(group.Key);
                }
            return usage.Where(p => p.Value.Count > 1).OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new Duplicate { path = p.Key, bundles = p.Value.OrderBy(b => b, StringComparer.Ordinal).ToArray() }).ToArray();
        }

        internal static void Apply(RimUnityWorkflow.Profile profile, IDictionary<string, string> prepared, Action<string> log)
        {
            var pins = profile.resourceOwners ?? Array.Empty<RimUnityWorkflow.ResourceOwner>();
            if (pins.Length == 0) return;
            var roots = prepared.ToDictionary(p => ExportSharing.Owner(p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase);
            var catalogs = roots.ToDictionary(p => p.Key, p => ExportStorage.Read(ContentFiles.Inside(p.Value, "catalog.json")), StringComparer.OrdinalIgnoreCase);
            var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
            string Resolve(string owner, string relative)
            {
                var shared = catalogs[owner]["sharedFiles"] as JObject;
                string actual = (string)shared?[relative] ?? owner;
                return ContentFiles.Inside(roots[actual], relative);
            }
            string Move(string sourceOwner, string relative, string targetOwner, bool descriptor)
            {
                string source = Resolve(sourceOwner, relative);
                byte[] bytes = File.ReadAllBytes(source);
                string result = relative;
                if (descriptor)
                {
                    string hash;
                    using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    result = "blobs/" + hash + (relative.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase) ? ".json.gz" : ".json");
                }
                ContentFiles.RequireBlob(result);
                if (assigned.TryGetValue(result, out string other) && !string.Equals(other, targetOwner, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("相同内容被指定了不同归属，请统一迁移目标：" + result);
                assigned[result] = targetOwner;
                string output = ContentFiles.Inside(roots[targetOwner], result);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                if (File.Exists(output) && !File.ReadAllBytes(output).SequenceEqual(bytes)) throw new InvalidDataException("共享内容冲突：" + result);
                if (!File.Exists(output)) File.WriteAllBytes(output, bytes);
                foreach (var pair in catalogs)
                {
                    var shared = pair.Value["sharedFiles"] as JObject ?? new JObject();
                    if (pair.Value["sharedFiles"] == null) pair.Value["sharedFiles"] = shared;
                    string local = ContentFiles.Inside(roots[pair.Key], result);
                    bool uses = shared[result] != null || File.Exists(local) || pair.Key == sourceOwner;
                    if (pair.Key == targetOwner) shared.Remove(result);
                    else if (uses)
                    {
                        shared[result] = targetOwner;
                        if (File.Exists(local)) File.Delete(local);
                    }
                }
                return result;
            }
            foreach (var pin in pins)
            {
                var mapping = profile.mappings.SingleOrDefault(m => m.bundle == pin.bundle);
                if (mapping == null) throw new InvalidDataException("迁移目标包未配置：" + pin.bundle);
                string targetOwner = ExportSharing.Owner(Path.GetFullPath(Path.Combine(profile.repository, mapping.mod)));
                if (!roots.ContainsKey(targetOwner)) throw new InvalidDataException("迁移目标必须参与本次发布：" + pin.bundle);
                string guid = AssetDatabase.AssetPathToGUID(pin.path);
                foreach (var pair in catalogs)
                    foreach (var record in ((JArray)pair.Value["assets"]).OfType<JObject>().Where(r =>
                        string.Equals((string)r["sourcePath"], pin.path, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(guid) && ((string)r["id"] ?? "").StartsWith(guid + "_", StringComparison.Ordinal))).ToArray())
                    {
                        string oldFile = (string)record["file"];
                        var data = ExportStorage.Read(Resolve(pair.Key, oldFile));
                        foreach (string blob in data.Descendants().OfType<JValue>().Where(v => v.Type == JTokenType.String)
                            .Select(v => (string)v).Where(v => v.StartsWith("blobs/", StringComparison.Ordinal)).Distinct().ToArray())
                            Move(pair.Key, blob, targetOwner, false);
                        record["file"] = Move(pair.Key, oldFile, targetOwner, true);
                        string oldLocal = ContentFiles.Inside(roots[pair.Key], oldFile);
                        if (!oldFile.StartsWith("blobs/", StringComparison.Ordinal) && File.Exists(oldLocal)
                            && !((JArray)pair.Value["assets"]).Any(r => (string)r["file"] == oldFile)) File.Delete(oldLocal);
                    }
                log?.Invoke("资源归属：" + pin.path + " → " + pin.bundle);
            }
            foreach (var pair in catalogs) ExportJson.Write(ContentFiles.Inside(roots[pair.Key], "catalog.json"), pair.Value);
        }
    }
}