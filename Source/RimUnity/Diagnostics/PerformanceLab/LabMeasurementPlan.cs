using System;
using System.Collections.Generic;
using System.Linq;

namespace RimUnity.Diagnostics;

// Pure diagnostic plan/statistics. No engine state, global caches or runtime objects.
internal static class LabMeasurementPlan
{
    internal sealed record Job(string Name, LabCpuMode Mode, int Roots, int ParticlesPerRoot, int Family = -1, string Group = "mixed");
    // Same configurations as the full suite, for before/after geometry checks.
    internal static Job[] GeometryJobs() => new[]
    {
        new Job("bridge-A1", LabCpuMode.BridgeOnly, 48, 64),
        new Job("trails-256", LabCpuMode.BridgeOnly, 4, 256, 5, "trails")
    };
    internal static Job[] Jobs() => new[]
    {
        new Job("bridge-repeat", LabCpuMode.BridgeOnly, 48, 64),
        new Job("bridge-A1", LabCpuMode.BridgeOnly, 48, 64),
        new Job("stages-B1", LabCpuMode.Stages, 48, 64),
        new Job("bridge-A2", LabCpuMode.BridgeOnly, 48, 64),
        new Job("stages-B2", LabCpuMode.Stages, 48, 64),
        new Job("particles-16", LabCpuMode.BridgeOnly, 48, 16),
        new Job("particles-256", LabCpuMode.BridgeOnly, 48, 256),
        new Job("systems-24", LabCpuMode.BridgeOnly, 24, 128),
        new Job("systems-96", LabCpuMode.BridgeOnly, 96, 32),
        new Job("trails-16", LabCpuMode.BridgeOnly, 4, 16, 5, "trails"),
        new Job("trails-64", LabCpuMode.BridgeOnly, 4, 64, 5, "trails"),
        new Job("trails-256", LabCpuMode.BridgeOnly, 4, 256, 5, "trails"),
        new Job("off-reference", LabCpuMode.Off, 48, 64, -1, "off")
    };

    internal sealed record Run(Job Job, string Report, int Frames, int ValidBridgeFrames,
        double BridgeMeanMs, double BridgeStdDevMs, double BridgeP95Ms, double BridgeP99Ms,
        double Systems, double Particles, double TrailPoints, double Vertices, double Indices,
        double PrePollToBridgeMs, double ParticleStdDev, long BridgeGen0, long BridgeGen1, long BridgeGen2)
    { public bool BridgeAvailable => ValidBridgeFrames > 0; }

    internal sealed record ProbeComparison(string Status, double StageMinusBridgeMs,
        double ObservedMeanSpreadMs, double StageMinusBridgeP95Ms, double StageMinusBridgeP99Ms,
        double MaxRelativeParticleDifference, double MaxRelativeGeometryDifference);

    internal static ProbeComparison Compare(IReadOnlyList<Run> runs)
    {
        var a = runs.Where(r => r.Job.Name.StartsWith("bridge-A", StringComparison.Ordinal)).ToArray();
        var b = runs.Where(r => r.Job.Name.StartsWith("stages-B", StringComparison.Ordinal)).ToArray();
        if (a.Length != 2 || b.Length != 2 || a.Concat(b).Any(r => r.ValidBridgeFrames < 2))
            return new("Incomplete", 0, 0, 0, 0, 0, 0);
        double delta = b.Average(r => r.BridgeMeanMs) - a.Average(r => r.BridgeMeanMs);
        double spread = Math.Max(Math.Abs(a[0].BridgeMeanMs - a[1].BridgeMeanMs), Math.Abs(b[0].BridgeMeanMs - b[1].BridgeMeanMs));
        var repeat = runs.FirstOrDefault(r => r.Job.Name == "bridge-repeat" && r.ValidBridgeFrames > 1);
        if (repeat != null) spread = Math.Max(spread, Math.Abs(repeat.BridgeMeanMs - a[0].BridgeMeanMs));
        var combined = repeat == null ? a.Concat(b).ToArray() : a.Concat(b).Append(repeat).ToArray();
        double particles = combined.Average(r => r.Particles);
        double mismatch = (combined.Max(r => r.Particles) - combined.Min(r => r.Particles)) / Math.Max(1, particles);
        double Relative(Func<Run, double> selector) => (combined.Max(selector) - combined.Min(selector)) / Math.Max(1, combined.Average(selector));
        double geometryMismatch = Math.Max(Relative(r => r.TrailPoints), Math.Max(Relative(r => r.Vertices), Relative(r => r.Indices)));
        bool unequalSystems = combined.Max(r => r.Systems) != combined.Min(r => r.Systems);
        string status = mismatch > .05 || geometryMismatch > .05 || unequalSystems ? "WorkloadMismatch" : Math.Abs(delta) <= spread ? "WithinObservedSpread"
            : delta < 0 ? "NegativeDifferenceInvestigateDrift" : "AboveObservedSpreadNotConfidenceInterval";
        return new(status, delta, spread, b.Average(r => r.BridgeP95Ms) - a.Average(r => r.BridgeP95Ms),
            b.Average(r => r.BridgeP99Ms) - a.Average(r => r.BridgeP99Ms), mismatch, geometryMismatch);
    }

    internal sealed record Fit(string Status, int Runs, double SystemMs, double ParticleMs, double InterceptMs, double RSquared, double RmseMs);
    internal static Fit FitMixed(IReadOnlyList<Run> runs)
    {
        var rows = runs.Where(r => r.Job.Group == "mixed" && r.Job.Mode == LabCpuMode.BridgeOnly && r.ValidBridgeFrames > 1).ToArray();
        if (rows.Length < 4) return new("InsufficientRuns", rows.Length, 0, 0, 0, 0, 0);
        double sx = rows.Average(r => r.Systems), px = rows.Average(r => r.Particles), y = rows.Average(r => r.BridgeMeanMs);
        double ss = 0, pp = 0, sp = 0, sy = 0, py = 0, yy = 0;
        foreach (var r in rows)
        {
            double a = r.Systems - sx, b = r.Particles - px, c = r.BridgeMeanMs - y;
            ss += a * a; pp += b * b; sp += a * b; sy += a * c; py += b * c; yy += c * c;
        }
        double det = ss * pp - sp * sp;
        if (ss <= 0 || pp <= 0 || det <= ss * pp * 1e-10) return new("RankDeficient", rows.Length, 0, 0, 0, 0, 0);
        double sa = (sy * pp - py * sp) / det, pb = (py * ss - sy * sp) / det, intercept = y - sa * sx - pb * px;
        double error = rows.Sum(r => Math.Pow(r.BridgeMeanMs - (sa * r.Systems + pb * r.Particles + intercept), 2));
        return new(sa < 0 || pb < 0 ? "NegativeSlopeDoNotInterpretAsUnitCost" : "DescriptiveMixedWorkloadOnly",
            rows.Length, sa, pb, intercept, yy > 0 ? 1 - error / yy : 0, Math.Sqrt(error / rows.Length));
    }

    internal sealed record TrailFit(string Status, int Runs, double MsPerPoint, double InterceptMs, double RmseMs);
    internal static TrailFit FitTrails(IReadOnlyList<Run> runs)
    {
        var rows = runs.Where(r => r.Job.Group == "trails" && r.ValidBridgeFrames > 1).ToArray();
        if (rows.Length < 3) return new("InsufficientRuns", rows.Length, 0, 0, 0);
        double x = rows.Average(r => r.TrailPoints), y = rows.Average(r => r.BridgeMeanMs);
        double xx = rows.Sum(r => Math.Pow(r.TrailPoints - x, 2));
        if (xx <= 0) return new("NoPointVariation", rows.Length, 0, 0, 0);
        double slope = rows.Sum(r => (r.TrailPoints - x) * (r.BridgeMeanMs - y)) / xx, intercept = y - slope * x;
        double error = rows.Sum(r => Math.Pow(r.BridgeMeanMs - slope * r.TrailPoints - intercept, 2));
        return new("WholeBridgeAssociationNotIsolatedTrailCost", rows.Length, slope, intercept, Math.Sqrt(error / rows.Length));
    }
}
