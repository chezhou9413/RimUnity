using System;
using System.Text;

namespace RimUnity.Diagnostics;

internal sealed partial class LabSampler
{
#if RIMUNITY_PARTICLE_ANALYSIS
    private const bool ParticleAnalysisBuild = true;
    private LabParticleDetail particleDetail;
    private string particleDetailStatus = "NotStarted";
    private void StartParticleDetail()
    {
        particleDetail = null;
        particleDetailStatus = cpu == null ? "CpuStagesDisabled" : ParticleSpikeCapture.LabCaptureActive ? "ConflictingSpikeCapture" : "Enabled";
        if (particleDetailStatus != "Enabled") return;
        particleDetail = new LabParticleDetail(Capacity, cpu.DetailFamily); particleDetail.Start();
        lab.Status = "分析构建：几何/模拟交错抽样；细分耗时包含探针影响，不能作为优化前后基线。";
    }
    private void BeginParticleDetailFrame()
    {
        if (ParticleSpikeCapture.LabCaptureActive) { particleDetail?.Stop(); particleDetailStatus = "StoppedBySpikeCapture"; }
        else particleDetail?.BeginFrame();
    }
    private void CompleteParticleDetailFrame()
    {
        if (ParticleSpikeCapture.LabCaptureActive) { particleDetail?.Stop(); particleDetailStatus = "StoppedBySpikeCapture"; }
        else particleDetail?.CompleteFrame();
    }
    private object ParticleDetailJson()
    {
        int n = 0; double bridge = 0, geometry = 0, simulation = 0;
        if (particleDetail != null && cpu != null)
            for (int f = 0; f < cpu.FrameCount; f++) if (particleDetail.IsOrdinaryFrame(f))
            {
                n++; bridge += cpu.Get(f, LabCpuStage.BridgeUpdate).Ticks;
                geometry += cpu.Get(f, LabCpuStage.ParticleGeometry).SelfTicks;
                simulation += cpu.Get(f, LabCpuStage.SimulationSystemStep).SelfTicks;
            }
        double divisor = Math.Max(1, n) * (double)System.Diagnostics.Stopwatch.Frequency / 1000;
        return new { Available = true, Status = particleDetailStatus, FamilyNames = LabStressScenario.Names, Capture = particleDetail?.Snapshot(),
            OrdinaryFrameCpu = new { Frames = n, BridgeInclusiveMs = bridge / divisor,
                GeometrySelfMs = geometry / divisor, SimulationSystemStepSelfMs = simulation / divisor,
                Caveats = "Only complete non-detailed frames; analysis build still contains dormant probe branches. Use normal Release for final optimization comparisons." } };
    }
    private string ParticleDetailText()
    {
        var text = new StringBuilder("粒子细分测量（分析构建） / schema 1 / 状态：" + particleDetailStatus + "\n");
        if (particleDetail == null) return text.ToString();
        var report = particleDetail.Snapshot();
        text.AppendLine($"每 {report.Stride} 帧：几何偏移 0、模拟偏移 15；普通完整帧 {report.OrdinaryFrames}；无效 {report.InvalidFramesOrScopes}；跨线程调用 {report.CrossThreadCalls}。");
        text.AppendLine($"空 scope 校准 {report.EmptyScopeNanoseconds:F1} ns；数值缓冲 {report.BufferBytes} B。校准不从耗时扣除。");
        foreach (var domain in report.Domains)
        {
            text.AppendLine($"\n{domain.Domain}：{domain.SelectedFrames} 个有效选中帧；以下平均均按这些帧计算，包含零调用帧。");
            text.AppendLine("stage\tcalls\tcalls/selected_frame\tincl_ms\tself_ms\tp95_self_ms\tp99_self_ms\tmax_call_ms\tself_B/selected_frame");
            foreach (var row in domain.Stages)
                text.AppendLine(FormattableString.Invariant($"{row.Stage}\t{row.Calls}\t{row.CallsPerSelectedFrame:F3}\t{row.InclusiveMs:F6}\t{row.SelfMs:F6}\t{row.P95SelfMs:F6}\t{row.P99SelfMs:F6}\t{row.MaxCallMs:F6}\t{row.SelfBytes:F1}"));
            text.AppendLine("counter\tcount\tper_selected_frame");
            foreach (var row in domain.Work) text.AppendLine(FormattableString.Invariant($"{row.Counter}\t{row.Count}\t{row.PerSelectedFrame:F3}"));
            text.AppendLine("family\tstage\tcalls\tincl_ms/selected_frame\tself_ms/selected_frame");
            foreach (var row in domain.Families) text.AppendLine(FormattableString.Invariant($"{row.Family}\t{row.Stage}\t{row.Calls}\t{row.InclusiveMs:F6}\t{row.SelfMs:F6}"));
        }
        text.AppendLine(report.Caveats);
        return text.ToString();
    }
#else
    private const bool ParticleAnalysisBuild = false;
    private object ParticleDetailJson() => new { Available = false, Status = "NotCompiled" };
    private string ParticleDetailText() => "粒子细分测量：未编入当前正常 Release。";
#endif
}
