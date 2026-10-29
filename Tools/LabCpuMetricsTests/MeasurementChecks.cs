using System.Diagnostics;
using System.Text.Json;
using RimUnity;
using RimUnity.Diagnostics;

internal static class MeasurementChecks
{
    internal static void Run(Func<Material, bool> append)
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        ParticleDrawBatch.Clear(); Graphics.Draws.Clear(); Time.frameCount++; ParticleDrawBatch.Sampling = true;
        ParticleDrawBatch.Begin(); append(new Material()); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(ParticleDrawBatch.SubmittedVertices == 3 && ParticleDrawBatch.SubmittedIndices == 3, "common geometry counters work without CPU-stage capture");
        ParticleDrawBatch.Begin(); append(new Material()); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(ParticleDrawBatch.SubmittedVertices == 6, "multiple world cameras accumulate within a frame");
        Time.frameCount++; Check(ParticleDrawBatch.SubmittedVertices == 0, "next frame cannot report stale geometry");
        ParticleDrawBatch.Sampling = false; ParticleDrawBatch.Begin(); append(new Material()); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(ParticleDrawBatch.SubmittedVertices == 0, "closed sampler does not collect workload counters");
        ParticleDrawBatch.Clear(); Graphics.Draws.Clear();
        var cpu = new LabCpuMetrics(5, LabCpuMode.BridgeOnly); cpu.Start();
        for (int i = 0; i < 3; i++)
        {
            cpu.BeginMainFrame();
            using (LabCpuMetrics.BridgeWindow())
            {
                using var ignored = LabCpuMetrics.Measure(LabCpuStage.ParticleAttributes);
                LabCpuMetrics.Batch(LabBatchReason.Accepted);
                LabCpuMetrics.Work(LabCpuStage.ParticleSubmit, 4, 6);
                LabCpuMetrics.RecordSnapshot(false, 5, 0);
                if (i == 1) GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            }
            cpu.CompleteFrame();
        }
        cpu.Stop();
        Check(cpu.FrameCount == 3 && !cpu.HasWindows(0) && cpu.HasWindows(1), "bridge-only omits partial first frame");
        Check(LabCpuReport.Stages(cpu).Length == 0 && LabCpuReport.Reasons(cpu).Length == 0, "bridge-only stages/reasons absent, not fictitious zero rows");
        Check(cpu.Get(1, LabCpuStage.ParticleAttributes).Calls == 0 && cpu.GetSnapshot(1).HdrCalls == 0, "bridge-only ignores nested hot-path probes");
        var windows = LabCpuReport.Windows(cpu); var bridge = windows.Single(w => w.Name == "Bridge");
        Check(bridge.CompleteFrames == 2 && bridge.Gen0 >= 1 && bridge.P99Ms >= bridge.P95Ms && bridge.StdDevMs >= 0, "window quantiles, SD and observed process GC changes");
        double one = LabCpuReport.Ms(cpu.GetWindow(1, LabFrameWindow.Bridge).Ticks), two = LabCpuReport.Ms(cpu.GetWindow(2, LabFrameWindow.Bridge).Ticks);
        Check(Math.Abs(bridge.StdDevMs - Math.Abs(one - two) / 2) < 1e-9, "population SD uses valid complete windows only");

        using (var sampler = new LabSampler(new PerformanceLab { CpuMode = LabCpuMode.BridgeOnly }))
        {
            sampler.Start(60);
            for (int i = 0; i < 3; i++)
            {
                Waylith.EngineApplication.Instance.Poll();
                using (LabCpuMetrics.BridgeWindow()) { using var scope = LabCpuMetrics.Measure(LabCpuStage.BridgeUpdate); }
                Waylith.EngineApplication.Instance.Emit();
            }
            sampler.Stop("bridge-only fixture", true);
            string prefix = Path.ChangeExtension(sampler.LastReport, null);
            using var doc = JsonDocument.Parse(File.ReadAllText(prefix + ".json"));
            var json = doc.RootElement.GetProperty("Cpu");
            Check(json.GetProperty("Mode").GetString() == "BridgeOnly" && !json.GetProperty("StagesEnabled").GetBoolean(), "real report identifies mode");
            Check(json.GetProperty("Stages").GetArrayLength() == 0 && json.GetProperty("FrameWindows").GetArrayLength() == 4, "real report retains boundary windows only");
            Check(File.Exists(prefix + ".windows.csv") && !File.Exists(prefix + ".stages.csv") && !File.Exists(prefix + ".families.csv"), "bridge-only exports no unmeasured stage CSVs");
            Check(File.ReadAllLines(prefix + ".windows.csv").All(line => line.Split(',').Length == 8), "window CSV includes explicit GC availability");
        }

        LabMeasurementPlan.Run Row(string name, LabCpuMode mode, double systems, double particles, double time) =>
            new(new(name, mode, 48, 64), "fixture", 100, 99, time, .01, time + .02, time + .04, systems, particles, particles * 3, particles * 4, particles * 6, .1, 2, 0, 0, 0);
        var rows = new List<LabMeasurementPlan.Run>();
        foreach (var point in new[] { (24d, 3000d), (48d, 3000d), (96d, 3000d), (48d, 1000d), (48d, 12000d) })
            rows.Add(Row("scale", LabCpuMode.BridgeOnly, point.Item1, point.Item2, .002 * point.Item1 + .0001 * point.Item2 + .3));
        var fit = LabMeasurementPlan.FitMixed(rows);
        Check(Math.Abs(fit.SystemMs - .002) < 1e-10 && Math.Abs(fit.ParticleMs - .0001) < 1e-10 && Math.Abs(fit.InterceptMs - .3) < 1e-10, "fit solves independent actual systems/particles");
        Check(fit.RmseMs < 1e-10 && fit.RSquared > .99999, "fit residuals exported");
        Check(LabMeasurementPlan.FitMixed(Enumerable.Range(1, 5).Select(i => Row("rank", LabCpuMode.BridgeOnly, i, 64 * i, i)).ToArray()).Status == "RankDeficient", "collinear workloads cannot claim unit costs");
        var comparison = new[] { Row("bridge-A1", LabCpuMode.BridgeOnly, 52, 3000, 1), Row("bridge-A2", LabCpuMode.BridgeOnly, 52, 3000, 1.02),
            Row("stages-B1", LabCpuMode.Stages, 52, 3000, 1.01), Row("stages-B2", LabCpuMode.Stages, 52, 3000, 1.02) };
        Check(LabMeasurementPlan.Compare(comparison).Status == "WithinObservedSpread", "small probe difference does not escape observed repeat spread");
        comparison[2] = comparison[2] with { Particles = 4000 };
        Check(LabMeasurementPlan.Compare(comparison).Status == "WorkloadMismatch", "probe comparison rejects unequal actual workloads");
        Check(LabMeasurementPlan.Jobs().Length == 13 && LabMeasurementPlan.Jobs().Select(j => j.Name).Distinct().Count() == 13, "bounded thirteen-step plan with unique identities");
        Console.WriteLine($"PASS {checks} measurement-mode/report/regression checks.");
    }
}
