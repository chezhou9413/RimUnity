using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using RimUnity;

internal static partial class Program
{
    private static void BenchmarkTrailSingleRuntime(string destination)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        Check(typeof(ParticleSystem).Assembly.GetType("RimUnity.Diagnostics.LabParticleDetail") == null, "use normal Release for CPU comparison");
        using var rig = new Rig(typeof(ParticleSystem).Assembly);
        var rows = new List<object>();
        foreach (string[] layout in new[] { Array.Empty<string>(), new[] { "UV", "VertexID" } })
        foreach (bool ribbon in new[] { false, true })
        {
            int points = ribbon ? 33 : 17;
            rig.Configure("Local", "Hierarchy", "Billboard", "View", 1, 1); rig.Streams(layout);
            var action = rig.TrailBatch(rig.Trail(points, "Stretch", ribbon, 0, 1), ribbon);
            var warm = Stopwatch.StartNew();
            for (int i = 0; i < 1000 || warm.ElapsedMilliseconds < 1500; i++) action();
            var ms = new List<double>(); var bytes = new List<long>();
            for (int run = 0; run < 8; run++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 200; i++) action();
                double elapsed = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / 200;
                long delta = GC.GetAllocatedBytesForCurrentThread() - allocated;
                ms.Add(elapsed); bytes.Add(delta);
                Check(delta == 0, "single-runtime warmed trail write allocates");
            }
            var sorted = ms.Order().ToArray();
            rows.Add(new { Layout = layout, Ribbon = ribbon, Points = points, Trails = 256,
                MedianMs = (sorted[3] + sorted[4]) / 2, RunsMs = ms, AllocatedBytes = bytes });
        }
        File.WriteAllText(destination, JsonSerializer.Serialize(new { Mvid = typeof(ParticleSystem).Module.ModuleVersionId,
            Runtime = typeof(ParticleSystem).Assembly.Location, TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            Caveats = "One runtime in the default loading context per process. Compare alternating separate baseline/candidate processes; cross-context ratios are invalid (same-DLL control found large bias). Synthetic CPU geometry only, no submission/GPU/whole-frame claim.", Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS single-runtime trail CPU benchmark; " + destination);
    }

    private static void BenchmarkTrailReservation(string baseline, string destination)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("trail reservation baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(Path.GetFullPath(baseline)));
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        var rows = new List<object>();
        foreach (string[] layout in new[] { Array.Empty<string>(), new[] { "UV", "VertexID" } })
        foreach (bool ribbon in new[] { false, true })
        foreach (int points in new[] { 2, 17, 33 })
        {
            foreach (var rig in new[] { old, current })
            { rig.Configure("Local", "Hierarchy", "Billboard", "View", 1, 1); rig.Streams(layout); }
            var slow = old.TrailBatch(old.Trail(points, "Stretch", ribbon, 0, 1), ribbon);
            var fast = current.TrailBatch(current.Trail(points, "Stretch", ribbon, 0, 1), ribbon);
            for (int i = 0; i < 500; i++) { slow(); fast(); }
            // Tier promotion is delayed by wall time, not just call count. Short
            // trails can finish 500 warmups before the tiering queue runs.
            var warm = Stopwatch.StartNew();
            while (warm.ElapsedMilliseconds < 1000) { slow(); fast(); }
            var a = new List<double>(); var b = new List<double>();
            var allocatedA = new List<long>(); var allocatedB = new List<long>();
            for (int run = 0; run < 6; run++)
            foreach (bool optimized in run % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                var action = optimized ? fast : slow;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100; i++) action();
                double ms = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / 100;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0")
                    Check(allocated == 0, "fixed-JIT trail reservation benchmark allocates after warmup");
                (optimized ? allocatedB : allocatedA).Add(allocated);
                (optimized ? b : a).Add(ms);
            }
            double Median(List<double> values) { var sorted = values.Order().ToArray(); return (sorted[2] + sorted[3]) / 2; }
            rows.Add(new { Layout = layout, Ribbon = ribbon, Points = points, Trails = 256,
                BaselineMedianMs = Median(a), CandidateMedianMs = Median(b), Ratio = Median(b) / Median(a),
                BaselineRunsMs = a, CandidateRunsMs = b, BaselineAllocatedBytes = allocatedA, CandidateAllocatedBytes = allocatedB });
        }
        File.WriteAllText(destination, JsonSerializer.Serialize(new { BaselineMvid = old.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId,
            CandidateMvid = typeof(ParticleSystem).Module.ModuleVersionId, TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            Caveats = "INVALID FOR OPTIMIZATION RATIOS: cross-loading-context same-DLL control found large bias. Use only for calibration, numeric checks and allocation investigation. Use --trail-reserve-single in alternating separate processes for CPU comparisons. Not whole-frame savings.", Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS 12 warmed trail reservation CPU comparisons; " + destination);
    }

    private static void CheckShortLivedTrails(Rig old, Rig current)
    {
        foreach (bool dieWithParticle in new[] { false, true })
        foreach (var layout in new[] { Array.Empty<string>(), new[] { "UV", "VertexID" } })
        {
            var a = old.ShortLivedTrailChannels(dieWithParticle, layout);
            var b = current.ShortLivedTrailChannels(dieWithParticle, layout);
            foreach (string channel in a.Keys)
                Check(ChannelBytes(channel, a[channel]).SequenceEqual(ChannelBytes(channel, b[channel])), "same-frame birth/death trail mismatch: " + channel);
        }
        Console.WriteLine("PASS same-frame particle birth/death: both dieWithParticles settings and simple/general streams match baseline.");
    }

    private sealed partial class Rig
    {
        internal Dictionary<string, object[]> ShortLivedTrailChannels(bool dieWithParticle, string[] layout)
        {
            Configure("World", "Hierarchy", "Billboard", "View", 1, 1); Streams(layout);
            Trail(0, "Stretch", false, 1, 1);
            Set(System, "useAutoRandomSeed", false); Set(System, "randomSeed", 9413u);
            Set(Get(System, "shape"), "enabled", false);
            var curve = ps.GetNestedType("MinMaxCurve");
            object Constant(float x) => Activator.CreateInstance(curve, new object[] { x });
            Set(main, "startLifetime", Constant(.004f)); Set(main, "startSpeed", Constant(1));
            var emission = Get(System, "emission"); Set(emission, "enabled", false);
            var module = Get(System, "trails");
            Set(module, "enabled", true); Set(module, "ratio", 1f); Set(module, "dieWithParticles", dieWithParticle);
            SetEnum(module, "mode", "PerParticle"); Set(module, "lifetime", Constant(100));
            Set(module, "minVertexDistance", 0f);
            var simulate = ps.GetMethod("Simulate");
            simulate.Invoke(System, new object[] { 0f, false, true, false });
            ps.GetMethod("Emit", new[] { typeof(int) }).Invoke(System, new object[] { 1 });
            simulate.Invoke(System, new object[] { .02f, false, false, false });
            Check((int)ps.GetProperty("particleCount").GetValue(System) == 0, "short-lived fixture must die within the step");
            renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            foreach (DictionaryEntry entry in (IDictionary)Get(System, "trailStates"))
                TrailChannels(entry.Value, false, clear: false);
            return Channels();
        }

        internal void CheckTrailReservationFailure()
        {
            Configure("Local", "Hierarchy", "Billboard", "View", 1, 0); Streams();
            var trail = Trail(17, "Stretch", false, 0, 0);
            var before = TrailChannels(trail, false);
            // Deterministic fault injection into the private numeric-list metadata:
            // force checked overflow after earlier channels have been reserved.
            // No game input can create this state; it tests the actual finally path
            // without adding a production callback or fault-injection branch.
            var tint = renderer.GetField("colors", Flags).GetValue(Renderer);
            var size = tint.GetType().GetField("_size", Flags);
            size.SetValue(tint, int.MaxValue);
            bool failed = false;
            try { TrailChannels(trail, false, clear: false); }
            catch (TargetInvocationException ex) when (ex.InnerException is OverflowException) { failed = true; }
            finally
            {
                if ((int)size.GetValue(tint) == int.MaxValue) size.SetValue(tint, before["colors"].Length);
            }
            Check(failed, "partial trail reservation must throw on checked channel overflow");
            var after = Channels();
            foreach (string channel in before.Keys)
                Check(ChannelBytes(channel, before[channel]).SequenceEqual(ChannelBytes(channel, after[channel])), "failed reservation changed preceding " + channel);
            var reused = TrailChannels(trail, false);
            foreach (string channel in before.Keys)
                Check(ChannelBytes(channel, before[channel]).SequenceEqual(ChannelBytes(channel, reused[channel])), "renderer cannot recover after reservation failure: " + channel);
            Console.WriteLine("PASS partially reserved trail failure: all nine prior channels preserved; next draw reuses buffers correctly.");
        }
    }
}
