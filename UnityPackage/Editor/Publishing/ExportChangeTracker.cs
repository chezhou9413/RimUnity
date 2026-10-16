using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimUnity.Serialization;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //Project-local successful publication baselines. No static asset or session cache.
    internal sealed class ExportChangeTracker
    {
        internal const string BaselinePath = "Library/RimUnity/export-baseline.json";
        internal sealed class Snapshot
        {
            public string key, path, target, logical, context, output, uncertaintyReason;
            public bool uncertain;
            public Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        internal sealed class State
        {
            public int version = 1;
            public Dictionary<string, Snapshot> entries = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Snapshot[] current;
        internal readonly Dictionary<string, string> Reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal Snapshot[] Snapshots => current;

        internal ExportChangeTracker(State baseline, Snapshot[] current, Func<Snapshot, string> output)
        {
            this.current = current;
            foreach (var item in current)
            {
                baseline.entries.TryGetValue(item.key, out var old);
                string reason = old == null ? "未发布或没有成功基线"
                    : item.uncertain ? item.uncertaintyReason ?? "输入状态无法确认"
                    : old.context != item.context ? "导出设置、UI 标记或工具链变化" : null;
                if (reason != null)
                {
                    foreach (string path in item.sources.Keys) Mark(path, reason);
                    Mark(item.path, reason);
                    continue;
                }
                foreach (var source in item.sources)
                    if (!old.sources.TryGetValue(source.Key, out string hash) || hash != source.Value)
                    { Mark(source.Key, "资源、导入设置或其依赖变化"); Mark(item.path, "入口或自动依赖变化"); }
                if (old.sources.Keys.Except(item.sources.Keys, StringComparer.OrdinalIgnoreCase).Any())
                    Mark(item.path, "自动依赖已移除或替换");
                try
                {
                    if (old.output == null || old.output != output(item)) Mark(item.path, "输出缺失、变化或依赖载荷损坏");
                }
                catch (Exception) { Mark(item.path, "输出缺失或无法读取"); }
            }
        }

        private void Mark(string path, string reason) { if (!Reasons.ContainsKey(path)) Reasons.Add(path, reason); }

        internal static State Load(string path = BaselinePath)
        {
            try
            {
                var state = File.Exists(path) ? JsonConvert.DeserializeObject<State>(File.ReadAllText(path)) : null;
                if (state?.version != 1 || state.entries == null) return new State();
                state.entries = new Dictionary<string, Snapshot>(state.entries, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in state.entries.Values)
                    entry.sources = new Dictionary<string, string>(entry.sources ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                return state;
            }
            catch (Exception) { return new State(); } //A lost/corrupt cache must never hide changes.
        }

        internal static Snapshot[] Capture(RimUnityWorkflow.Profile profile, IEnumerable<RimUnityWorkflow.Entry> entries, IEnumerable<string> uiPaths,
            IDictionary<string, string[]> dependencies = null, Func<string, string> preparedHash = null,
            Func<string, bool> preparedDirty = null, Func<string, string> preparedFileHash = null)
        {
            bool Dirty(string path) => preparedDirty != null ? preparedDirty(path) : HasUnsavedChanges(path);
            string FileHash(string path) => preparedFileHash != null ? preparedFileHash(path ?? "") : DigestFile(path);
            var ui = new HashSet<string>(uiPaths, StringComparer.OrdinalIgnoreCase);
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string SourceHash(string path)
            {
                if (!hashes.TryGetValue(path, out string hash))
                {
                    //The Unity hash includes transitive imports, .meta, platform and importer version.
                    hash = preparedHash != null ? preparedHash(path) : AssetDatabase.GetAssetDependencyHash(path).ToString();
                    if (Dirty(path)) hash += ":unsaved";
                    hashes[path] = hash;
                }
                return hash;
            }
            var processors = TypeCache.GetTypesWithAttribute<RimUnityExportProcessorAttribute>().ToArray();
            bool opaqueProcessor = false;
            var processorInputs = new List<string>();
            foreach (Type type in processors.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                string token = null;
                try
                {
                    if (typeof(IExportChangeFingerprint).IsAssignableFrom(type))
                        token = ((IExportChangeFingerprint)Activator.CreateInstance(type)).GetChangeFingerprint();
                }
                catch (Exception) { } //Unknown external state is always changed, never silently skipped.
                opaqueProcessor |= token == null;
                processorInputs.Add(type.AssemblyQualifiedName + ":" + type.Module.ModuleVersionId + ":" + token);
            }
            string tool = FileHash(profile.shaderTool);
            string host = FileHash(Path.Combine(profile.gameRoot ?? "", "Waylith.dll"));
            string common = JsonConvert.SerializeObject(new {
                policy = 1, unity = Application.unityVersion, platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
                exporter = typeof(ExportChangeTracker).Assembly.ManifestModule.ModuleVersionId.ToString(),
                tool, host,
                about = (profile.mappings ?? Array.Empty<RimUnityWorkflow.Mapping>()).Where(m => !string.IsNullOrWhiteSpace(m.mod))
                    .Select(m => FileHash(Path.Combine(profile.repository, m.mod, "About/About.xml"))).ToArray(),
                toolFiles = Directory.Exists(Path.GetDirectoryName(profile.shaderTool))
                    ? Directory.GetFiles(Path.GetDirectoryName(profile.shaderTool)).Where(f => new[] { ".dll", ".json", ".config" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => f, StringComparer.Ordinal).Select(f => f + ":" + FileHash(f)).ToArray() : Array.Empty<string>(),
                profile.disableCastShadows, profile.disableReceiveShadows,
                profile.disableLightProbes, profile.disableReflectionProbes,
                mappings = profile.mappings, owners = profile.resourceOwners,
                processors = processorInputs
            });
            return entries.Select(entry => {
                string target = Path.GetFullPath(Path.Combine(profile.repository, entry.mod ?? "", "1.7/Data/RimUnity"));
                string logical = "Bundles/" + entry.bundle + "/" + entry.path;
                var sources = (dependencies != null ? dependencies[entry.path] : AssetDatabase.GetDependencies(new[] { entry.path }, true)).Concat(new[] { entry.path })
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                return new Snapshot {
                    key = target + "|" + logical, path = entry.path, target = target, logical = logical,
                    context = Hash(common + JsonConvert.SerializeObject(new { rootUi = entry.ui,
                        ui = sources.Where(p => ui.Contains(p)).ToArray() })),
                    uncertain = opaqueProcessor || sources.Any(Dirty),
                    uncertaintyReason = opaqueProcessor ? "自定义处理器未声明外部输入指纹" : sources.Any(Dirty) ? "源资源尚未保存" : null,
                    sources = sources.ToDictionary(p => p, SourceHash, StringComparer.OrdinalIgnoreCase)
                };
            }).ToArray();
        }

        internal static ExportChangeTracker Scan(RimUnityWorkflow.Profile profile, IEnumerable<RimUnityWorkflow.Entry> entries, IEnumerable<string> uiPaths)
        {
            var output = new OutputReader(profile);
            return new ExportChangeTracker(Load(), Capture(profile, entries, uiPaths), output.Read);
        }

        internal static void Commit(State state, Snapshot[] snapshots, Func<Snapshot, string> output, bool full, string path = BaselinePath)
        {
            //Compute all output signatures first. A failed scan/write cannot partially advance the baseline.
            var next = new State { entries = new Dictionary<string, Snapshot>(state.entries, StringComparer.OrdinalIgnoreCase) };
            if (full)
                foreach (string key in next.entries.Where(p => snapshots.Any(s => s.target == p.Value.target)).Select(p => p.Key).ToArray())
                    next.entries.Remove(key);
            foreach (var item in snapshots)
            {
                var saved = JsonConvert.DeserializeObject<Snapshot>(JsonConvert.SerializeObject(item));
                saved.output = output(item);
                next.entries[item.key] = saved;
            }
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, ".baseline-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(next, Formatting.Indented));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static void RecordSuccess(RimUnityWorkflow.Profile profile, Snapshot[] snapshots, bool full)
        {
            var output = new OutputReader(profile);
            Commit(Load(), snapshots, output.Read, full);
        }

        internal static bool HasUnsavedChanges(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            //Shader dirtiness also tracks editor/compiler state. Shader text/meta/import changes
            //are represented by GetAssetDependencyHash; a dirty flag alone is not a source edit.
            return asset != null && !(asset is Shader) && EditorUtility.IsDirty(asset);
        }

        internal static RimUnityWorkflow.Entry[] ExpandSelection(IEnumerable<RimUnityWorkflow.Entry> entries,
            Snapshot[] snapshots, IEnumerable<RimUnityWorkflow.Entry> selection, ISet<string> changedPaths = null)
        {
            var roots = entries.ToArray();
            var chosen = selection.ToArray();
            var registered = new HashSet<string>(roots.Select(e => e.path), StringComparer.OrdinalIgnoreCase);
            var dependencies = new HashSet<string>(chosen.Where(e => !registered.Contains(e.path)).Select(e => e.path), StringComparer.OrdinalIgnoreCase);
            var affected = new HashSet<string>(snapshots.Where(s => (changedPaths == null || changedPaths.Contains(s.path))
                && s.sources.Keys.Any(dependencies.Contains)).Select(s => s.path), StringComparer.OrdinalIgnoreCase);
            //Explicit roots mean exactly those roots. Only non-root dependency rows expand consumers.
            return chosen.Where(e => registered.Contains(e.path)).Concat(roots.Where(e => affected.Contains(e.path)))
                .Concat(chosen.Where(e => !registered.Contains(e.path) && !snapshots.Any(s => s.sources.ContainsKey(e.path))))
                .GroupBy(e => e.path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
        }

        private static string Hash(string text)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        private static string DigestFile(string path)
        {
            string result = null;
            foreach (var step in DigestSteps(path, hash => result = hash)) { }
            return result;
        }
        internal static IEnumerable<string> DigestSteps(string path, Action<string> completed)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            { completed("missing:" + path); yield break; }
            using var sha = SHA256.Create();
            using var input = File.OpenRead(path);
            var buffer = new byte[128 * 1024];
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, count, buffer, 0);
                yield return path;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            completed(BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant());
        }
        internal static IEnumerable<string> FingerprintFiles(RimUnityWorkflow.Profile profile)
        {
            yield return profile.shaderTool ?? "";
            yield return Path.Combine(profile.gameRoot ?? "", "Waylith.dll");
            foreach (var mapping in profile.mappings ?? Array.Empty<RimUnityWorkflow.Mapping>())
                if (!string.IsNullOrWhiteSpace(mapping.mod)) yield return Path.Combine(profile.repository, mapping.mod, "About/About.xml");
            string directory = Path.GetDirectoryName(profile.shaderTool);
            if (Directory.Exists(directory))
                foreach (string file in Directory.GetFiles(directory).Where(f => new[] { ".dll", ".json", ".config" }.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.Ordinal))
                    yield return file;
        }

        //One scan owns file/JSON/hash caches, released on completion. Includes shared and gzip payloads.
        internal sealed class OutputReader
        {
            private readonly Dictionary<string, string> owners;
            private readonly Dictionary<string, JObject> json = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal OutputReader(RimUnityWorkflow.Profile profile)
            {
                owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mapping in profile.mappings ?? Array.Empty<RimUnityWorkflow.Mapping>())
                {
                    if (string.IsNullOrWhiteSpace(mapping.mod)) continue;
                    try
                    {
                        foreach (var pair in ExportSharing.Roots(new[] { Path.GetFullPath(Path.Combine(profile.repository, mapping.mod)) }))
                            owners[pair.Key] = pair.Value;
                    }
                    catch (Exception) { } //Missing owner output is handled as changed by Read.
                }
            }
            private JObject Json(string file)
            {
                if (!json.TryGetValue(file, out var value)) json[file] = value = ExportStorage.Read(file);
                return value;
            }
            internal string Read(Snapshot snapshot)
            {
                string result = null;
                foreach (var step in ReadSteps(snapshot, hash => result = hash)) { }
                return result;
            }
            internal IEnumerable<string> ReadSteps(Snapshot snapshot, Action<string> completed)
            {
                var catalog = Json(Path.Combine(snapshot.target, "catalog.json"));
                var assets = ((JArray)catalog["assets"]).OfType<JObject>().ToDictionary(a => (string)a["id"]);
                var root = assets.Values.Single(a => (string)a["path"] == snapshot.logical);
                var visited = new HashSet<string>();
                var parts = new List<string>();
                string Resolve(string relative)
                {
                    string directory = snapshot.target;
                    if (catalog["sharedFiles"] is JObject shared && shared[relative] != null)
                    {
                        ContentFiles.RequireBlob(relative);
                        directory = owners[(string)shared[relative]];
                    }
                    return ContentFiles.Inside(directory, relative);
                }
                IEnumerable<string> FilePart(string relative)
                {
                    string file = Resolve(relative);
                    if (!hashes.TryGetValue(file, out string hash))
                    {
                        if (!File.Exists(file)) throw new FileNotFoundException("缺失导出载荷", file);
                        foreach (var step in DigestSteps(file, value => hash = value)) yield return step;
                        hashes[file] = hash;
                    }
                    parts.Add(hash);
                    yield return file;
                }
                IEnumerable<string> Visit(string id)
                {
                    if (!visited.Add(id)) yield break;
                    var asset = assets[id];
                    var identity = (JObject)asset.DeepClone();
                    identity.Remove("file"); //Generation-directory relocation is not a content change.
                    parts.Add(identity.ToString(Formatting.None));
                    string relative = (string)asset["file"];
                    foreach (var step in FilePart(relative)) yield return step;
                    var data = Json(Resolve(relative));
                    foreach (string field in new[] { "vertexFile", "fragmentFile", "descriptorFile" })
                        if (data[field]?.Type == JTokenType.String)
                            foreach (var step in FilePart((string)data[field])) yield return step;
                    foreach (var member in (data["members"] as JArray ?? new JArray()).OfType<JObject>())
                        if ((string)member["name"] == "file")
                            foreach (var step in FilePart((string)member["value"]?["text"])) yield return step;
                    foreach (string dependency in (asset["dependencies"] as JArray ?? new JArray()).Values<string>().OrderBy(p => p, StringComparer.Ordinal))
                        foreach (var step in Visit(dependency)) yield return step;
                }
                foreach (var step in Visit((string)root["id"])) yield return step;
                completed(Hash(string.Join("\n", parts)));
            }
        }
    }
}
