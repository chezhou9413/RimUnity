using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

// Reads PE resources/metadata without loading or executing the published runtime.
internal static class ArtifactAudit
{
    internal static int Run(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("--audit <standalone root> <embedded container> <embedded references> <current root> <deployed root> <output directory>");
        var published = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[5]); Directory.CreateDirectory(output);
        using var peFile = File.OpenRead(args[1]); using var pe = new PEReader(peFile);
        var metadata = pe.GetMetadataReader();
        var resource = metadata.ManifestResources.Select(metadata.GetManifestResource)
            .Single(r => metadata.GetString(r.Name) == "RimUnity.Payload.zip" && r.Implementation.IsNil);
        var reader = pe.GetSectionData(pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress)
            .GetReader((int)resource.Offset, pe.PEHeaders.CorHeader.ResourcesDirectory.Size - (int)resource.Offset);
        byte[] payload = reader.ReadBytes(reader.ReadInt32());
        using var zip = new ZipArchive(new MemoryStream(payload));
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("Assemblies/") || e.FullName.StartsWith("Shaders/")))
        {
            using var source = entry.Open();
            string hash = Convert.ToHexString(SHA256.HashData(source)); hashes[entry.FullName] = hash;
            var standaloneFile = Path.Combine(published, "1.7", entry.FullName);
            if (!File.Exists(standaloneFile) || Hash(standaloneFile) != hash)
                throw new InvalidDataException("Published standalone/embedded mismatch: " + entry.FullName);
            if (entry.FullName.StartsWith("Assemblies/"))
            {
                var reference = Path.Combine(args[2], Path.GetFileName(entry.FullName));
                if (Hash(reference) != hash) throw new InvalidDataException("Published embedded reference mismatch: " + reference);
            }
        }
        var files = hashes.Keys.Select(name => new {
            File = name, Published = hashes[name],
            Current = OptionalHash(Path.Combine(args[3], "1.7", name)),
            Deployed = OptionalHash(Path.Combine(args[4], "1.7", name)),
            CurrentTextEqual = name.EndsWith(".glsl") ? SameText(Path.Combine(published, "1.7", name), Path.Combine(args[3], "1.7", name)) : (bool?)null,
            DeployedTextEqual = name.EndsWith(".glsl") ? SameText(Path.Combine(published, "1.7", name), Path.Combine(args[4], "1.7", name)) : (bool?)null
        }).ToArray();
        var report = new { Utc = DateTime.UtcNow, PayloadHash = Convert.ToHexString(SHA256.HashData(payload)),
            Runtime = ReadRuntime(zip), VerifiedPublishedFiles = hashes.Count, Files = files };
        string reportFile = Path.Combine(output, "release-audit.json");
        File.WriteAllText(reportFile, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS {hashes.Count} published DLL/shader hashes match standalone, embedded payload and references.");
        foreach (var file in files.Where(f => f.File.StartsWith("Assemblies/") || f.CurrentTextEqual == false || f.DeployedTextEqual == false))
            Console.WriteLine($"{file.File}: current={(file.Current == file.Published || file.CurrentTextEqual == true ? "same" : "different")}; deployed={(file.Deployed == file.Published || file.DeployedTextEqual == true ? "same" : "different")}; published={file.Published}");
        Console.WriteLine("AUDIT " + reportFile);
        return 0;
    }
    private static object ReadRuntime(ZipArchive zip)
    { using var stream = zip.GetEntry("runtime.json").Open(); using var json = JsonDocument.Parse(stream); return json.RootElement.Clone(); }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static string OptionalHash(string path) => File.Exists(path) ? Hash(path) : "missing";
    private static bool SameText(string a, string b) => File.Exists(b)
        && File.ReadAllText(a).Replace("\r\n", "\n").TrimStart('\uFEFF') == File.ReadAllText(b).Replace("\r\n", "\n").TrimStart('\uFEFF');
}
