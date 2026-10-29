using System;
using System.Diagnostics;

namespace RimUnity.Diagnostics;

// Only evaluated after collection stops. Percentiles include frames with zero calls.
internal static class LabCpuReport
{
    internal sealed record Stage(string Name, long Calls, double CallsPerFrame,
        double MeanInclusiveMs, double MeanSelfMs, double P95InclusiveMs, double P99InclusiveMs,
        double P95SelfMs, double P99SelfMs, double MaxFrameInclusiveMs, double MaxCallMs,
        double InclusiveBytesPerFrame, double SelfBytesPerFrame, long Vertices, long Indices);
    internal sealed record BatchCounter(string Name, long Count, double PerFrame);
    internal sealed record FrameWindow(string Name, int CompleteFrames, double MeanMs, double P95Ms, double MaxMs, double BytesPerFrame, double P99Ms, double StdDevMs, long Gen0, long Gen1, long Gen2)
    { public bool GcCountersAvailable => Name == "Bridge"; }
    internal static FrameWindow[] Windows(LabCpuMetrics cpu)
    {
        if (cpu == null) return Array.Empty<FrameWindow>();
        int complete = 0;
        for (int f = 0; f < cpu.FrameCount; f++) if (cpu.HasWindows(f)) complete++;
        if (complete == 0) return Array.Empty<FrameWindow>();
        var result = new FrameWindow[(int)LabFrameWindow.Count];
        var ticks = new long[complete];
        for (int w = 0; w < result.Length; w++)
        {
            long totalTicks = 0, bytes = 0, g0 = 0, g1 = 0, g2 = 0; int index = 0;
            for (int f = 0; f < cpu.FrameCount; f++)
                if (cpu.HasWindows(f))
                {
                    var value = cpu.GetWindow(f, (LabFrameWindow)w);
                    ticks[index++] = value.Ticks; totalTicks += value.Ticks; bytes += value.Bytes;
                    g0 += value.Gen0; g1 += value.Gen1; g2 += value.Gen2;
                }
            Array.Sort(ticks);
            int p95 = Math.Clamp((int)Math.Ceiling(complete * .95) - 1, 0, complete - 1);
            double variance = 0, mean = totalTicks / (double)complete;
            foreach (long value in ticks) variance += (value - mean) * (value - mean);
            int p99 = Math.Clamp((int)Math.Ceiling(complete * .99) - 1, 0, complete - 1);
            result[w] = new FrameWindow(((LabFrameWindow)w).ToString(), complete, Ms(totalTicks) / complete,
                Ms(ticks[p95]), Ms(ticks[^1]), bytes / (double)complete, Ms(ticks[p99]), Math.Sqrt(variance / complete) * 1000d / Stopwatch.Frequency, g0, g1, g2);
        }
        return result;
    }
    internal static Stage[] Stages(LabCpuMetrics cpu)
    {
        if (cpu == null || !cpu.StagesEnabled || cpu.FrameCount == 0) return Array.Empty<Stage>();
        int frames = cpu.FrameCount;
        var result = new Stage[LabCpuMetrics.StageCount];
        var inclusive = new long[frames]; var self = new long[frames];
        for (int s = 0; s < result.Length; s++)
        {
            long calls = 0, ticks = 0, selfTicks = 0, bytes = 0, selfBytes = 0, maximum = 0, vertices = 0, indices = 0;
            for (int f = 0; f < frames; f++)
            {
                var m = cpu.Get(f, (LabCpuStage)s);
                inclusive[f] = m.Ticks; self[f] = m.SelfTicks;
                calls += m.Calls; ticks += m.Ticks; selfTicks += m.SelfTicks;
                bytes += m.Bytes; selfBytes += m.SelfBytes; maximum = Math.Max(maximum, m.MaxCallTicks);
                vertices += m.Vertices; indices += m.Indices;
            }
            Array.Sort(inclusive); Array.Sort(self);
            int p95 = Math.Clamp((int)Math.Ceiling(frames * .95) - 1, 0, frames - 1);
            int p99 = Math.Clamp((int)Math.Ceiling(frames * .99) - 1, 0, frames - 1);
            result[s] = new Stage(((LabCpuStage)s).ToString(), calls, calls / (double)frames,
                Ms(ticks) / frames, Ms(selfTicks) / frames, Ms(inclusive[p95]), Ms(inclusive[p99]),
                Ms(self[p95]), Ms(self[p99]), Ms(inclusive[^1]), Ms(maximum),
                bytes / (double)frames, selfBytes / (double)frames, vertices, indices);
        }
        return result;
    }
    internal static BatchCounter[] Reasons(LabCpuMetrics cpu)
    {
        if (cpu == null || !cpu.StagesEnabled || cpu.FrameCount == 0) return Array.Empty<BatchCounter>();
        var result = new BatchCounter[LabCpuMetrics.ReasonCount];
        for (int r = 0; r < result.Length; r++)
        {
            long total = 0;
            for (int f = 0; f < cpu.FrameCount; f++) total += cpu.Reason(f, (LabBatchReason)r);
            result[r] = new BatchCounter(((LabBatchReason)r).ToString(), total, total / (double)cpu.FrameCount);
        }
        return result;
    }
    internal static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
