using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using RimUnity.Serialization;

namespace RimUnity.Editor
{
    //只在本次共同发布、且声明必需依赖的模组之间共享文件。
    public static class ExportSharing
    {
        //显式目录表供离线打包及增量导出解析依赖，不扫描用户磁盘。
        public static Dictionary<string, string> Roots(IEnumerable<string> mods)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string mod in mods.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string owner = Owner(mod);
                string root = Path.GetFullPath(Path.Combine(mod, "1.7/Data/RimUnity"));
                if (result.ContainsKey(owner) && result[owner] != root) throw new InvalidDataException("模组 packageId 重复：" + owner);
                result[owner] = root;
            }
            return result;
        }

        //参数为正式模组目录到准备目录的映射，正式内容不受改动。
        public static void Share(IDictionary<string, string> prepared)
        {
            var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var required = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in prepared)
            {
                string owner = Owner(pair.Key);
                roots.Add(owner, pair.Value);
                var about = XDocument.Load(Path.Combine(pair.Key, "About/About.xml")).Root;
                required[owner] = (about.Element("modDependencies")?.Elements("li") ?? Enumerable.Empty<XElement>())
                    .Concat(about.Element("modDependenciesByVersion")?.Element("v1.7")?.Elements("li") ?? Enumerable.Empty<XElement>())
                    .Select(e => e.Element("packageId")?.Value.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string owner)
            {
                if (visited.Contains(owner)) return;
                if (!active.Add(owner)) throw new InvalidDataException("共享模组依赖形成循环：" + owner);
                foreach (string dependency in required[owner].Where(roots.ContainsKey)) Visit(dependency);
                string root = roots[owner];
                var catalog = ExportStorage.Read(ContentFiles.Inside(root, "catalog.json"));
                if (!string.Equals((string)catalog["owner"], owner, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("资源清单 owner 与 About 不一致：" + root);
                var shared = (JObject)catalog["sharedFiles"] ?? new JObject();
                var removals = new List<string>();
                string blobs = ContentFiles.Inside(root, "blobs");
                foreach (string file in Directory.Exists(blobs) ? Directory.GetFiles(blobs) : Array.Empty<string>())
                {
                    string relative = "blobs/" + Path.GetFileName(file);
                    ContentFiles.RequireBlob(relative);
                    foreach (string dependency in required[owner].Where(roots.ContainsKey))
                    {
                        string candidate = ContentFiles.Inside(roots[dependency], relative);
                        //共享链不转发；依赖模组必须实际拥有该文件。
                        if (!File.Exists(candidate)) continue;
                        if (!File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(candidate)))
                            throw new InvalidDataException("内容寻址文件同名但内容不同：" + relative);
                        shared[relative] = dependency;
                        removals.Add(ContentFiles.Inside(root, relative));
                        break;
                    }
                }
                catalog["formatVersion"] = 2;
                catalog["sharedFiles"] = shared;
                ExportJson.Write(ContentFiles.Inside(root, "catalog.json"), catalog);
                foreach (string file in removals) File.Delete(file);
                active.Remove(owner);
                visited.Add(owner);
            }
            foreach (string owner in roots.Keys) Visit(owner);
        }

        //读取实际内容模组的身份。
        public static string Owner(string mod)
        {
            string owner = XDocument.Load(Path.Combine(mod, "About/About.xml")).Root.Element("packageId")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(owner)) throw new InvalidDataException("模组缺少 packageId：" + mod);
            return owner;
        }
    }
}
