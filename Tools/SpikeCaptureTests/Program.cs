using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using RimUnity.Diagnostics;

try
{
int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}
var data = new SpikeCaptureData();
var parent = data.Begin("load", "prefab", 10);
var child = data.Begin("compile", "shader", 10);
Thread.SpinWait(10000);
data.End(child, "compilerCacheHit=False");
data.End(parent);
var s = data.Read();
var p = s.Events.Single(e => e.Stage == "load");
var c = s.Events.Single(e => e.Stage == "compile");
Check(c.Parent == p.Id, "Nested spans must preserve parent IDs.");
Check(p.DurationMs >= c.DurationMs && Math.Abs(p.SelfMs - (p.DurationMs - c.DurationMs)) < .1, "Self time must subtract measured children once.");
Check(s.InFlight == 0 && s.Events[0].Id < s.Events[1].Id, "Events are exported in start order, not completion order.");
data.End(parent);
Check(data.Read().Groups.Sum(g => g.Calls) == 2, "Duplicate finalization cannot count a span twice.");
var fault = data.Begin("spawn", "bad", 11);
data.End(fault, "exception:synthetic failure");
Check(data.Read().Groups.Single(g => g.Stage == "spawn").Errors == 1, "Exceptions must appear in aggregates.");

var parallel = new SpikeCaptureData();
Parallel.For(0, 8, worker =>
{
    for (int i = 0; i < 100; i++)
    {
        var a = parallel.Begin("worker", worker.ToString(), i);
        var b = parallel.Begin("child", worker.ToString(), i);
        parallel.End(b); parallel.End(a);
    }
});
var ps = parallel.Read();
Check(ps.Groups.Sum(g => g.Calls) == 1600 && ps.InFlight == 0, "Concurrent threads must retain all aggregate counts.");
Check(ps.Events.Where(e => e.Stage == "child").All(e => ps.Events.Any(p => p.Id == e.Parent && p.Thread == e.Thread)), "Parents must never cross threads.");

var closed = new SpikeCaptureData();
var pending = closed.Begin("pending", "old session", 1);
closed.Close();
Check(closed.Read().InFlight == 1 && closed.Begin("late", "", 2) == null, "Stop must reject new work and expose in-flight spans.");
var replacement = new SpikeCaptureData();
var fresh = replacement.Begin("fresh", "new session", 2); replacement.End(fresh);
closed.End(pending);
Check(replacement.Read().Events.Single().Parent == 0 && closed.Read().Groups.Length == 0 && closed.Read().InFlight == 0,
    "Late old-session completions cannot contaminate a replacement capture.");

var bounded = new SpikeCaptureData();
for (int i = 0; i < SpikeCaptureData.MaxGroups + 20; i++) { var t = bounded.Begin("unique", i.ToString(), 0); bounded.End(t); }
for (int i = 0; i < SpikeCaptureData.MaxEvents + 20; i++) bounded.Mark("marker", 0);
for (int i = 0; i < SpikeCaptureData.MaxFrames + 20; i++) bounded.AddFrame(new(0, i, 1, 0, 0, 0, 0, 0, 0, false));
var bs = bounded.Read();
Check(bs.Groups.Length == SpikeCaptureData.MaxGroups && bs.OmittedGroups == 20, "Group cardinality is bounded and rejected calls are disclosed.");
Check(bs.Events.Length == SpikeCaptureData.MaxEvents && bs.OmittedEvents > 0, "Event retention is bounded and truncation is disclosed.");
Check(bs.Frames.Length == SpikeCaptureData.MaxFrames && bs.OmittedFrames == 20, "Frame retention is bounded and truncation is disclosed.");
var copied = data.Read(); copied.Groups[0].Calls = -1;
Check(data.Read().Groups.All(g => g.Calls > 0), "Snapshots must not expose mutable live aggregates.");
Console.WriteLine($"PASS: {checks} recorder checks.");

// Optional real-assembly smoke check: resolves managed game assemblies, installs probes without running a scene,
// verifies native out-parameter/byref signatures are patchable, then verifies all our patches are removed.
if (args.Length >= 3)
{
    string assemblyPath = Path.GetFullPath(args[0]), gameDir = Path.GetFullPath(args[1]), harmonyPath = Path.GetFullPath(args[2]);
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        foreach (string directory in new[] { Path.GetDirectoryName(assemblyPath), gameDir, Path.GetDirectoryName(harmonyPath) })
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
        }
        return null;
    };
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    var capture = assembly.GetType("RimUnity.Diagnostics.ParticleSpikeCapture", true);
    const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
    var install = capture.GetMethod("InstallProbes", flags);
    var remove = capture.GetMethod("RemoveProbes", flags);
    try
    {
        install.Invoke(null, null);
        var coverage = (IEnumerable<string>)capture.GetField("coverage", flags).GetValue(null);
        foreach (string line in coverage) Console.WriteLine(line);
        Check(!coverage.Any(c => c.StartsWith("FAILED") || c.StartsWith("MISSING")), "Every expected probe must install against the current host assembly.");
        int count = ((System.Collections.ICollection)capture.GetField("installed", flags).GetValue(null)).Count;
        Check(count >= 40, "Expected broad loading, simulation and native rendering coverage.");
        Console.WriteLine($"PASS: {count} real-assembly probes installed.");

        // Exercise an actual patched resource entry without constructing engine resources or a game.
        var catalogType = assembly.GetType("RimUnity.Assets.AssetCatalog", true);
        var catalog = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(catalogType);
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        var manifestField = catalogType.GetField("Manifest", instanceFlags);
        var manifest = Activator.CreateInstance(manifestField.FieldType);
        manifestField.FieldType.GetField("owner").SetValue(manifest, "test.owner");
        manifestField.SetValue(catalog, manifest);
        foreach (string field in new[] { "records", "resources", "loading" })
        {
            var f = catalogType.GetField(field, instanceFlags); f.SetValue(catalog, Activator.CreateInstance(f.FieldType));
        }
        var resourceCache = (System.Collections.IDictionary)catalogType.GetField("resources", instanceFlags).GetValue(catalog);
        resourceCache.Add("cached", null);
        var sessionType = assembly.GetType("RimUnity.Diagnostics.SpikeCaptureData", true);
        var session = Activator.CreateInstance(sessionType, true);
        capture.GetField("current", flags).SetValue(null, session);
        try
        {
            var get = catalogType.GetMethod("Get", instanceFlags);
            Check(get.Invoke(catalog, new object[] { "cached" }) == null, "Cache-hit result must be unchanged by probes.");
            try { get.Invoke(catalog, new object[] { "unknown" }); throw new Exception("Expected unknown resource failure."); }
            catch (TargetInvocationException e) { Check(e.InnerException is InvalidDataException, "Original resource exception must propagate unchanged."); }
            var snapshot = sessionType.GetMethod("Read", instanceFlags).Invoke(session, null);
            var events = ((Array)snapshot.GetType().GetProperty("Events").GetValue(snapshot)).Cast<object>().ToArray();
            Check(events.Length == 2, "Real patched entry must record success and exception exactly once.");
            Check(events.Any(e => (string)e.GetType().GetProperty("Notes").GetValue(e) == "cache=hit"), "Real cache hit metadata must be recorded.");
            Check(events.Any(e => ((string)e.GetType().GetProperty("Outcome").GetValue(e)).StartsWith("exception:")), "Real thrown exception must finalize its span.");
            Check((long)snapshot.GetType().GetProperty("ProbeErrors").GetValue(snapshot) == 0, "Real probe metadata must not fail.");
        }
        finally { capture.GetField("current", flags).SetValue(null, null); sessionType.GetMethod("Close", instanceFlags).Invoke(session, null); }
        Console.WriteLine("PASS: real resource cache hit, exception propagation, metadata and finalization.");

        capture.GetMethod("Add", flags).Invoke(null, new object[] { typeof(ProbeFixture), "Compile", "Test.OutCache", null, null, null, "CompilerCacheResult", null });
        var outSession = Activator.CreateInstance(sessionType, true);
        capture.GetField("current", flags).SetValue(null, outSession);
        object outSnapshot;
        try
        {
            object[] compileArgs = { "", "synthetic", "main", 0, 0, false };
            Check((int)typeof(ProbeFixture).GetMethod("Compile").Invoke(null, compileArgs) == 7 && (bool)compileArgs[5], "Out parameters and return values must survive instrumentation.");
            outSnapshot = sessionType.GetMethod("Read", instanceFlags).Invoke(outSession, null);
            var outEvents = ((Array)outSnapshot.GetType().GetProperty("Events").GetValue(outSnapshot)).Cast<object>().ToArray();
            Check(outEvents.Length == 1 && (string)outEvents[0].GetType().GetProperty("Outcome").GetValue(outEvents[0]) == "compilerCacheHit=True",
                "Out-parameter cache hit must be visible to the postfix before finalization.");
            var text = (string)capture.GetMethod("BuildReport", flags).Invoke(null, new[] { outSnapshot });
            Check(text.Contains("compilerCacheHit=True") && text.Contains("探针覆盖"), "Text report must include measured cache results and coverage.");
            var json = System.Text.Json.JsonSerializer.Serialize(outSnapshot, outSnapshot.GetType(), new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
            using var document = System.Text.Json.JsonDocument.Parse(json);
            Check(document.RootElement.GetProperty("Events").GetArrayLength() == 1 && document.RootElement.GetProperty("Groups").GetArrayLength() == 1,
                "Structured export must serialize events and aggregate fields.");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string redacted = (string)capture.GetMethod("Redact", flags).Invoke(null, new object[] { home + " " + System.Text.Json.JsonSerializer.Serialize(home) });
            Check(!redacted.Contains(home, StringComparison.OrdinalIgnoreCase) && redacted.Contains("<USER>"), "Plain and JSON-escaped user paths must both be redacted.");
        }
        finally { capture.GetField("current", flags).SetValue(null, null); sessionType.GetMethod("Close", instanceFlags).Invoke(outSession, null); }
        Console.WriteLine("PASS: out parameters, cache postfix, text/JSON export and path redaction.");
    }
    finally
    {
        remove.Invoke(null, null);
        Check(((System.Collections.ICollection)capture.GetField("installed", flags).GetValue(null)).Count == 0, "All session probes must be removed.");
    }
    Console.WriteLine("PASS: real-assembly probes removed.");
}
return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

internal static class ProbeFixture
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int Compile(string source, string sourceName, string entryPoint, int language, int stage, out bool cacheHit)
    {
        cacheHit = true;
        return 7;
    }
}
