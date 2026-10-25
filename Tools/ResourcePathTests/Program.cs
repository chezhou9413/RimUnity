using System.IO.Compression;
using RimUnity;
using RimUnity.Embedded;

string original = Environment.CurrentDirectory;
string scratch = Path.Combine(Path.GetTempPath(), "RimUnityResourcePaths", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    Environment.CurrentDirectory = scratch;
    using var bytes = new MemoryStream();
    const string shader = "Shaders/RimUnity/HdrTransfer.frag.glsl";
    using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
    using (var writer = new StreamWriter(zip.CreateEntry(shader).Open())) writer.Write("shader fixture");
    var payload = new RuntimePayload { Bytes = bytes.ToArray(), Hash = "fixture" };
    string relative = Path.Combine("Configs", "RimUnity", "Embedded");
    for (int attempt = 0; attempt < 2; attempt++)
    {
        string root = payload.Extract(relative);
        if (!Path.IsPathFullyQualified(root)) throw new Exception("Extraction returned a relative root");
        RuntimeInfo.Configure(Path.GetRelativePath(scratch, root), false, "fixture", 1, "fixture");
        if (!Path.IsPathFullyQualified(RuntimeInfo.ResourceRoot)) throw new Exception("Runtime root is relative");
        string path = Path.Combine(RuntimeInfo.ResourceRoot, shader);
        Environment.CurrentDirectory = Path.GetTempPath();
        if (File.ReadAllText(path) != "shader fixture") throw new Exception("Shader resolution depends on working directory");
        Environment.CurrentDirectory = scratch;
    }
    Console.WriteLine("PASS: relative cache extraction, cache reuse, configured root, and shader reads after working-directory changes");
}
finally
{
    Environment.CurrentDirectory = original;
    Directory.Delete(scratch, true);
}
