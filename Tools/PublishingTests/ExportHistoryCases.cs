using Newtonsoft.Json;
using RimUnity.Editor;

internal static class ExportHistoryCases
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "RimUnity", "HistoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "catalog.json"), """
                {"assets":[
                  {"id":"old","path":"old.prefab","type":"Prefab","file":"old.json"},
                  {"id":"new","path":"new.prefab","sourcePath":"Assets/new.prefab","type":"Prefab","file":"new.json"}
                ],"sharedFiles":{"new.json":"tests.owner"}}
                """);
            File.WriteAllText(Path.Combine(root, "old.json"), "{}");
            var record = new ExportHistory { full = false, targets = new[] { root }, inputs = new[] { "Assets/new.prefab" }, status = "成功", finished = DateTime.Now };
            record.CaptureOutputs();
            Check(record.outputs.Single().assets.Length == 2, "incremental output includes retained entries");
            Check(record.outputs[0].assets[1].Contains("Assets/new.prefab") && record.outputs[0].assets[1].Contains("tests.owner"), "output records source and shared owner");
            Check(record.outputs[0].files.SequenceEqual(new[] { "catalog.json", "old.json" }), "physical files are distinct from shared catalog references");
            string saved = JsonConvert.SerializeObject(record);
            File.WriteAllText(Path.Combine(root, "catalog.json"), "{\"assets\":[]}");
            var restored = JsonConvert.DeserializeObject<ExportHistory>(saved);
            Check(restored.outputs[0].assets.Length == 2 && !restored.full && restored.status == "成功", "historical output is immutable after later publication and reload");
            record.targets = new[] { root, Path.Combine(root, "missing") };
            try { record.CaptureOutputs(); throw new Exception("Expected missing catalog"); }
            catch (DirectoryNotFoundException) { }
            Check(record.outputs[0].assets.Length == 2, "snapshot read failure does not expose a partial new snapshot");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL export history: " + name);
        Console.WriteLine("PASS export history: " + name);
    }
}
