using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using RimUnity.Diagnostics;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

var sources = new[] {
    new MemoryAttribution.Source("nivarian", "涅瓦莲", "D:/Mods/Nivarian"),
    new MemoryAttribution.Source("dlc", "DLC", "D:/Game/Data/Biotech")
};
Check(MemoryAttribution.Resolve("D:\\Mods\\Nivarian\\Textures\\a.png", "GameTexture", sources).Owner.Contains("[nivarian]"), "absolute file owner");
Check(MemoryAttribution.Resolve("Nivarian/Textures/a.png", "GameTexture", sources).Owner.Contains("[nivarian]"), "host relative content-root label");
Check(MemoryAttribution.Resolve("NivarianExtra/Textures/a.png", "GameTexture", sources).Owner == MemoryAttribution.Unknown, "prefix collision is not attributed");
Check(MemoryAttribution.Resolve("D:/Mods/Nivarian/../Other/a.png", "GameTexture", sources).Owner == MemoryAttribution.Unknown, "relative escape is not attributed");
Check(MemoryAttribution.Resolve("a.png", "GameTexture", sources).Owner == MemoryAttribution.Unknown, "short filename stays unknown");
var ambiguous = sources.Concat(new[] { new MemoryAttribution.Source("other", "Other", "E:/Mods/Nivarian") }).ToArray();
Check(MemoryAttribution.Resolve("Nivarian/Textures/a.png", "GameTexture", ambiguous).Owner == MemoryAttribution.Unknown, "duplicate root names stay unknown");
Check(MemoryAttribution.Resolve("D:/Mods/Nivarian/Textures/a.png", "GameTexture", ambiguous).Owner.Contains("[nivarian]"), "absolute path resolves duplicate basenames");
Check(MemoryAttribution.Resolve("TextureAtlas_Building", "Atlas", sources).Owner == MemoryAttribution.Shared, "mixed atlas stays shared");
Check(MemoryAttribution.Resolve("Resources/Textures/a.png", "GameTexture", sources).Owner == MemoryAttribution.Builtin, "builtin asset is not charged to triggering mod");
var nested = sources.Concat(new[] { new MemoryAttribution.Source("nested", "Nested", "D:/Mods/Nivarian/Sub") }).ToArray();
Check(MemoryAttribution.Resolve("D:/Mods/Nivarian/Sub/a.png", "GameTexture", nested).Owner.Contains("[nested]"), "most specific absolute root wins");

var rows = Enumerable.Range(0, 22197).Select(i => new MemoryAttribution.Row(i % 2 == 0 ? "A" : "B", "asset" + i,
    "GameTexture", i + 1, i % 3 == 0 ? "设备本地" : i % 3 == 1 ? "系统内存" : "未知", "test", "", "image:" + i)).ToArray();
var snapshot = new MemoryAttribution.Snapshot(DateTime.UtcNow, 0, rows, [], []);
var groups = snapshot.Groups;
Check(groups.Sum(x => x.Count) == 22197 && groups.Sum(x => x.Bytes) == snapshot.Total, "full allocation reconciliation beyond largest 150");
Check(groups.All(x => x.Device + x.Host + x.UnknownPlacement == x.Bytes), "unknown placement is conserved and not charged to VRAM");
Check(JsonDocument.Parse(JsonSerializer.Serialize(new { snapshot.Rows, Groups = groups })).RootElement.GetProperty("Rows").GetArrayLength() == 22197, "export retains all allocations");

if (args.Length > 0)
{
    string game = Path.GetFullPath(args[0]);
    AssemblyLoadContext.Default.Resolving += (c, n) => {
        string path = Path.Combine(game, n.Name + ".dll");
        return File.Exists(path) ? c.LoadFromAssemblyPath(path) : null;
    };
    var host = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Waylith.dll"));
    const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
    foreach (var (typeName, fieldName) in new[] { ("TextureResourceManager", "LiveAllocations"), ("TextureResidency", "Registry") })
    {
        var type = host.GetType("Waylith.Rendering." + typeName, true);
        Check(type.GetField("Gate", flags)?.FieldType.FullName == "System.Threading.Lock", typeName + " exact host lock API");
        Check(type.GetField(fieldName, flags)?.FieldType.GetGenericTypeDefinition() == typeof(List<>), typeName + " weak registry API");
    }
    var gpu = host.GetType("Waylith.Rendering.GpuTextureAllocation", true);
    Check(gpu.GetProperty("IsHostResident")?.PropertyType == typeof(bool), "host placement evidence API");
}
Console.WriteLine("All memory attribution checks passed.");
