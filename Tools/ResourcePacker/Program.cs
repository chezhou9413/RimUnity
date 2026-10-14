using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;
using RimUnity.Editor;
using RimUnity.Serialization;

//离线整理已有导出文件；源目录保持原样，输出目录必须不存在。
if (args.Length < 2) throw new ArgumentException("用法：ResourcePacker <outputDirectory> <modRoot> [modRoot...]");
Console.OutputEncoding = new UTF8Encoding(false);
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output) || File.Exists(output + ".zip")) throw new IOException("输出已存在：" + output);
string[] mods = args.Skip(1).Select(Path.GetFullPath).ToArray();
var sourceRoots = ExportSharing.Roots(mods);
var prepared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
long before = 0;
foreach (string mod in mods)
{
    string owner = ExportSharing.Owner(mod);
    string destination = ContentFiles.Inside(output, owner + "/1.7/Data/RimUnity");
    before += Directory.EnumerateFiles(sourceRoots[owner], "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    ExportStorage.CopyForPublish(sourceRoots[owner], destination, id => sourceRoots[id]);
    prepared.Add(mod, destination);
}
ExportSharing.Share(prepared);
var owners = prepared.ToDictionary(p => ExportSharing.Owner(p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase);
int assets = 0, sharedCount = 0;
foreach (var pair in prepared)
{
    string root = pair.Value;
    var catalog = ExportStorage.Read(ContentFiles.Inside(root, "catalog.json"));
    var shared = (JObject)catalog["sharedFiles"];
    sharedCount += shared.Count;
    string source = sourceRoots[ExportSharing.Owner(pair.Key)];
    var originalCatalog = ExportStorage.Read(ContentFiles.Inside(source, "catalog.json"));
    var originals = ((JArray)originalCatalog["assets"]).ToDictionary(a => (string)a["id"]);
    void CheckPayload(string relative)
    {
        string owner = (string)shared[relative];
        string file = ContentFiles.Inside(owner == null ? root : owners[owner], relative);
        if (!File.Exists(file)) throw new FileNotFoundException("资源载荷缺失：" + relative, file);
    }
    foreach (var entry in (JArray)catalog["assets"])
    {
        string id = (string)entry["id"];
        //每份资源与原始描述逐项对照，压缩不得改变字段值。
        var original = originals[id];
        var data = ExportStorage.Read(ContentFiles.Inside(root, (string)entry["file"]));
        var old = ExportStorage.Read(ContentFiles.Inside(source, (string)original["file"]));
        foreach (var property in old.Descendants().OfType<JProperty>().Where(p => p.Value.Type == JTokenType.Null).ToArray()) property.Remove();
        //文件路径可能被内容寻址重排；实际文件字节通过共享验证核对。
        if ((string)entry["type"] is "Prefab" or "Mesh" or "AnimationClip" or "RuntimeAnimatorController")
            if (!JToken.DeepEquals(data, old)) throw new InvalidDataException("压缩前后资源不一致：" + id);
        foreach (string field in new[] { "vertexFile", "fragmentFile", "descriptorFile" })
            if (data[field]?.Type == JTokenType.String) CheckPayload((string)data[field]);
        if (data["members"] is JArray members)
            foreach (var member in members.OfType<JObject>())
                if ((string)member["name"] == "file") CheckPayload((string)member["value"]["text"]);
        assets++;
    }
    foreach (var property in shared.Properties())
        if (!File.Exists(ContentFiles.Inside(owners[(string)property.Value], property.Name)))
            throw new FileNotFoundException("共享载荷缺失：" + property.Name);
}
long after = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
ZipFile.CreateFromDirectory(output, output + ".zip", CompressionLevel.Optimal, false);
Console.WriteLine($"资源 {assets}，共享引用 {sharedCount}，原始 {before:N0} bytes，优化 {after:N0} bytes，ZIP {new FileInfo(output + ".zip").Length:N0} bytes");
Console.WriteLine(output);
