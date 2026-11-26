using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;
using RimUnity.Editor;
using RimUnity.Serialization;

//离线整理已有导出文件；源目录保持原样，输出目录必须不存在。
bool packCatalogZip = args.Length > 0 && args[0] == "--catalog-zip";
string[] positional = packCatalogZip ? args.Skip(1).ToArray() : args;
if (positional.Length < 2) throw new ArgumentException("用法：ResourcePacker [--catalog-zip] <outputDirectory> <modRoot> [modRoot...]");
Console.OutputEncoding = new UTF8Encoding(false);
string output = Path.GetFullPath(positional[0]);
if (Directory.Exists(output) || File.Exists(output + ".zip")) throw new IOException("输出已存在：" + output);
string[] mods = positional.Skip(1).Select(Path.GetFullPath).ToArray();
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
    using var originalReader = ExportStorage.CatalogReader.OpenRoot(source);
    var originalCatalog = originalReader.ReadJson(ContentFiles.CATALOG_FILE_NAME);
    var originals = ((JArray)originalCatalog["assets"]).ToDictionary(asset => (string)asset["id"]);
    JObject ReadOriginal(string relative)
    {
        string owner = (string)originalCatalog["sharedFiles"]?[relative];
        if (owner == null) return originalReader.ReadJson(relative);
        ContentFiles.RequireBlob(relative);
        using var ownerReader = ExportStorage.CatalogReader.OpenRoot(sourceRoots[owner]);
        return ownerReader.ReadJson(relative);
    }
    string ResolveOutput(string relative)
    {
        string owner = (string)shared[relative];
        string file = ContentFiles.Inside(owner == null ? root : owners[owner], relative);
        if (!File.Exists(file)) throw new FileNotFoundException("资源载荷缺失：" + relative, file);
        return file;
    }
    foreach (var entry in (JArray)catalog["assets"])
    {
        string id = (string)entry["id"];
        //每份资源与原始描述逐项对照，压缩不得改变字段值。
        var original = originals[id];
        var data = ExportStorage.Read(ResolveOutput((string)entry["file"]));
        //文件路径可能被内容寻址重排；实际文件字节通过共享验证核对。
        if ((string)entry["type"] is "Prefab" or "Mesh" or "AnimationClip" or "RuntimeAnimatorController")
        {
            var originalData = ReadOriginal((string)original["file"]);
            foreach (var property in originalData.Descendants().OfType<JProperty>().Where(property => property.Value.Type == JTokenType.Null).ToArray())
            {
                property.Remove();
            }
            if (!JToken.DeepEquals(data, originalData)) throw new InvalidDataException("压缩前后资源不一致：" + id);
        }
        foreach (string field in new[] { "vertexFile", "fragmentFile", "descriptorFile" })
            if (data[field]?.Type == JTokenType.String) ResolveOutput((string)data[field]);
        if (data["members"] is JArray members)
            foreach (var member in members.OfType<JObject>())
                if ((string)member["name"] == "file") ResolveOutput((string)member["value"]["text"]);
        assets++;
    }
    foreach (var property in shared.Properties())
        if (!File.Exists(ContentFiles.Inside(owners[(string)property.Value], property.Name)))
            throw new FileNotFoundException("共享载荷缺失：" + property.Name);
}
if (packCatalogZip)
{
    foreach (string root in prepared.Values)
    {
        string archive = Path.Combine(root, ContentFiles.ARCHIVE_FILE_NAME);
        string staged = Path.Combine(Path.GetDirectoryName(root)!, ".pack-" + Guid.NewGuid().ToString("N") + ".zip");
        ExportStorage.WriteArchiveFromDirectory(root, staged);
        foreach (string file in Directory.GetFiles(root))
        {
            File.Delete(file);
        }
        foreach (string directory in Directory.GetDirectories(root))
        {
            Directory.Delete(directory, true);
        }
        File.Move(staged, archive);
    }
}
long after = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
ZipFile.CreateFromDirectory(output, output + ".zip", CompressionLevel.Optimal, false);
Console.WriteLine($"资源 {assets}，共享引用 {sharedCount}，原始 {before:N0} bytes，优化 {after:N0} bytes，ZIP {new FileInfo(output + ".zip").Length:N0} bytes{(packCatalogZip ? "（已封装 catalog.zip）" : "")}");
Console.WriteLine(output);
