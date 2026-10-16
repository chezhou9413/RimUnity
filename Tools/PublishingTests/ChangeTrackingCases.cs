using Newtonsoft.Json.Linq;
using RimUnity.Editor;
using UnityEditor;
using Entry = RimUnityWorkflow.Entry;
using Snapshot = RimUnity.Editor.ExportChangeTracker.Snapshot;

public sealed class TrackedProcessor : IExportChangeFingerprint
{
    public static string Token = "initial";
    public string GetChangeFingerprint() => Token;
}

internal static class ChangeTrackingCases
{
    internal static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS changes: " + message); }
        var directory = Path.Combine(Path.GetTempPath(), "RimUnity", "Changes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profile = new RimUnityWorkflow.Profile { repository = directory, gameRoot = directory,
            shaderTool = Path.Combine(directory, "tool.exe"), mappings = Array.Empty<RimUnityWorkflow.Mapping>() };
        File.WriteAllText(profile.shaderTool, "tool");
        var a = new Entry { path = "Assets/A.prefab", bundle = "a", mod = "A" };
        var b = new Entry { path = "Assets/B.prefab", bundle = "b", mod = "B" };
        const string material = "Assets/Auto.mat", texture = "Assets/Shared.png";
        AssetDatabase.Graph[a.path] = new[] { a.path, material, texture };
        AssetDatabase.Graph[b.path] = new[] { b.path, material, texture };
        Snapshot[] Capture() => ExportChangeTracker.Capture(profile, new[] { a, b }, Array.Empty<string>());
        var initial = Capture();
        EditorUtility.Dirty.Add("Assets/Cached.shader");
        Check(!ExportChangeTracker.HasUnsavedChanges("Assets/Cached.shader"), "Shader compiler dirty state is not an unsaved source edit");
        EditorUtility.Dirty.Clear();
        var consumer = new Snapshot { path = b.path, sources = new Dictionary<string, string>(initial[1].sources) };
        consumer.sources[a.path] = "referenced";
        Check(ExportChangeTracker.ExpandSelection(new[] { a, b }, new[] { initial[0], consumer }, new[] { a }).Length == 1,
            "explicit root selection never expands unrelated consumers");

        var preparedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in ExportChangeTracker.FingerprintFiles(profile).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var step in ExportChangeTracker.DigestSteps(file, hash => preparedFiles[file] = hash)) { }
        var prepared = ExportChangeTracker.Capture(profile, new[] { a, b }, Array.Empty<string>(),
            AssetDatabase.Graph, path => AssetDatabase.GetAssetDependencyHash(path), _ => false, path => preparedFiles[path]);
        Check(prepared.Select(s => s.context).SequenceEqual(initial.Select(s => s.context))
            && prepared.Zip(initial).All(p => p.First.sources.OrderBy(x => x.Key).SequenceEqual(p.Second.sources.OrderBy(x => x.Key))),
            "frame-collected dependency snapshot matches synchronous publication snapshot");

        var empty = new ExportChangeTracker.State();
        Check(new ExportChangeTracker(empty, initial, _ => "out").Reasons.ContainsKey(texture), "first run includes automatic dependencies");
        string baseline = Path.Combine(directory, "cache", "baseline.json");
        ExportChangeTracker.Commit(empty, initial, _ => "out", true, baseline);
        var state = ExportChangeTracker.Load(baseline);
        Check(new ExportChangeTracker(state, Capture(), _ => "out").Reasons.Count == 0, "successful unchanged inputs disappear from filter after reload");

        AssetDatabase.Hashes[texture] = "changed-texture";
        var modified = Capture();
        var changes = new ExportChangeTracker(state, modified, _ => "out");
        Check(changes.Reasons.ContainsKey(texture) && changes.Reasons.ContainsKey(a.path) && changes.Reasons.ContainsKey(b.path),
            "dependency change invalidates every consumer even without parent hash changes");
        Check(ExportChangeTracker.ExpandSelection(new[] { a, b }, modified, new[] { new Entry { path = texture } }).Length == 2,
            "selecting an automatic dependency exports both consumers without registering a new root");
        ExportChangeTracker.Commit(state, new[] { modified[0] }, _ => "out", false, baseline);
        state = ExportChangeTracker.Load(baseline);
        changes = new ExportChangeTracker(state, Capture(), _ => "out");
        var limited = ExportChangeTracker.ExpandSelection(new[] { a, b }, Capture(), new[] { new Entry { path = texture } },
            new HashSet<string>(changes.Reasons.Keys, StringComparer.OrdinalIgnoreCase));
        Check(limited.Length == 1 && limited[0].path == b.path, "dependency selection excludes already-current consumers");
        Check(!changes.Reasons.ContainsKey(a.path) && changes.Reasons.ContainsKey(b.path) && changes.Reasons.ContainsKey(texture),
            "publishing one consumer never acknowledges another consumer's stale dependency");
        string saved = File.ReadAllText(baseline);
        try { ExportChangeTracker.Commit(state, modified, _ => throw new IOException("failed output"), false, baseline); }
        catch (IOException) { }
        Check(saved == File.ReadAllText(baseline), "failed publication scan cannot advance baseline");

        var ui = ExportChangeTracker.Capture(profile, new[] { a, b }, new[] { material });
        Check(new ExportChangeTracker(state, ui, _ => "out").Reasons.ContainsKey(a.path), "automatic material UI flag invalidates parent");
        profile.disableCastShadows = true;
        Check(new ExportChangeTracker(state, Capture(), _ => "out").Reasons.ContainsKey(a.path), "renderer policy change invalidates entries");
        profile.disableCastShadows = false;
        File.WriteAllText(profile.shaderTool, "new tool");
        Check(new ExportChangeTracker(state, Capture(), _ => "out").Reasons.ContainsKey(a.path), "tool binary change invalidates entries");
        File.WriteAllText(profile.shaderTool, "tool");
        AssetDatabase.Graph[a.path] = new[] { a.path, texture };
        Check(new ExportChangeTracker(state, Capture(), _ => "out").Reasons.ContainsKey(a.path), "removed reference is detected independently of parent hash");
        AssetDatabase.Graph[a.path] = new[] { a.path, material, texture };
        EditorUtility.Dirty.Add(texture);
        Check(new ExportChangeTracker(state, Capture(), _ => "out").Reasons.ContainsKey(a.path), "unsaved dependency remains conservatively changed");
        Check(new ExportChangeTracker(state, Capture(), _ => "out").Reasons[a.path].Contains("未保存"),
            "unsaved source reason is distinct from external processor uncertainty");
        EditorUtility.Dirty.Clear();
        TypeCache.Types = new[] { typeof(ChangeTrackingCases) };
        Check(new ExportChangeTracker(state, Capture(), _ => "out").Reasons.ContainsKey(a.path), "opaque processor never silently hides external input changes");
        TypeCache.Types = new[] { typeof(TrackedProcessor) };
        var processorState = new ExportChangeTracker.State();
        var withProcessor = Capture();
        ExportChangeTracker.Commit(processorState, withProcessor, _ => "out", true, baseline);
        processorState = ExportChangeTracker.Load(baseline);
        Check(new ExportChangeTracker(processorState, Capture(), _ => "out").Reasons.Count == 0,
            "processor with declared external fingerprint can become unchanged");
        TrackedProcessor.Token = "external-file-edited";
        Check(new ExportChangeTracker(processorState, Capture(), _ => "out").Reasons.ContainsKey(a.path),
            "processor external fingerprint change invalidates consumers");
        TypeCache.Types = Array.Empty<Type>();

        var item = initial[0];
        Directory.CreateDirectory(item.target);
        File.WriteAllText(Path.Combine(item.target, "root.json"), "{}");
        File.WriteAllText(Path.Combine(item.target, "dep.json"), "{\"members\":[{\"name\":\"file\",\"value\":{\"text\":\"payload.bin\"}}]}");
        File.WriteAllText(Path.Combine(item.target, "payload.bin"), "pixels");
        var catalog = new JObject { ["formatVersion"] = 2, ["assets"] = new JArray(
            new JObject { ["id"] = "root", ["path"] = item.logical, ["file"] = "root.json", ["dependencies"] = new JArray("dep") },
            new JObject { ["id"] = "dep", ["path"] = "__assets/dep", ["file"] = "dep.json", ["dependencies"] = new JArray() }) };
        File.WriteAllText(Path.Combine(item.target, "catalog.json"), catalog.ToString());
        string Output() => new ExportChangeTracker.OutputReader(profile).Read(item);
        string original = Output();
        File.WriteAllText(Path.Combine(item.target, "payload.bin"), "PIXELS");
        Check(Output() != original, "output byte changes detected even with same file length");
        File.Delete(Path.Combine(item.target, "payload.bin"));
        bool missing = false;
        try { Output(); } catch (FileNotFoundException) { missing = true; }
        Check(missing, "missing automatic output payload detected");
        //A successful partial export does not invalidate unchanged sibling output signatures.
        File.WriteAllText(Path.Combine(item.target, "payload.bin"), "pixels");
        File.WriteAllText(Path.Combine(item.target, "unrelated.json"), "{}");
        ((JArray)catalog["assets"]).Add(new JObject { ["id"] = "other", ["path"] = "other", ["file"] = "unrelated.json", ["dependencies"] = new JArray() });
        File.WriteAllText(Path.Combine(item.target, "catalog.json"), catalog.ToString());
        Check(Output() == original, "unrelated catalog additions do not invalidate unchanged roots");
        Directory.CreateDirectory(Path.Combine(item.target, "exports", "next"));
        File.Copy(Path.Combine(item.target, "dep.json"), Path.Combine(item.target, "exports/next/dep.json"));
        catalog["assets"][1]["file"] = "exports/next/dep.json";
        File.WriteAllText(Path.Combine(item.target, "catalog.json"), catalog.ToString());
        Check(Output() == original, "shared descriptor relocation does not cause endless consumer re-export");
        string packedFile = Path.Combine(item.target, "dep.json.gz");
        using (var stream = File.Create(packedFile))
        using (var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Compress))
        { var bytes = File.ReadAllBytes(Path.Combine(item.target, "dep.json")); gzip.Write(bytes); }
        catalog["assets"][1]["file"] = "dep.json.gz";
        File.WriteAllText(Path.Combine(item.target, "catalog.json"), catalog.ToString());
        Check(!string.IsNullOrEmpty(Output()), "gzip descriptions are followed to their payloads");
        string owner = Path.Combine(directory, "Owner");
        Directory.CreateDirectory(Path.Combine(owner, "About"));
        File.WriteAllText(Path.Combine(owner, "About/About.xml"), "<ModMetaData><packageId>tests.owner</packageId></ModMetaData>");
        string ownerRoot = Path.Combine(owner, "1.7/Data/RimUnity");
        Directory.CreateDirectory(Path.Combine(ownerRoot, "blobs"));
        string blob = "blobs/" + new string('a', 64) + ".bin";
        File.WriteAllText(Path.Combine(ownerRoot, blob), "shared pixels");
        File.WriteAllText(Path.Combine(item.target, "dep.json"), "{\"members\":[{\"name\":\"file\",\"value\":{\"text\":\"" + blob + "\"}}]}");
        catalog["assets"][1]["file"] = "dep.json";
        catalog["sharedFiles"] = new JObject { [blob] = "tests.owner" };
        File.WriteAllText(Path.Combine(item.target, "catalog.json"), catalog.ToString());
        profile.mappings = new[] { new RimUnityWorkflow.Mapping { bundle = "owner", mod = owner } };
        string sharedSignature = Output();
        File.WriteAllText(Path.Combine(ownerRoot, blob), "edited pixels");
        Check(Output() != sharedSignature, "cross-mod shared payload changes are detected");

        File.WriteAllText(baseline, "invalid");
        Check(ExportChangeTracker.Load(baseline).entries.Count == 0, "corrupt baseline falls back to first-run behavior");
        AssetDatabase.Graph.Clear(); AssetDatabase.Hashes.Clear();
    }
}
