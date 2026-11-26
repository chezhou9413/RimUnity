using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Verse;
using Waylith;

namespace RimUnity.PerformanceLoop;

[StaticConstructorOnStartup]
internal static class RunLab
{
    private const string ResultFile = "rimunity-performance-result.json";
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly string[] RuntimeAssemblyNames = ["RimUnity", "RimUnity.Core", "RimUnity.Spine"];
    private sealed record Request(int Seconds, bool Bloom, string CpuMode, bool RenderTrace);
    private static EngineApplication? application;
    private static Request request = null!;
    private static Type? labType;
    private static Dictionary<string, string>? runtimeHashes;
    private static object? lab, sampler;
    private static Func<object, int> sampleCount = null!;
    private static Func<object, bool> sampling = null!;
    private static Func<object, bool> isCurrent = null!;
    private static int lastCount;
    private static bool opened, started, finished;

    static RunLab()
    {
        if (!GenCommandLine.CommandLineArgPassed("rimunityPerformanceLoop")
            || !GenCommandLine.CommandLineArgPassed("quicktest")
            || !GenCommandLine.TryGetCommandLineArg("savedatafolder", out _)) return;
        LongEventHandler.ExecuteWhenFinished(Start);
    }

    private static void Start()
    {
        try
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "rimunity-performance-request.json");
            request = JsonSerializer.Deserialize<Request>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Missing benchmark request.");
            if (!Prefs.DevMode || request.Seconds is < 5 or > 180 || request.CpuMode is not ("BridgeOnly" or "Stages"))
                throw new InvalidOperationException("The isolated benchmark requires developer mode and a valid request.");
            // Only the explicit quick-test process enables god mode for the existing lab permission gate.
            DebugSettings.godMode = true;
            application = EngineApplication.Instance ?? throw new InvalidOperationException("Waylith application is unavailable.");
            application.PrePollEvents += Poll;
        }
        catch (Exception exception) { Complete(null, exception); }
    }

    private static void Poll(EngineApplication app)
    {
        try
        {
            if (!DebugSettings.ShowDevGizmos) throw new InvalidOperationException("Developer permissions changed.");
            if (LongEventHandler.AnyEventNowOrWaiting || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            if (!opened)
            {
                var runtime = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RimUnity");
                if (!(bool)runtime.GetType("RimUnity.RimUnityMod", true)!.GetProperty("IsRuntimeReady")!.GetValue(null)!) return;
                runtimeHashes = CaptureRuntimeHashes();
                labType = runtime.GetType("RimUnity.Diagnostics.PerformanceLab", true)!;
                Invoke(labType, null, "Open");
                opened = true;
                Log.Message("[RimUnity Performance Loop] Opening the performance lab.");
                return;
            }
            if (started)
            {
                if (!isCurrent(lab!)) throw new InvalidOperationException("The performance lab closed during the run.");
                Observe();
                return;
            }
            if (labType!.GetProperty("Current", Members)!.GetValue(null) is not object currentLab) return;
            lab = currentLab;
            isCurrent = Getter<bool>(labType, "IsCurrent");
            sampler = labType.GetProperty("Sampler", Members)!.GetValue(lab)!;
            sampleCount = Getter<int>(sampler.GetType(), "Count");
            sampling = Getter<bool>(sampler.GetType(), "Sampling");
            Set(lab, "StressBloom", request.Bloom);
            var modeField = labType.GetField("CpuMode", Members)!;
            modeField.SetValue(lab, Enum.Parse(modeField.FieldType, request.CpuMode));
            Invoke(labType, lab, "StartMixedStress");
            Set(lab, "SampleSeconds", request.Seconds);
            if (!(bool)labType.GetProperty("Playing", Members)!.GetValue(lab)!)
                throw new InvalidOperationException((string?)labType.GetField("Status", Members)?.GetValue(lab));
            WaitProbes.Install();
            if (request.RenderTrace) RenderProbes.Install();
            started = true;
            Log.Message("[RimUnity Performance Loop] Warming up the mixed particle scenario.");
        }
        catch (Exception exception) { Complete(null, exception); }
    }

    private static void Observe()
    {
        int count = sampleCount(sampler!);
        var pending = WaitProbes.Take();
        if (count == 0) return;
        if (sampling(sampler!))
        {
            if (lastCount == 0 && request.RenderTrace) RenderProbes.Start();
            // Throttle after sampled frame N belongs to the interval ending at frame N+1.
            if (count != lastCount) WaitProbes.Store(count + 1, pending);
            lastCount = count;
            return;
        }

        string? reason = (string?)sampler!.GetType().GetProperty("MeasurementStopReason", Members)!.GetValue(sampler);
        string? report = (string?)sampler.GetType().GetProperty("LastReport", Members)!.GetValue(sampler);
        if (reason != "采样时间结束" || string.IsNullOrWhiteSpace(report) || !File.Exists(report))
            throw new InvalidOperationException("Sampling did not finish normally: " + reason);
        Complete(report, null);
    }

    private static Func<object, T> Getter<T>(Type type, string property)
    {
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, T>>(
            Expression.Property(Expression.Convert(target, type), type.GetProperty(property, Members)!), target).Compile();
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Members)!.SetValue(target, value);
    private static void Invoke(Type type, object? target, string method) => type.GetMethod(method, Members)!.Invoke(target, null);

    private static Dictionary<string, string> CaptureRuntimeHashes()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in RuntimeAssemblyNames)
        {
            var assembly = assemblies.Single(a => a.GetName().Name == name);
            using var file = File.OpenRead(assembly.Location);
            hashes.Add(name, Convert.ToHexString(SHA256.HashData(file)));
        }
        return hashes;
    }

    private static void Complete(string? report, Exception? error)
    {
        if (finished) return;
        finished = true;
        if (application != null) application.PrePollEvents -= Poll;
        bool renderTrace = request?.RenderTrace == true;
        try
        {
            WaitProbes.Remove();
            if (renderTrace) RenderProbes.Stop();
            if (report != null)
            {
                WaitProbes.Export(Path.ChangeExtension(report, ".waits.csv"), sampleCount(sampler!));
                if (renderTrace) RenderProbes.Export(Path.ChangeExtension(report, ".render.csv"));
            }
        }
        catch (Exception exception) { error ??= exception; }
        string path = Path.Combine(GenFilePaths.SaveDataFolderPath, ResultFile);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { Passed = error == null, Report = report, RuntimeHashes = runtimeHashes, Error = error?.ToString() }));
        File.Move(path + ".tmp", path, true);
        if (error != null) Log.Error("[RimUnity Performance Loop] " + error);
        else Log.Message("[RimUnity Performance Loop] Exported " + report);
        (application ?? EngineApplication.Instance)?.RequestShutdown("RimUnity Performance Loop complete");
    }
}
