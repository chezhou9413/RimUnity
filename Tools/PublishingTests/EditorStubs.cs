using RimUnity.Editor;
using Newtonsoft.Json.Linq;

public static class RimUnityWorkflow
{
    public sealed class Profile { public string repository, shaderTool, gameRoot; public Mapping[] mappings; public bool disableCastShadows, disableReceiveShadows, disableLightProbes, disableReflectionProbes; }
    public sealed class Mapping { public string mod; }
    public sealed class Entry { public string path, mod, bundle, status, error; public bool ui; }
}
namespace UnityEngine { public sealed class Object { public string Path; } }
namespace UnityEditor
{
    public static class AssetDatabase { public static UnityEngine.Object LoadMainAssetAtPath(string path) => new() { Path = path }; }
    public static class EditorUtility
    {
        public static bool Cancel;
        public static bool DisplayCancelableProgressBar(string title, string text, float progress) => Cancel;
        public static void ClearProgressBar() { }
    }
    public static class EditorPrefs
    {
        private static readonly Dictionary<string, string> Values = new();
        public static string GetString(string key, string defaultValue) => Values.GetValueOrDefault(key, defaultValue);
        public static void SetString(string key, string value) => Values[key] = value;
    }
}
namespace RimUnity.Editor
{
    public sealed class ExportOptions { public bool disableCastShadows, disableReceiveShadows, disableLightProbes, disableReflectionProbes; public Action<string> log; }
    public sealed class ExportRequest { public UnityEngine.Object asset; public bool ui; public string path, description; }
    public static class PrefabExporter
    {
        public static ExportOptions LastOptions;
        public static void Export(string mod, params ExportRequest[] requests) => Export(mod, new ExportOptions(), requests);
        public static void Export(string mod, ExportOptions options, params ExportRequest[] requests)
        {
            LastOptions = options;
            string root = Path.Combine(mod, "1.7/Data/RimUnity");
            Directory.CreateDirectory(root);
            string catalog = Path.Combine(root, "catalog.json");
            var json = File.Exists(catalog) ? JObject.Parse(File.ReadAllText(catalog)) : new JObject { ["formatVersion"] = 1, ["assets"] = new JArray() };
            json["owner"] = ExportSharing.Owner(mod);
            foreach (var request in requests)
            {
                if (request.asset.Path == "failure") throw new InvalidOperationException("Injected converter failure");
                string id = request.asset.Path;
                File.WriteAllText(Path.Combine(root, id + ".json"), "{}");
                ((JArray)json["assets"]).Add(new JObject { ["id"] = id, ["path"] = request.path, ["file"] = id + ".json", ["dependencies"] = new JArray() });
            }
            File.WriteAllText(catalog, json.ToString());
        }
    }
}
