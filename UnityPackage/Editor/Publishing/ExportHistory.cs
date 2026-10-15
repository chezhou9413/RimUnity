using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RimUnity.Editor
{
    //Immutable snapshots of each window publication; never read today's catalog when displaying old history.
    [Serializable] internal sealed class ExportHistory
    {
        public DateTime started = DateTime.Now, finished;
        public bool full;
        public string status = "进行中";
        public string[] inputs = Array.Empty<string>(), dependencies = Array.Empty<string>();
        public string[] targets = Array.Empty<string>();
        public List<Output> outputs = new List<Output>();
        public List<string> messages = new List<string>();
        [Serializable] internal sealed class Output
        {
            public string target;
            public string[] assets, files;
        }

        internal void CaptureOutputs()
        {
            var snapshot = new List<Output>();
            foreach (string target in targets)
            {
                var catalog = JObject.Parse(File.ReadAllText(Path.Combine(target, "catalog.json")));
                var shared = catalog["sharedFiles"] as JObject;
                snapshot.Add(new Output {
                    target = target,
                    assets = (catalog["assets"] as JArray ?? new JArray()).OfType<JObject>().Select(a => {
                        string file = (string)a["file"];
                        string owner = file == null ? null : (string)shared?[file];
                        return ((string)a["type"] ?? "资源") + "  " + ((string)a["sourcePath"] ?? (string)a["path"] ?? (string)a["id"]) +
                            "\n  地址：" + (string)a["path"] + "\n  文件：" + file + (owner == null ? "" : "\n  共享所属：" + owner);
                    }).ToArray(),
                    files = Directory.GetFiles(target, "*", SearchOption.AllDirectories)
                        .Select(f => f.Substring(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1).Replace('\\', '/'))
                        .OrderBy(f => f, StringComparer.Ordinal).ToArray()
                });
            }
            outputs = snapshot;
        }
    }
}
