using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimUnity.Serialization;

namespace RimUnity.Editor
{
    //按清单复制发布内容，附属文件按内容共享，不合并不同资源的对象身份。
    public static class ExportStorage
    {
        private static readonly HashSet<string> s_precompressedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gz", ".ogg", ".mp3", ".mp4", ".webm", ".zip"
        };

        private static readonly DateTimeOffset s_entryTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        //判断资源目录是否包含有效清单入口（散文件或 catalog.zip）。
        public static bool HasCatalog(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            return File.Exists(Path.Combine(root, ContentFiles.CATALOG_FILE_NAME))
                || File.Exists(Path.Combine(root, ContentFiles.ARCHIVE_FILE_NAME));
        }

        //同时存在 catalog.json 与 catalog.zip 时明确报错，防止读写错位。
        public static bool IsArchiveRoot(string root)
        {
            root = Path.GetFullPath(root);
            bool hasJson = File.Exists(Path.Combine(root, ContentFiles.CATALOG_FILE_NAME));
            bool hasZip = File.Exists(Path.Combine(root, ContentFiles.ARCHIVE_FILE_NAME));
            if (hasJson && hasZip)
                throw new InvalidDataException("资源目录同时包含 " + ContentFiles.CATALOG_FILE_NAME + " 与 " + ContentFiles.ARCHIVE_FILE_NAME + "：" + root);
            return hasZip;
        }

        //把已整理好的目录写成确定性 catalog.zip：已压缩载荷按 Store 存储，小 JSON 与明文文本按 Optimal 压缩。
        public static void WriteArchiveFromDirectory(string sourceDirectory, string archivePath)
        {
            sourceDirectory = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            archivePath = Path.GetFullPath(archivePath);
            if (!File.Exists(Inside(sourceDirectory, ContentFiles.CATALOG_FILE_NAME)))
                throw new FileNotFoundException("打包目录缺少清单：" + ContentFiles.CATALOG_FILE_NAME, Path.Combine(sourceDirectory, ContentFiles.CATALOG_FILE_NAME));
            string parent = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .Select(file => new { Full = file, Relative = ContentFiles.NormalizeRelativePath(file.Substring(sourceDirectory.Length + 1)) })
                .OrderBy(item => item.Relative, StringComparer.Ordinal)
                .ToArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in files)
            {
                Inside(sourceDirectory, item.Relative);
                if (!seen.Add(item.Relative))
                    throw new InvalidDataException("打包目录存在仅大小写不同的重复路径：" + item.Relative);
            }
            string pending = archivePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, false, Encoding.UTF8))
                {
                    foreach (var item in files)
                    {
                        string extension = Path.GetExtension(item.Relative);
                        CompressionLevel level = s_precompressedExtensions.Contains(extension)
                            ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                        var entry = archive.CreateEntry(item.Relative, level);
                        entry.LastWriteTime = s_entryTime;
                        using var input = File.OpenRead(item.Full);
                        using var target = entry.Open();
                        input.CopyTo(target);
                    }
                }
                if (File.Exists(archivePath)) File.Replace(pending, archivePath, null);
                else File.Move(pending, archivePath);
            }
            catch
            {
                if (File.Exists(pending)) File.Delete(pending);
                throw;
            }
        }

        //安全解包 catalog.zip 到目标目录，供增量构建和离线二次整理复用。
        public static void ExtractArchiveToDirectory(string archivePath, string destinationDirectory)
        {
            archivePath = Path.GetFullPath(archivePath);
            destinationDirectory = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(destinationDirectory) && Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
                throw new IOException("资源解包目标必须为空：" + destinationDirectory);
            Directory.CreateDirectory(destinationDirectory);
            using var reader = CatalogReader.OpenArchive(archivePath);
            if (!reader.Exists(ContentFiles.CATALOG_FILE_NAME))
                throw new InvalidDataException("归档缺少清单：" + archivePath);
            foreach (string relative in reader.ListEntries())
            {
                string output = Inside(destinationDirectory, relative);
                string folder = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                using var input = reader.OpenRead(relative);
                using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(target);
            }
        }

        //目标必须为空目录；源目录保持不变，支持散文件目录或 catalog.zip 输入，失败由发布事务处理。
        public static void CopyForPublish(string source, string destination, Func<string, string> ownerRoot = null)
        {
            source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase)
                || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("资源源目录与发布目录不能相同或互相包含。");
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new IOException("资源整理目标必须为空：" + destination);
            Directory.CreateDirectory(destination);
            var ownerReaders = new Dictionary<string, CatalogReader>(StringComparer.OrdinalIgnoreCase);
            using var sourceReader = CatalogReader.OpenRoot(source);
            try
            {
                var catalog = sourceReader.ReadJson(ContentFiles.CATALOG_FILE_NAME);
                var assets = (JArray)catalog["assets"];
                if (((int)catalog["formatVersion"] != 1 && (int)catalog["formatVersion"] != 2) || assets == null)
                    throw new InvalidDataException("资源清单格式无效。");
                var records = assets.Cast<JObject>().ToDictionary(a => (string)a["id"], StringComparer.Ordinal);
                var reachable = new HashSet<string>(StringComparer.Ordinal);
                void Visit(string id)
                {
                    if (!records.TryGetValue(id, out var record)) throw new InvalidDataException("缺少依赖：" + id);
                    if (!reachable.Add(id)) return;
                    foreach (string dependency in ((JArray)record["dependencies"]).Values<string>()) Visit(dependency);
                }
                foreach (var record in records.Values)
                    if (!((string)record["path"]).StartsWith("__assets/", StringComparison.Ordinal)) Visit((string)record["id"]);
                var payloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                CatalogReader ResolveReader(string relative)
                {
                    if (catalog["sharedFiles"] is JObject shared && shared[relative] != null)
                    {
                        ContentFiles.RequireBlob(relative);
                        string owner = (string)shared[relative];
                        if (ownerRoot == null) throw new InvalidDataException("整理共享资源需要提供所属模组目录：" + owner);
                        if (!ownerReaders.TryGetValue(owner, out var ownerReader))
                            ownerReaders.Add(owner, ownerReader = CatalogReader.OpenRoot(ownerRoot(owner)));
                        return ownerReader;
                    }
                    return sourceReader;
                }
                string CopyPayload(string relative)
                {
                    CatalogReader reader = ResolveReader(relative);
                    string key = reader.Describe(relative);
                    if (payloads.TryGetValue(key, out var cached)) return cached;
                    byte[] raw = reader.ReadAllBytes(relative);
                    byte[] packed = relative.EndsWith(".rutex", StringComparison.OrdinalIgnoreCase)
                        ? RimUnity.IO.GpuTexturePayload.CompressIfUseful(raw) : null;
                    byte[] bytes = packed ?? raw;
                    string hash;
                    using (var algorithm = SHA256.Create())
                        hash = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    string extension = packed != null || relative.EndsWith(".rutex.gz", StringComparison.OrdinalIgnoreCase)
                        ? ".rutex.gz" : Path.GetExtension(relative).ToLowerInvariant();
                    string target = "blobs/" + hash + extension;
                    string output = Inside(destination, target);
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    if (!File.Exists(output)) File.WriteAllBytes(output, bytes);
                    payloads.Add(key, target);
                    return target;
                }
                var kept = new JArray();
                foreach (var record in records.Values.Where(a => reachable.Contains((string)a["id"])))
                {
                    string relative = (string)record["file"];
                    var data = ResolveReader(relative).ReadJson(relative);
                    foreach (string field in new[] { "vertexFile", "fragmentFile" })
                        if (data[field]?.Type == JTokenType.String) data[field] = CopyPayload((string)data[field]);
                    if (data["descriptorFile"]?.Type == JTokenType.String)
                    {
                        string descriptorRelative = (string)data["descriptorFile"];
                        var descriptor = ResolveReader(descriptorRelative).ReadJson(descriptorRelative);
                        //离线描述中的阶段路径相对于描述文件，随共享程序一起更新。
                        descriptor["vertexFile"] = Path.GetFileName((string)data["vertexFile"]);
                        descriptor["fragmentFile"] = Path.GetFileName((string)data["fragmentFile"]);
                        byte[] bytes = Encoding.UTF8.GetBytes(descriptor.ToString(Formatting.None));
                        string hash;
                        using (var algorithm = SHA256.Create())
                            hash = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        string target = "blobs/" + hash + ".json";
                        string descriptorOutput = Inside(destination, target);
                        Directory.CreateDirectory(Path.GetDirectoryName(descriptorOutput));
                        if (!File.Exists(descriptorOutput)) File.WriteAllBytes(descriptorOutput, bytes);
                        data["descriptorFile"] = target;
                    }
                    if (data["members"] is JArray members)
                        foreach (var member in members.OfType<JObject>())
                            if ((string)member["name"] == "file" && member["value"] is JObject value)
                                value["text"] = CopyPayload((string)value["text"]);
                    //只移除 null，不移除 0、false 或空集合。
                    foreach (var property in data.Descendants().OfType<JProperty>().Where(p => p.Value.Type == JTokenType.Null).ToArray())
                        property.Remove();
                    if (relative.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase)) relative = relative.Substring(0, relative.Length - 3);
                    bool dense = new[] { "Prefab", "Mesh", "AnimationClip", "RuntimeAnimatorController" }.Contains((string)record["type"]);
                    byte[] encoded = Encoding.UTF8.GetBytes(data.ToString(Formatting.None));
                    if (dense && encoded.Length >= 4096)
                    {
                        using var buffer = new MemoryStream();
                        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, true)) gzip.Write(encoded, 0, encoded.Length);
                        if (buffer.Length < encoded.Length) { encoded = buffer.ToArray(); relative += ".gz"; }
                    }
                    record["file"] = relative;
                    string output = Inside(destination, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    File.WriteAllBytes(output, encoded);
                    kept.Add(record);
                }
                catalog["assets"] = kept;
                catalog["formatVersion"] = 2;
                catalog["sharedFiles"] = new JObject();
                ExportJson.Write(Path.Combine(destination, ContentFiles.CATALOG_FILE_NAME), catalog);
            }
            finally
            {
                foreach (var reader in ownerReaders.Values)
                {
                    reader.Dispose();
                }
            }
        }

        //逐文件读取，避免将整个资源目录同时留在内存中。
        internal static JObject Read(string path)
        {
            using var stream = new StreamReader(ContentFiles.OpenJson(path), Encoding.UTF8);
            using var reader = new JsonTextReader(stream) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        internal static JObject Read(Stream input, string name)
        {
            using var decoded = ContentFiles.OpenJson(input, name);
            using var stream = new StreamReader(decoded, Encoding.UTF8);
            using var reader = new JsonTextReader(stream) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        //统一读取散文件目录或 catalog.zip，供发布校验、变更追踪和离线打包共用。
        internal sealed class CatalogReader : IDisposable
        {
            private readonly string _root;
            private readonly string _archivePath;
            private readonly ZipArchive _archive;
            private readonly Dictionary<string, ZipArchiveEntry> _entries;

            private CatalogReader(string root, string archivePath)
            {
                _root = root;
                _archivePath = archivePath;
                if (archivePath == null) return;
                var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    _archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
                    _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in _archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)
                            && (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal)))
                            continue;
                        string normalized = ContentFiles.NormalizeRelativePath(entry.FullName);
                        if (_entries.ContainsKey(normalized))
                            throw new InvalidDataException("资源归档包含重复条目：" + entry.FullName);
                        _entries.Add(normalized, entry);
                    }
                }
                catch
                {
                    if (_archive != null) _archive.Dispose();
                    else stream.Dispose();
                    throw;
                }
            }

            internal static CatalogReader OpenRoot(string root)
            {
                root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (IsArchiveRoot(root)) return new CatalogReader(root, Inside(root, ContentFiles.ARCHIVE_FILE_NAME));
                return new CatalogReader(root, null);
            }

            internal static CatalogReader OpenArchive(string archivePath)
            {
                archivePath = Path.GetFullPath(archivePath);
                string parent = Path.GetDirectoryName(archivePath) ?? "";
                return new CatalogReader(parent, archivePath);
            }

            internal string Describe(string relative)
                => _archive != null
                    ? _archivePath + "!" + ContentFiles.NormalizeRelativePath(relative)
                    : Inside(_root, relative);

            internal bool Exists(string relative)
            {
                if (_archive != null) return _entries.ContainsKey(ContentFiles.NormalizeRelativePath(relative));
                return File.Exists(Inside(_root, relative));
            }

            internal Stream OpenRead(string relative)
            {
                if (_archive == null) return File.OpenRead(Inside(_root, relative));
                string normalized = ContentFiles.NormalizeRelativePath(relative);
                if (!_entries.TryGetValue(normalized, out var entry))
                    throw new FileNotFoundException("资源归档缺少条目：" + normalized, Describe(normalized));
                return entry.Open();
            }

            internal byte[] ReadAllBytes(string relative)
            {
                if (_archive == null) return File.ReadAllBytes(Inside(_root, relative));
                using var stream = OpenRead(relative);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }

            internal JObject ReadJson(string relative)
            {
                using var stream = OpenRead(relative);
                return Read(stream, relative);
            }

            internal string[] ListEntries()
            {
                if (_archive != null)
                    return _entries.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
                if (!Directory.Exists(_root)) return Array.Empty<string>();
                return Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(_root.Length + 1).Replace('\\', '/'))
                    .OrderBy(f => f, StringComparer.Ordinal).ToArray();
            }

            public void Dispose() => _archive?.Dispose();
        }

        //引用只能落在本资源根目录，拒绝通过链接跳出目录。
        private static string Inside(string root, string relative) => ContentFiles.Inside(root, relative);
    }
}
