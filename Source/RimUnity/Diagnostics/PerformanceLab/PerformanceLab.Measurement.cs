using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using RimWorld;
using Verse;

namespace RimUnity.Diagnostics;

// Owned by the developer lab only. No scheduler survives Close/map change/permission loss.
internal sealed partial class PerformanceLab
{
    private readonly List<LabMeasurementPlan.Run> measurementRuns = new(13);
    private LabMeasurementPlan.Job[] measurementJobs;
    private int measurementIndex;
    private bool measurementCollecting;
    private double measurementDeadline;
    private string measurementPrefix;
    private SavedMeasurementSettings measurementSettings;
    internal bool MeasurementRunning => measurementJobs != null;
    internal string MeasurementProgress => MeasurementRunning
        ? $"自动测量 {measurementIndex + 1}/{measurementJobs.Length} · {measurementJobs[measurementIndex].Name} · {(Sampler.Sampling ? $"采样 {Sampler.Seconds:F0}/{SampleSeconds} 秒" : "建立场景 / 预热")}" : "";
    internal string MeasurementReport { get; private set; }
    internal string MeasurementJob => MeasurementRunning ? measurementJobs[measurementIndex].Name : null;
    internal int StressParticleTarget = 64, StressOnlyFamily = -1;
    private sealed record SavedMeasurementSettings(int Concurrency, LabCpuMode Mode, int Seconds, float Warmup,
        bool Bloom, bool Fixed, float Radius, float Scale, float Rotation, float Rate, float Lifetime, int Target, int Family);

    internal void StartMeasurementSequence(bool geometryOnly = false)
    {
        if (!IsCurrent || !DebugSettings.ShowDevGizmos || MeasurementRunning) return;
#if RIMUNITY_PARTICLE_ANALYSIS
        Status = "请使用普通 Release 执行模式对照，分析构建会改变热路径。"; return;
#endif
        if (ParticleSpikeCapture.LabCaptureActive) { Status = "请先结束详细探针，再执行自动模式对照。"; return; }
        Sampler.Stop("开始自动测量", export: true);
        measurementSettings = new(Concurrency, CpuMode, SampleSeconds, WarmupSeconds, StressBloom, FixedInstances,
            Radius, Scale, RotationX, Rate, Lifetime, StressParticleTarget, StressOnlyFamily);
        measurementJobs = geometryOnly ? LabMeasurementPlan.GeometryJobs() : LabMeasurementPlan.Jobs();
        measurementIndex = 0; measurementRuns.Clear();
        MeasurementReport = null;
        try
        {
            string directory = Path.Combine(GenFilePaths.ConfigFolderPath, "RimUnityReports");
            measurementPrefix = Path.Combine(directory, "effect-suite-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            BeginMeasurementJob();
        }
        catch (Exception ex) { EndMeasurementSequence("启动失败：" + ex.Message); }
    }

    private void ConfigureMeasurementLoad(ParticleSystem[] systems)
    {
        var children = new HashSet<ParticleSystem>();
        foreach (var ps in systems) foreach (var entry in ps.subEmitters.entries)
            if (entry.system != null) children.Add(entry.system);
        foreach (var ps in systems)
        {
            ps.main.maxParticles = StressParticleTarget;
            // Emission roots reach the requested steady occupancy. Event-driven child
            // emission/Burst contracts remain intact; actual child population is reported.
            if (!children.Contains(ps))
            {
                ps.emission.rateOverTime = StressParticleTarget / Math.Max(.1f, ps.main.startLifetime.constant);
                ps.emission.rateOverDistance = 0;
            }
        }
    }

    private void BeginMeasurementJob()
    {
        var job = measurementJobs[measurementIndex];
        Concurrency = job.Roots; CpuMode = job.Mode; FixedInstances = true; StressBloom = false;
        WarmupSeconds = 12; SampleSeconds = 60; Radius = 14; Scale = 1; RotationX = 0;
        Rate = 16; Lifetime = 4; StressParticleTarget = job.ParticlesPerRoot; StressOnlyFamily = job.Family;
        CameraJumper.TryJump(Map.Center, Map, CameraJumper.MovementMode.Cut); Find.CameraDriver.SetRootSize(24);
        measurementCollecting = false;
        measurementDeadline = Now + WarmupSeconds + SampleSeconds + 120;
        StartPlayback(mixedStress: true);
        if (!Playing) throw new InvalidOperationException(Status);
    }

    // Called before simulation, not from the export callback. Scene retirement and
    // report allocation never happen inside an already-running sampled frame.
    private void UpdateMeasurementSequence()
    {
        if (!MeasurementRunning) return;
        if (!IsCurrent || !DebugSettings.ShowDevGizmos || ParticleSpikeCapture.LabCaptureActive)
        { EndMeasurementSequence("权限、场景或详细探针状态改变"); return; }
        if (Now > measurementDeadline || !Playing)
        { EndMeasurementSequence("场景未能按时完成建立或采样"); return; }
        if (Sampler.Sampling) { measurementCollecting = true; return; }
        if (!measurementCollecting) return;
        if (Sampler.MeasurementStopReason != "采样时间结束" || string.IsNullOrEmpty(Sampler.LastReport))
        { EndMeasurementSequence("采样中断或报告导出失败"); return; }
        measurementRuns.Add(Sampler.MeasurementResult(measurementJobs[measurementIndex]));
        WriteMeasurementSummary("Running");
        measurementIndex++;
        if (measurementIndex == measurementJobs.Length) { EndMeasurementSequence("完成"); return; }
        BeginMeasurementJob();
    }

    internal void EndMeasurementSequence(string reason)
    {
        if (!MeasurementRunning) return;
        // Clear the running flag before any cleanup can re-enter the lab.
        measurementJobs = null;
        try
        {
            try { Sampler?.Stop("自动测量停止：" + reason, export: true); }
            finally { StopPlayback(); }
            WriteMeasurementSummary(reason);
            Status = "自动测量" + reason + "；汇总：" + MeasurementReport;
        }
        catch (Exception ex) { Status = "自动测量已停止，汇总写入失败：" + ex.Message; Log.Error("[RimUnity] " + Status); }
        finally
        {
            var saved = measurementSettings; measurementSettings = null;
            if (saved != null)
            {
                Concurrency = saved.Concurrency; CpuMode = saved.Mode; SampleSeconds = saved.Seconds; WarmupSeconds = saved.Warmup;
                StressBloom = saved.Bloom; FixedInstances = saved.Fixed; Radius = saved.Radius; Scale = saved.Scale;
                RotationX = saved.Rotation; Rate = saved.Rate; Lifetime = saved.Lifetime;
                StressParticleTarget = saved.Target; StressOnlyFamily = saved.Family;
            }
        }
    }

    private void WriteMeasurementSummary(string status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(measurementPrefix));
        var probe = LabMeasurementPlan.Compare(measurementRuns);
        var fit = LabMeasurementPlan.FitMixed(measurementRuns);
        var trailFit = LabMeasurementPlan.FitTrails(measurementRuns);
        const string caveats = "CPU main-thread elapsed time, not GPU or whole-module CPU usage. Both enabled modes retain boundary timestamp/allocation/process-GC probes and common workload observers. Inactive source probe calls remain in both modes; comparison measures collection overhead, not a build with all diagnostic call sites removed. GC counter changes can be caused by other threads; not RU trigger attribution or pause duration. Off has no Bridge measurement (ValidBridgeFrames=0). Two repeats provide an observed spread, not a statistical confidence interval. P95/P99 comparisons use per-run quantiles, not pooled quantiles. Regression uses actual mean active systems/particles, not configured caps; coefficients describe this mixed workload, not universal unit costs. Trail regression is whole-Bridge association and also includes particle simulation/rendering. Actual workload variation, host load, frequency, camera and GC can confound fits. No warmup/creation/export time is included in valid Bridge windows. Partial/failed runs are not included.";
        var text = new StringBuilder("RimUnity 自动测量汇总 / schema 1\n状态：" + status + "\n");
        text.AppendLine("name\tmode\tsystems\tparticles\tparticle_stddev\ttrail_points\tvertices\tindices\tbridge_mean_ms\tbridge_stddev_ms\tp95_ms\tp99_ms\tvalid_frames");
        foreach (var r in measurementRuns)
            text.AppendLine(FormattableString.Invariant($"{r.Job.Name}\t{r.Job.Mode}\t{r.Systems:F2}\t{r.Particles:F2}\t{r.ParticleStdDev:F2}\t{r.TrailPoints:F2}\t{r.Vertices:F2}\t{r.Indices:F2}\t{(r.BridgeAvailable ? r.BridgeMeanMs.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) : "NotMeasured")}\t{r.BridgeStdDevMs:F6}\t{r.BridgeP95Ms:F6}\t{r.BridgeP99Ms:F6}\t{r.ValidBridgeFrames}"));
        text.AppendLine("探针对照：" + JsonSerializer.Serialize(probe));
        text.AppendLine("Bridge ≈ a×实际系统数 + b×实际粒子数 + c：" + JsonSerializer.Serialize(fit));
        text.AppendLine("拖尾点关联：" + JsonSerializer.Serialize(trailFit));
        text.AppendLine(caveats);
        foreach (var r in measurementRuns) text.AppendLine(r.Job.Name + ": " + r.Report);
        File.WriteAllText(measurementPrefix + ".txt", text.ToString(), new UTF8Encoding(false));
        File.WriteAllText(measurementPrefix + ".json", JsonSerializer.Serialize(new { Schema = 1, Status = status, Runs = measurementRuns, ProbeComparison = probe, MixedFit = fit, TrailFit = trailFit, Caveats = caveats }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        MeasurementReport = measurementPrefix + ".txt";
    }
}
