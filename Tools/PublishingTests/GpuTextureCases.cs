using System.Diagnostics;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using RimUnity.Editor;
using RimUnity.IO;
using RimUnity.Serialization;

internal static class GpuTextureCases
{
    internal static void Run(string[] actualFiles = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "RimUnity", "TextureTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (int format in new[] { 10, 12, 25, 26, 27 })
            {
                byte[] bytes = Fixture(format);
                string raw = Path.Combine(root, format + ".rutex"), packed = raw + ".gz";
                File.WriteAllBytes(raw, bytes);
                File.WriteAllBytes(packed, GpuTexturePayload.CompressIfUseful(bytes));
                Equal(GpuTexturePayload.Read(raw), GpuTexturePayload.Read(packed));
                Check(true, "exact format, dimensions, opacity and full mip chain: " + format);
            }
            var random = new byte[65536]; new Random(17).NextBytes(random);
            Check(GpuTexturePayload.CompressIfUseful(random) == null && GpuTexturePayload.CompressIfUseful(new byte[100]) == null, "tiny and incompressible payloads remain raw");
            byte[] fixture = Fixture(12);
            Failure(() => GpuTexturePayload.Read(new MemoryStream(fixture.Take(fixture.Length - 9).ToArray())), "truncated mip rejected");
            Failure(() => GpuTexturePayload.Read(new MemoryStream(fixture.Concat(new byte[] { 7 }).ToArray())), "trailing bytes rejected");
            var malformed = (byte[])fixture.Clone(); BitConverter.GetBytes(-1).CopyTo(malformed, 25);
            Failure(() => GpuTexturePayload.Read(new MemoryStream(malformed)), "invalid mip length rejected");
            malformed = (byte[])fixture.Clone(); BitConverter.GetBytes(99).CopyTo(malformed, 4);
            Failure(() => GpuTexturePayload.Read(new MemoryStream(malformed)), "unknown format version rejected");
            byte[] corrupt = GpuTexturePayload.CompressIfUseful(fixture); corrupt[corrupt.Length - 8] ^= 128;
            string bad = Path.Combine(root, "bad.rutex.gz"); File.WriteAllBytes(bad, corrupt);
            Failure(() => GpuTexturePayload.Read(bad), "gzip integrity failure rejected");
            string source = Path.Combine(root, "source"), output = Path.Combine(root, "output"), again = Path.Combine(root, "again");
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "texture.rutex"), fixture);
            File.WriteAllText(Path.Combine(source, "texture.json"), "{\"members\":[{\"name\":\"file\",\"value\":{\"kind\":\"string\",\"text\":\"texture.rutex\"}}]}");
            File.WriteAllText(Path.Combine(source, "catalog.json"), "{\"formatVersion\":2,\"assets\":[{\"id\":\"texture\",\"type\":\"Texture2D\",\"path\":\"texture\",\"file\":\"texture.json\",\"dependencies\":[]}]}");
            ExportStorage.CopyForPublish(source, output);
            string payload = (string)ExportStorage.Read(Path.Combine(output, "texture.json"))["members"][0]["value"]["text"];
            Check(payload.EndsWith(".rutex.gz"), "publication upgrades existing textures to compressed blobs");
            ContentFiles.RequireBlob(payload);
            Equal(GpuTexturePayload.Read(Path.Combine(source, "texture.rutex")), GpuTexturePayload.Read(Path.Combine(output, payload)));
            ExportStorage.CopyForPublish(output, again);
            string retained = (string)ExportStorage.Read(Path.Combine(again, "texture.json"))["members"][0]["value"]["text"];
            Check(retained == payload && File.ReadAllBytes(Path.Combine(output, payload)).SequenceEqual(File.ReadAllBytes(Path.Combine(again, retained))), "incremental repack preserves compressed bytes and content hash");
            string baseMod = Path.Combine(root, "base"), addon = Path.Combine(root, "addon");
            foreach (string mod in new[] { baseMod, addon }) Directory.CreateDirectory(Path.Combine(mod, "About"));
            File.WriteAllText(Path.Combine(baseMod, "About/About.xml"), "<ModMetaData><packageId>tests.gpu</packageId></ModMetaData>");
            File.WriteAllText(Path.Combine(addon, "About/About.xml"), "<ModMetaData><packageId>tests.addon</packageId><modDependencies><li><packageId>tests.gpu</packageId></li></modDependencies></ModMetaData>");
            foreach (var pair in new[] { (output, "tests.gpu"), (again, "tests.addon") })
            {
                var catalog = ExportStorage.Read(Path.Combine(pair.Item1, "catalog.json")); catalog["owner"] = pair.Item2;
                File.WriteAllText(Path.Combine(pair.Item1, "catalog.json"), catalog.ToString());
            }
            ExportSharing.Share(new Dictionary<string, string> { [baseMod] = output, [addon] = again });
            var sharedCatalog = ExportStorage.Read(Path.Combine(again, "catalog.json"));
            Check((string)sharedCatalog["sharedFiles"][payload] == "tests.gpu" && !File.Exists(Path.Combine(again, payload)), "compressed textures share between dependent mods");
            string materialized = Path.Combine(root, "materialized");
            ExportStorage.CopyForPublish(again, materialized, _ => output);
            Equal(GpuTexturePayload.Read(Path.Combine(output, payload)), GpuTexturePayload.Read(Path.Combine(materialized, payload)));
            Check(File.Exists(Path.Combine(materialized, payload)), "shared compressed texture can be repacked into a self-contained copy");
            if (actualFiles != null) Benchmark(actualFiles, root);
        }
        finally { Directory.Delete(root, true); }
    }
    private static byte[] Fixture(int format)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0x58545552); writer.Write(1); writer.Write(format); writer.Write(128); writer.Write(64); writer.Write(8); writer.Write(false);
        for (int mip = 0; mip < 8; mip++)
        {
            int length = ((Math.Max(1, 128 >> mip) + 3) / 4) * ((Math.Max(1, 64 >> mip) + 3) / 4) * (format == 10 || format == 26 ? 8 : 16);
            writer.Write(length); writer.Write(Enumerable.Range(0, length).Select(i => (byte)(i % 23)).ToArray());
        }
        return stream.ToArray();
    }
    private static void Equal(GpuTexturePayload.Data a, GpuTexturePayload.Data b)
    {
        if (a.width != b.width || a.height != b.height || a.format != b.format || a.mipCount != b.mipCount || a.opaque != b.opaque ||
            !a.levels.Zip(b.levels).All(p => p.First.SequenceEqual(p.Second))) throw new Exception("Decoded texture differs");
    }
    private static void Benchmark(string[] files, string root)
    {
        long before = 0, after = 0; var samples = new List<(string raw, string packed)>();
        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(file), packed = GpuTexturePayload.CompressIfUseful(bytes);
            before += bytes.Length; after += packed?.Length ?? bytes.Length;
            if (packed == null) continue;
            string path = Path.Combine(root, samples.Count + ".rutex.gz"); File.WriteAllBytes(path, packed);
            Equal(GpuTexturePayload.Read(file), GpuTexturePayload.Read(path)); samples.Add((file, path));
        }
        double Measure(bool compressed)
        {
            var times = new List<double>();
            for (int pass = 0; pass < 6; pass++)
            {
                GC.Collect(); var watch = Stopwatch.StartNew();
                foreach (var sample in samples) GpuTexturePayload.Read(compressed ? sample.packed : sample.raw);
                watch.Stop(); if (pass > 0) times.Add(watch.Elapsed.TotalMilliseconds);
            }
            return times.Order().ElementAt(2);
        }
        Console.WriteLine($"ACTUAL textures={files.Length}, compressed={samples.Count}, raw={before}, packed={after}, warm batch raw median={Measure(false):F2}ms, gzip median={Measure(true):F2}ms (5 samples, OS cache warm; excludes GPU/cache/game)");
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS GPU payload: " + name); }
    private static void Failure(Action action, string name)
    {
        try { action(); } catch (Exception error) when (error is IOException || error is InvalidDataException) { Check(true, name); return; }
        throw new Exception("Expected failure: " + name);
    }
}
