using RimUnity.Editor;
using Newtonsoft.Json.Linq;

public static class RimUnityWorkflow
{
    public static string[] UiPaths = Array.Empty<string>();
    public static string[] LoadUiPaths() => UiPaths;
    public sealed class Profile { public string repository, shaderTool, gameRoot; public Mapping[] mappings; public ResourceOwner[] resourceOwners; public bool disableCastShadows, disableReceiveShadows, disableLightProbes, disableReflectionProbes; }
    public sealed class Mapping { public string mod, bundle; }
    public sealed class ResourceOwner { public string path, bundle; }
    public sealed class Entry { public string path, mod, bundle, status, error, type; public bool ui; }
}
namespace UnityEngine
{
    public static class Application { public static string unityVersion = "test"; }
    public class Object { public string Path; }
    public sealed class Shader : Object { }
    public sealed class Material : Object { public Shader shader; }
}
namespace UnityEditor
{
    public static class AssetDatabase {
        public static readonly Dictionary<string, string[]> Graph = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, string> Hashes = new(StringComparer.OrdinalIgnoreCase);
        public static string GetAssetDependencyHash(string path) => Hashes.GetValueOrDefault(path, path);
 public static string GetAssetPath(UnityEngine.Object asset) => asset?.Path ?? ""; public static UnityEngine.Object LoadMainAssetAtPath(string path) => path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ? new UnityEngine.Shader { Path = path } : new UnityEngine.Object { Path = path }; public static string AssetPathToGUID(string path) => ""; public static bool IsValidFolder(string path) => false; public static string[] GetDependencies(string[] paths, bool recursive) => paths.SelectMany(p => Graph.GetValueOrDefault(p, new[] { p, "Assets/shared.png", "Assets/code.cs" })).ToArray(); }
    public static class EditorUtility
    {
        public static readonly HashSet<string> Dirty = new(StringComparer.OrdinalIgnoreCase);
        public static bool IsDirty(UnityEngine.Object asset) => asset != null && Dirty.Contains(asset.Path);
        public static bool Cancel;
        public static bool DisplayCancelableProgressBar(string title, string text, float progress) => Cancel;
        public static void ClearProgressBar() { }
    }
    public static class EditorUserBuildSettings { public static string activeBuildTarget = "test"; }
    public static class TypeCache { public static Type[] Types = Array.Empty<Type>(); public static IEnumerable<Type> GetTypesWithAttribute<T>() => Types; }
    public static class EditorPrefs
    {
        private static readonly Dictionary<string, string> Values = new();
        public static string GetString(string key, string defaultValue) => Values.GetValueOrDefault(key, defaultValue);
        public static void SetString(string key, string value) => Values[key] = value;
    }
}
namespace RimUnity.Editor
{
    public interface IExportChangeFingerprint { string GetChangeFingerprint(); }
    public sealed class RimUnityExportProcessorAttribute : Attribute { }
    public sealed class ExportOptions { public string[] uiResourcePaths; public bool disableCastShadows, disableReceiveShadows, disableLightProbes, disableReflectionProbes; public Action<string> log; }
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
