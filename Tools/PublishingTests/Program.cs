using RimUnity.Editor;
using Newtonsoft.Json.Linq;
using Entry = RimUnityWorkflow.Entry;

if (args.Length > 1 && args[0] == "--validate")
{
    foreach (string path in args.Skip(1)) { RimUnityPublisher.Validate(path); Console.WriteLine("PASS actual catalog: " + path); }
    return;
}

string root = Path.GetFullPath(args.Length == 1 ? args[0] : Path.Combine(Path.GetTempPath(), "RimUnity", "PublishingTests"));
Directory.CreateDirectory(root);
string run = Path.Combine(root, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(run);
var profile = new RimUnityWorkflow.Profile { repository = run, shaderTool = Path.Combine(run, "tool.exe"), gameRoot = run };
File.WriteAllText(profile.shaderTool, "test");
File.WriteAllText(Path.Combine(run, "Waylith.dll"), "test");
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
string Mod(string name, bool old = true)
{
    string mod = Path.Combine(run, name);
    Directory.CreateDirectory(Path.Combine(mod, "About"));
    File.WriteAllText(Path.Combine(mod, "About/About.xml"), "<ModMetaData><packageId>tests." + name + "</packageId></ModMetaData>");
    if (old)
    {
        string live = Path.Combine(mod, "1.7/Data/RimUnity"); Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "old.json"), "{\"members\":[{\"name\":\"file\",\"value\":{\"kind\":\"string\",\"text\":\"old-sidecar.png\"}}]}");
        File.WriteAllText(Path.Combine(live, "old-sidecar.png"), "old image");
        File.WriteAllText(Path.Combine(live, "catalog.json"), new JObject { ["formatVersion"] = 1,
            ["assets"] = new JArray(new JObject { ["id"] = "old", ["file"] = "old.json", ["path"] = "old", ["dependencies"] = new JArray() }) }.ToString());
    }
    return mod;
}
Entry E(string mod, string path = "new") => new() { mod = mod, path = path, status = "pending", bundle = "test" };
bool Old(string mod) => File.Exists(Path.Combine(mod, "1.7/Data/RimUnity/old-sidecar.png"));
void Failure(Action action, string name)
{
    try { action(); } catch { Console.WriteLine("PASS " + name); return; }
    throw new Exception("Expected failure: " + name);
}
var messages = new List<string>();
string full = Mod("full");
RimUnityPublisher.Export(profile, new[] { E(full) }, true, messages.Add);
Check(!Old(full) && File.Exists(Path.Combine(full, "1.7/Data/RimUnity/new.json")), "full rebuild removes all old snapshot files after success");
Check(!Directory.GetDirectories(Path.Combine(full, "1.7/Data"), ".rimunity-*").Any(), "owned stage and backup cleaned");
string incremental = Mod("incremental");
RimUnityPublisher.Export(profile, new[] { E(incremental) }, false, messages.Add);
var prior = JObject.Parse(File.ReadAllText(Path.Combine(incremental, "1.7/Data/RimUnity/old.json")));
string priorPayload = (string)prior["members"][0]["value"]["text"];
Check(File.ReadAllText(Path.Combine(incremental, "1.7/Data/RimUnity", priorPayload)) == "old image",
    "incremental preserves prior entries and shared sidecars");
string first = Mod("first"), second = Mod("second");
string before = File.ReadAllText(Path.Combine(first, "1.7/Data/RimUnity/catalog.json"));
Failure(() => RimUnityPublisher.Export(profile, new[] { E(first), E(second, "failure") }, true, messages.Add), "converter failure aborts entire publication");
Check(Old(first) && Old(second) && File.ReadAllText(Path.Combine(first, "1.7/Data/RimUnity/catalog.json")) == before, "failure on second target leaves first catalog and both old versions untouched");
UnityEditor.EditorUtility.Cancel = true;
Failure(() => RimUnityPublisher.Export(profile, new[] { E(first) }, true, messages.Add), "user cancellation");
UnityEditor.EditorUtility.Cancel = false;
Check(Old(first), "cancel preserves live snapshot");
string blocked = Mod("blocked", false);
Directory.CreateDirectory(Path.Combine(blocked, "1.7/Data"));
File.WriteAllText(Path.Combine(blocked, "1.7/Data/RimUnity"), "publication collision");
Failure(() => RimUnityPublisher.Export(profile, new[] { E(first), E(blocked) }, true, messages.Add), "second-target rename failure");
Check(Old(first) && File.ReadAllText(Path.Combine(first, "1.7/Data/RimUnity/catalog.json")) == before, "publication rollback restores first target");
using (var held = new FileStream(Path.Combine(first, "1.7/Data/.rimunity-publish.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    Failure(() => RimUnityPublisher.Export(profile, new[] { E(first) }, true, messages.Add), "concurrent publisher rejected");
string validate = Mod("validate"); string vroot = Path.Combine(validate, "1.7/Data/RimUnity");
File.WriteAllText(Path.Combine(vroot, "old.json"), "{\"members\":[{\"name\":\"file\",\"value\":{\"kind\":\"string\",\"text\":\"old-sidecar.png\"}}]}");
RimUnityPublisher.Validate(vroot);
Check(true, "content-root sidecar path validated");
File.Delete(Path.Combine(vroot, "old-sidecar.png"));
Failure(() => RimUnityPublisher.Validate(vroot), "missing sidecar rejected");
Directory.CreateDirectory(Path.Combine(vroot, "exports/shader"));
foreach (string file in new[] { "shader.vert.glsl", "shader.frag.glsl", "shader.json" }) File.WriteAllText(Path.Combine(vroot, "exports/shader", file), "test");
File.WriteAllText(Path.Combine(vroot, "old.json"), "{\"vertexFile\":\"exports/shader/shader.vert.glsl\",\"fragmentFile\":\"exports/shader/shader.frag.glsl\",\"descriptorFile\":\"exports/shader/shader.json\"}");
RimUnityPublisher.Validate(vroot);
Check(true, "Shader stage and descriptor paths use content root");
File.WriteAllText(Path.Combine(vroot, "old.json"), "{\"vertexFile\":\"../escape.vert\"}");
Failure(() => RimUnityPublisher.Validate(vroot), "path traversal rejected");
profile.disableCastShadows = profile.disableReceiveShadows = profile.disableLightProbes = profile.disableReflectionProbes = true;
RimUnityPublisher.Export(profile, new[] { E(Mod("renderer-options")) }, true, messages.Add);
Check(PrefabExporter.LastOptions.disableCastShadows && PrefabExporter.LastOptions.disableReceiveShadows
    && PrefabExporter.LastOptions.disableLightProbes && PrefabExporter.LastOptions.disableReflectionProbes,
    "all renderer export policies reach the framework exporter");
profile.disableCastShadows = profile.disableReceiveShadows = profile.disableLightProbes = profile.disableReflectionProbes = false;
RimUnityPublisher.Export(profile, new[] { E(Mod("strict-options")) }, true, messages.Add);
Check(!PrefabExporter.LastOptions.disableCastShadows && !PrefabExporter.LastOptions.disableReceiveShadows
    && !PrefabExporter.LastOptions.disableLightProbes && !PrefabExporter.LastOptions.disableReflectionProbes,
    "disabled policies remain strict on the next export");
//压缩与依赖共享共同执行时，独立模组和增量副本必须仍能独立读取。
string baseMod = Mod("shared-base"), addon = Mod("shared-addon"), unrelated = Mod("unrelated");
File.WriteAllText(Path.Combine(addon, "About/About.xml"), "<ModMetaData><packageId>tests.shared-addon</packageId><modDependencies><li><packageId>tests.shared-base</packageId></li></modDependencies></ModMetaData>");
var stages = new Dictionary<string, string>();
foreach (string mod in new[] { baseMod, addon, unrelated })
{
    string source = Path.Combine(mod, "1.7/Data/RimUnity");
    var manifest = JObject.Parse(File.ReadAllText(Path.Combine(source, "catalog.json")));
    manifest["owner"] = ExportSharing.Owner(mod);
    ((JArray)manifest["assets"]).Add(new JObject { ["id"] = "dense", ["type"] = "Prefab", ["file"] = "dense.json", ["path"] = "dense", ["dependencies"] = new JArray() });
    File.WriteAllText(Path.Combine(source, "catalog.json"), manifest.ToString());
    File.WriteAllText(Path.Combine(source, "dense.json"), new JObject { ["enabled"] = false, ["count"] = 0, ["text"] = new string('x', 12000) }.ToString());
    string target = Path.Combine(run, "prepared-" + Path.GetFileName(mod));
    ExportStorage.CopyForPublish(source, target);
    stages.Add(mod, target);
}
ExportSharing.Share(stages);
var ownerRoots = stages.ToDictionary(p => ExportSharing.Owner(p.Key), p => p.Value);
foreach (string rootPath in stages.Values) RimUnityPublisher.Validate(rootPath, owner => ownerRoots[owner]);
var addonCatalog = ExportStorage.Read(Path.Combine(stages[addon], "catalog.json"));
Check(((JObject)addonCatalog["sharedFiles"]).Count == 1 && Directory.GetFiles(Path.Combine(stages[addon], "blobs")).Length == 0, "required dependency shares payload");
Check(Directory.GetFiles(Path.Combine(stages[unrelated], "blobs")).Length == 1, "unrelated mod retains its payload");
string denseFile = (string)((JArray)addonCatalog["assets"]).Single(a => (string)a["id"] == "dense")["file"];
var dense = ExportStorage.Read(Path.Combine(stages[addon], denseFile));
Check(denseFile.EndsWith(".json.gz") && (bool)dense["enabled"] == false && (int)dense["count"] == 0 && ((string)dense["text"]).Length == 12000, "compressed descriptor preserves explicit values");
Failure(() => RimUnityPublisher.Validate(stages[addon]), "missing owner resolver reports dependency error");
string standalone = Path.Combine(run, "standalone-copy");
ExportStorage.CopyForPublish(stages[addon], standalone, owner => ownerRoots[owner]);
RimUnityPublisher.Validate(standalone);
Check(((JObject)ExportStorage.Read(Path.Combine(standalone, "catalog.json"))["sharedFiles"]).Count == 0, "repacking shared compressed input produces self-contained copy");
Console.WriteLine("All publication tests passed. Scratch data: " + run);
