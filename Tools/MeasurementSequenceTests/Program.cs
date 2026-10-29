using System.Text.Json;
using RimUnity;
using RimUnity.Diagnostics;
using Verse;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Main()
    {
        var lab = new PerformanceLab(); lab.StartMeasurementSequence();
        Check(lab.MeasurementRunning && lab.Starts == 1 && lab.CpuMode == LabCpuMode.BridgeOnly, "sequence starts first planned mode");
        lab.StartMeasurementSequence(); Check(lab.Starts == 1, "duplicate start is ignored");
        for (int i = 0; i < 13; i++)
        {
            lab.Sampler.Sampling = true; lab.UpdateSequence();
            lab.Sampler.Complete(); lab.UpdateSequence();
            Check(i == 12 ? !lab.MeasurementRunning : lab.Starts == i + 2, "each completed run advances exactly once");
        }
        Check(lab.CpuMode == LabCpuMode.Off && lab.Concurrency == 17 && lab.SampleSeconds == 30 && lab.Scale == 2 && lab.StressBloom, "user settings restored after completion");
        Check(!lab.Playing && lab.Clears > 0 && File.Exists(lab.MeasurementReport), "completion retires scene and writes summary");
        using (var summary = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(lab.MeasurementReport, ".json"))))
            Check(summary.RootElement.GetProperty("Runs").GetArrayLength() == 13 && summary.RootElement.GetProperty("Status").GetString() == "完成", "all completed runs serialized");

        lab = new PerformanceLab(); lab.StartMeasurementSequence(geometryOnly: true);
        Check(lab.Concurrency == 48 && lab.StressParticleTarget == 64 && lab.CpuMode == LabCpuMode.BridgeOnly, "short run matches standard full-suite load");
        lab.Sampler.Sampling = true; lab.UpdateSequence(); lab.Sampler.Complete(); lab.UpdateSequence();
        Check(lab.MeasurementRunning && lab.Concurrency == 4 && lab.StressParticleTarget == 256 && lab.StressOnlyFamily == 5, "short run advances to full-suite high-trail load");
        lab.Sampler.Sampling = true; lab.UpdateSequence(); lab.Sampler.Complete(); lab.UpdateSequence();
        using (var summary = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(lab.MeasurementReport, ".json"))))
            Check(summary.RootElement.GetProperty("Runs").GetArrayLength() == 2 && !lab.MeasurementRunning && lab.Concurrency == 17, "short run exports both jobs and restores settings");

        foreach (string reason in new[] { "cancel", "permission", "context", "timeout", "capture", "partial", "export", "startup" })
        {
            lab = new PerformanceLab(); DebugSettings.ShowDevGizmos = true; PerformanceLab.Clock = 0;
            lab.FailStart = reason == "startup"; lab.StartMeasurementSequence();
            if (reason != "startup")
            {
                lab.Sampler.Sampling = true; lab.UpdateSequence();
                switch (reason)
                {
                    case "cancel": lab.EndMeasurementSequence("用户停止"); break;
                    case "permission": DebugSettings.ShowDevGizmos = false; lab.UpdateSequence(); break;
                    case "context": lab.IsCurrent = false; lab.UpdateSequence(); break;
                    case "timeout": PerformanceLab.Clock = 1000; lab.UpdateSequence(); break;
                    case "capture": ParticleSpikeCapture.LabCaptureActive = true; lab.UpdateSequence(); ParticleSpikeCapture.LabCaptureActive = false; break;
                    case "partial": lab.Sampler.Complete("手动停止"); lab.UpdateSequence(); break;
                    case "export": lab.Sampler.Complete(); lab.Sampler.LastReport = null; lab.UpdateSequence(); break;
                }
            }
            Check(!lab.MeasurementRunning && !lab.Sampler.Sampling && !lab.Playing, reason + " clears running state");
            Check(lab.Concurrency == 17 && lab.CpuMode == LabCpuMode.Off, reason + " restores settings");
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(lab.MeasurementReport, ".json")));
            Check(summary.RootElement.GetProperty("Runs").GetArrayLength() == 0, reason + " excludes partial run from fit");
        }
        DebugSettings.ShowDevGizmos = true;
        lab = new PerformanceLab { StressParticleTarget = 256 };
        var parent = new ParticleSystem(); var child = new ParticleSystem();
        parent.subEmitters.entries = [new() { system = child }]; child.emission.rateOverTime = 0;
        lab.ConfigureLoad([parent, child]);
        Check(parent.main.maxParticles == 256 && parent.emission.rateOverTime == 64, "scale changes cap and root emission rate together");
        Check(child.main.maxParticles == 256 && child.emission.rateOverTime == 0, "event-driven child keeps its emission contract");
        Console.WriteLine($"PASS {checks} production sequence lifecycle/plan/configuration checks (fake clock/host, no game/GPU).");
    }
}
