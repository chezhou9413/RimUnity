using System;
using System.Linq;

namespace RimUnity.Diagnostics;

internal sealed partial class LabSampler
{
    internal string MeasurementStopReason => stopReason;
    internal LabMeasurementPlan.Run MeasurementResult(LabMeasurementPlan.Job job)
    {
        var windows = LabCpuReport.Windows(cpu);
        var bridge = windows.FirstOrDefault(w => w.Name == "Bridge");
        var pre = windows.FirstOrDefault(w => w.Name == "PrePollToBridge");
        int n = 0; double systems = 0, particles = 0, points = 0, vertices = 0, indices = 0;
        for (int f = 0; f < count; f++) if (cpu == null || cpu.HasWindows(f))
        {
            var s = samples[f]; n++; systems += s.Systems; particles += s.Particles;
            points += s.TrailPoints; vertices += s.SubmittedVertices; indices += s.SubmittedIndices;
        }
        double divisor = Math.Max(1, n), particleMean = particles / divisor, variance = 0;
        for (int f = 0; f < count; f++) if (cpu == null || cpu.HasWindows(f)) variance += Math.Pow(samples[f].Particles - particleMean, 2);
        return new(job, lastReport, count, bridge?.CompleteFrames ?? 0, bridge?.MeanMs ?? 0, bridge?.StdDevMs ?? 0,
            bridge?.P95Ms ?? 0, bridge?.P99Ms ?? 0, systems / divisor, particleMean, points / divisor,
            vertices / divisor, indices / divisor, pre?.MeanMs ?? 0, Math.Sqrt(variance / divisor),
            bridge?.Gen0 ?? 0, bridge?.Gen1 ?? 0, bridge?.Gen2 ?? 0);
    }
}
