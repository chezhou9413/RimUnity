using System.IO.Compression;
using Newtonsoft.Json.Linq;
using RimUnity.Editor;
using RimUnity.Serialization;
using Entry = RimUnityWorkflow.Entry;

internal static class CatalogArchiveCases
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "RimUnity", "CatalogArchiveTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string previousCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(root);
        try
        {
            var profile = new RimUnityWorkflow.Profile
            {
                repository = root,
                shaderTool = Path.Combine(root, "tool.exe"),
                gameRoot = root,
                packCatalogZip = true
            };
            File.WriteAllText(profile.shaderTool, "test");
            File.WriteAllText(Path.Combine(root, "Waylith.dll"), "test");

            string MakeMod(string name, string dependency = null)
            {
                string mod = Path.Combine(root, name);
                Directory.CreateDirectory(Path.Combine(mod, "About"));
                string dep = dependency == null ? "" : $"<modDependencies><li><packageId>{dependency}</packageId></li></modDependencies>";
                File.WriteAllText(Path.Combine(mod, "About/About.xml"), $"<ModMetaData><packageId>tests.{name}</packageId>{dep}</ModMetaData>");
                return mod;
            }

            //1. CopyForPublish + WriteArchiveFromDirectory 确定性输出与混合压缩策略（png Store，普通 json 压缩）
            string rawSource = Path.Combine(root, "raw-source");
            Directory.CreateDirectory(rawSource);
            File.WriteAllText(Path.Combine(rawSource, "tex.json"), "{\"members\":[{\"name\":\"file\",\"value\":{\"kind\":\"string\",\"text\":\"icon.png\"}}],\"padding\":\"" + new string('a', 2048) + "\"}");
            File.WriteAllBytes(Path.Combine(rawSource, "icon.png"), Enumerable.Range(0, 512).Select(i => (byte)(i * 31)).ToArray());
            File.WriteAllText(Path.Combine(rawSource, "catalog.json"), new JObject
            {
                ["formatVersion"] = 1,
                ["owner"] = "tests.raw",
                ["assets"] = new JArray(new JObject
                {
                    ["id"] = "tex",
                    ["type"] = "Texture2D",
                    ["file"] = "tex.json",
                    ["path"] = "Textures/Icon",
                    ["dependencies"] = new JArray()
                })
            }.ToString());

            string stageA = Path.Combine(root, "stage-a");
            string stageB = Path.Combine(root, "stage-b");
            string packedA = Path.Combine(root, "packed-a");
            string packedB = Path.Combine(root, "packed-b");
            ExportStorage.CopyForPublish(rawSource, stageA);
            ExportStorage.CopyForPublish(rawSource, stageB);
            string zipA = Path.Combine(packedA, ContentFiles.ARCHIVE_FILE_NAME);
            string zipB = Path.Combine(packedB, ContentFiles.ARCHIVE_FILE_NAME);
            ExportStorage.WriteArchiveFromDirectory(stageA, zipA);
            ExportStorage.WriteArchiveFromDirectory(stageB, zipB);
            _Check(Directory.GetFiles(packedA).Select(Path.GetFileName).SequenceEqual(new[] { ContentFiles.ARCHIVE_FILE_NAME }), "archive output emits only catalog.zip");
            _Check(File.ReadAllBytes(zipA).SequenceEqual(File.ReadAllBytes(zipB)), "WriteArchiveFromDirectory produces byte-identical deterministic catalog.zip");
            using (var archive = ZipFile.OpenRead(zipA))
            {
                var pngEntry = archive.Entries.Single(e => e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                var jsonEntry = archive.Entries.Single(e => e.FullName == "tex.json");
                _Check(pngEntry.CompressedLength == pngEntry.Length, "precompressed png stays stored without recompression");
                _Check(jsonEntry.CompressedLength < jsonEntry.Length, "plain json entry is deflate-compressed");
            }
            RimUnityPublisher.Validate(packedA);

            //从 catalog.zip 再次 CopyForPublish 回到散目录
            string unpacked = Path.Combine(root, "unpacked-from-zip");
            ExportStorage.CopyForPublish(packedA, unpacked);
            RimUnityPublisher.Validate(unpacked);
            _Check(File.Exists(Path.Combine(unpacked, ContentFiles.CATALOG_FILE_NAME)), "CopyForPublish accepts catalog.zip source");

            //同时存在 catalog.json 与 catalog.zip 时拒绝
            string ambiguous = Path.Combine(root, "ambiguous");
            Directory.CreateDirectory(ambiguous);
            File.Copy(zipA, Path.Combine(ambiguous, ContentFiles.ARCHIVE_FILE_NAME));
            File.WriteAllText(Path.Combine(ambiguous, ContentFiles.CATALOG_FILE_NAME), "{\"formatVersion\":2,\"assets\":[]}");
            _ExpectFailure(() => RimUnityPublisher.Validate(ambiguous), "ambiguous directory with both catalog.json and catalog.zip rejected");

            //2. Publisher 全量与增量发布 catalog.zip
            var messages = new List<string>();
            string modA = MakeMod("zipmod");
            string liveA = Path.Combine(modA, "1.7/Data/RimUnity");
            RimUnityPublisher.Export(profile, new[] { new Entry { mod = modA, path = "first", status = "pending", bundle = "test" } }, true, messages.Add);
            _Check(Directory.GetFiles(liveA, "*", SearchOption.AllDirectories).Select(Path.GetFileName).SequenceEqual(new[] { ContentFiles.ARCHIVE_FILE_NAME }),
                "publisher full rebuild emits only catalog.zip");
            RimUnityPublisher.Validate(liveA);

            //从已有 catalog.zip 增量追加入口
            RimUnityPublisher.Export(profile, new[] { new Entry { mod = modA, path = "second", status = "pending", bundle = "test" } }, false, messages.Add);
            RimUnityPublisher.Validate(liveA);
            var history = new ExportHistory { full = false, targets = new[] { liveA }, inputs = new[] { "second" }, status = "成功", finished = DateTime.Now };
            history.CaptureOutputs();
            _Check(history.outputs.Single().files.SequenceEqual(new[] { ContentFiles.ARCHIVE_FILE_NAME }), "ExportHistory lists catalog.zip as physical file");
            _Check(history.outputs.Single().assets.Length == 2, "incremental catalog.zip preserves prior entry and adds new entry");

            //ExportChangeTracker 对 catalog.zip 的输出哈希与 packCatalogZip 开关变更
            profile.mappings = new[] { new RimUnityWorkflow.Mapping { bundle = "test", mod = modA } };
            var entries = new[] { new Entry { mod = modA, path = "first", status = "pending", bundle = "test" } };
            var deps = new Dictionary<string, string[]> { ["first"] = Array.Empty<string>() };
            var snapshots = ExportChangeTracker.Capture(profile, entries, Array.Empty<string>(), deps, p => "h:" + p, _ => false, p => "f:" + p);
            string ReadOutput(ExportChangeTracker.Snapshot snapshot)
            {
                using var reader = new ExportChangeTracker.OutputReader(profile);
                return reader.Read(snapshot);
            }
            string signature = ReadOutput(snapshots[0]);
            var state = new ExportChangeTracker.State();
            ExportChangeTracker.Commit(state, snapshots, ReadOutput, true, Path.Combine(root, "baseline.json"));
            var loaded = ExportChangeTracker.Load(Path.Combine(root, "baseline.json"));
            var cleanTracker = new ExportChangeTracker(loaded, snapshots, ReadOutput);
            _Check(cleanTracker.Reasons.Count == 0 && !string.IsNullOrEmpty(signature), "ExportChangeTracker reports no change on matching catalog.zip");

            profile.packCatalogZip = false;
            var switchedSnapshots = ExportChangeTracker.Capture(profile, entries, Array.Empty<string>(), deps, p => "h:" + p, _ => false, p => "f:" + p);
            var switchedTracker = new ExportChangeTracker(loaded, switchedSnapshots, ReadOutput);
            _Check(switchedTracker.Reasons.ContainsKey("first"), "toggling packCatalogZip invalidates baseline context");
            profile.packCatalogZip = true;

            //3. 跨 Mod 共享（主包与扩展包均为 catalog.zip）
            string baseMod = MakeMod("zipbase");
            string addonMod = MakeMod("zipaddon", "tests.zipbase");
            foreach (string mod in new[] { baseMod, addonMod })
            {
                string src = Path.Combine(mod, "1.7/Data/RimUnity");
                Directory.CreateDirectory(src);
                File.WriteAllText(Path.Combine(src, "item.json"), "{\"members\":[{\"name\":\"file\",\"value\":{\"kind\":\"string\",\"text\":\"shared.png\"}}]}");
                File.WriteAllText(Path.Combine(src, "shared.png"), "shared-bytes");
                File.WriteAllText(Path.Combine(src, "catalog.json"), new JObject
                {
                    ["formatVersion"] = 1,
                    ["owner"] = ExportSharing.Owner(mod),
                    ["assets"] = new JArray(new JObject
                    {
                        ["id"] = "item",
                        ["type"] = "Texture2D",
                        ["file"] = "item.json",
                        ["path"] = "Textures/Shared",
                        ["dependencies"] = new JArray()
                    })
                }.ToString());
            }
            var prepared = new Dictionary<string, string>();
            foreach (string mod in new[] { baseMod, addonMod })
            {
                string stage = Path.Combine(root, "shared-stage-" + Path.GetFileName(mod));
                ExportStorage.CopyForPublish(Path.Combine(mod, "1.7/Data/RimUnity"), stage);
                prepared.Add(mod, stage);
            }
            ExportSharing.Share(prepared);
            var zippedRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in prepared)
            {
                string zipDir = Path.Combine(root, "shared-zip-" + Path.GetFileName(pair.Key));
                Directory.CreateDirectory(zipDir);
                ExportStorage.WriteArchiveFromDirectory(pair.Value, Path.Combine(zipDir, ContentFiles.ARCHIVE_FILE_NAME));
                zippedRoots.Add(ExportSharing.Owner(pair.Key), zipDir);
            }
            foreach (string packedRoot in zippedRoots.Values)
            {
                RimUnityPublisher.Validate(packedRoot, owner => zippedRoots[owner]);
            }
            _Check(true, "cross-mod sharedFiles validated when both owner and consumer use catalog.zip");

            //4. 安全边界：目录穿越与大小写重名条目均被拒绝
            string badTraversal = Path.Combine(root, "bad-traversal");
            Directory.CreateDirectory(badTraversal);
            using (var archive = ZipFile.Open(Path.Combine(badTraversal, ContentFiles.ARCHIVE_FILE_NAME), ZipArchiveMode.Create))
                using (var writer = new StreamWriter(archive.CreateEntry("../escape.json").Open())) writer.Write("{}");
            _ExpectFailure(() => RimUnityPublisher.Validate(badTraversal), "archive entry with path traversal rejected");

            string badDuplicate = Path.Combine(root, "bad-duplicate");
            Directory.CreateDirectory(badDuplicate);
            using (var archive = ZipFile.Open(Path.Combine(badDuplicate, ContentFiles.ARCHIVE_FILE_NAME), ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("catalog.json").Open())) writer.Write("{}");
                using (var writer = new StreamWriter(archive.CreateEntry("CATALOG.JSON").Open())) writer.Write("{}");
            }
            _ExpectFailure(() => RimUnityPublisher.Validate(badDuplicate), "archive entry with case-insensitive collision rejected");

            //5. 打包输出不得落入源目录内部；派生 CatalogReader 覆写 OpenRead 后 ReadAllBytes 与 ReadJson 一致
            _ExpectFailure(() => ExportStorage.WriteArchiveFromDirectory(stageA, Path.Combine(stageA, ContentFiles.ARCHIVE_FILE_NAME)),
                "WriteArchiveFromDirectory rejects output inside source directory");
            using (var customReader = new _OverrideCatalogReader(stageA))
            {
                _ExpectFailure(() => customReader.Exists("sub/../catalog.json"), "directory CatalogReader rejects parent-segment relative path");
                byte[] overriddenBytes = customReader.ReadAllBytes("catalog.json");
                JObject overriddenJson = customReader.ReadJson("catalog.json");
                _Check(System.Text.Encoding.UTF8.GetString(overriddenBytes).Contains("\"overridden\"", StringComparison.Ordinal)
                    && (bool)overriddenJson["overridden"],
                    "CatalogReader ReadAllBytes and ReadJson both route through overridden OpenRead");
            }
            _Check(ContentFiles.IsBlob("blobs/" + new string('a', 64) + ".png") && !ContentFiles.IsBlob("blobs/icon.png"),
                "ContentFiles.IsBlob distinguishes SHA256 blobs from plain blobs/* names");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCwd);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class _OverrideCatalogReader : ExportStorage.CatalogReader
    {
        internal _OverrideCatalogReader(string root) : base(root, null) { }

        public override Stream OpenRead(string relative)
            => new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"overridden\":true}"));
    }

    private static void _Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL catalog archive: " + name);
        Console.WriteLine("PASS catalog archive: " + name);
    }

    private static void _ExpectFailure(Action action, string name)
    {
        try { action(); }
        catch { Console.WriteLine("PASS catalog archive: " + name); return; }
        throw new Exception("Expected failure: " + name);
    }
}
