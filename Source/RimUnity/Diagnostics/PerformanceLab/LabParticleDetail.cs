#if RIMUNITY_PARTICLE_ANALYSIS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RimUnity.Diagnostics;

internal enum ParticleDetailDomain { None, Geometry, Simulation }
internal enum ParticleDetailStage
{
    Geometry, Attributes, Space, Size, Color, Custom, Atlas, Axes, Projection,
    Mesh, MeshSelect, MeshPose, MeshDirections, MeshVertices, StreamPrepare, StreamPack, VertexWrite, IndexWrite,
    TrailGeometry, TrailPositions, TrailAppearance, TrailSegments, GeometryNoise, Submit,
    Simulation, Schedule, SystemStep, EmissionSchedule, EmissionTimeline, Spawn,
    AdvanceParticles, Integrate, Forces, Velocity, LimitDrag, Noise, Rotation,
    Physics, PhysicsCandidates, TrailAging, TrailRecord, SubMotion, SubAdvance,
    DeathRelease, CapacityReclaim, NoiseModule, Count
}
internal enum ParticleDetailWork
{
    ParticleVisits, Integrations, Births, SpawnAttempts, Deaths, CapacityVisits, EmissionScheduleVisits, CurveCalls,
    CurveSamples, GradientCalls, GradientSamples, NoiseCalls, NoiseOctaves, SpaceConversions, TrailPointVisits,
    TrailPointsAdded, Vertices, Indices, BufferGrowths, Count
}

// One explicit lab session, main thread only. Numeric buffers and numeric family
// identities only; no scene objects. Normal builds contain neither this type nor probes.
internal sealed class LabParticleDetail
{
    internal const int Stride = 31, Families = 13;
    private const int Stages = (int)ParticleDetailStage.Count, Works = (int)ParticleDetailWork.Count;
    private static LabParticleDetail current;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly Func<int, int> familyFor;
    private struct Metric { internal long Calls, Ticks, SelfTicks, Bytes, SelfBytes, MaxTicks; }
    private struct Entry { internal int Stage, Family; internal long Start, Bytes, Children, ChildBytes, Serial; }
    private readonly Entry[] stack = new Entry[32];
    private readonly Metric[] pending = new Metric[Stages], familyPending = new Metric[Families * Stages];
    private readonly Metric[] familyTotals = new Metric[2 * Families * Stages];
    private readonly long[] work = new long[Works], workTotals = new long[2 * Works];
    private readonly Metric[] frames;
    private readonly byte[] domains;
    private readonly byte[] frameKinds;
    private readonly int[] ordinals;
    private readonly long[] scopesPerFrame;
    private int depth, recorded, ordinal;
    private long serial, invalid, crossThread, frameScopes;
    private bool open, valid, stopped;
    private ParticleDetailDomain domain;
    internal long OrdinaryFrames { get; private set; }
    internal double EmptyScopeNanoseconds { get; private set; }
    internal long BufferBytes { get; }

    internal LabParticleDetail(int capacity, Func<int, int> familyFor = null)
    {
        if (capacity < 1 || capacity > 36000) throw new ArgumentOutOfRangeException(nameof(capacity));
        int selected = checked(2 * ((capacity + Stride - 1) / Stride) + 2);
        frameKinds = new byte[capacity];
        frames = new Metric[checked(selected * Stages)]; domains = new byte[selected];
        ordinals = new int[selected]; scopesPerFrame = new long[selected]; this.familyFor = familyFor;
        BufferBytes = (long)frames.Length * 48 + (pending.Length + familyPending.Length + familyTotals.Length) * 48L
            + frameKinds.Length + stack.Length * 56L + (work.Length + workTotals.Length) * 8L + selected * 13L;
        if (BufferBytes > 16 * 1024 * 1024) throw new InvalidOperationException("Particle detail buffer exceeds 16 MiB.");
    }
    internal void Start()
    {
        if (current != null || stopped || Environment.CurrentManagedThreadId != thread)
            throw new InvalidOperationException("Particle detail session already active or retired.");
        current = this;
        // Calibration uses the real entry/exit path, before the lab sample clock starts.
        open = valid = true; domain = ParticleDetailDomain.Geometry;
        for (int i = 0; i < 1000; i++) { using var scope = Measure(ParticleDetailStage.Geometry); }
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 10000; i++) { using var scope = Measure(ParticleDetailStage.Geometry); }
        EmptyScopeNanoseconds = (Stopwatch.GetTimestamp() - start) * 1e9 / Stopwatch.Frequency / 10000;
        ClearPending(); open = false; domain = ParticleDetailDomain.None;
    }
    internal void BeginFrame()
    {
        if (stopped || Environment.CurrentManagedThreadId != thread) return;
        if (open) { valid = false; invalid++; return; }
        open = valid = true;
        domain = ordinal % Stride == 0 ? ParticleDetailDomain.Geometry
            : ordinal % Stride == 15 ? ParticleDetailDomain.Simulation : ParticleDetailDomain.None;
    }
    internal void CompleteFrame()
    {
        if (stopped || Environment.CurrentManagedThreadId != thread) return;
        if (open && valid && depth == 0)
        {
            if (domain == ParticleDetailDomain.None) { OrdinaryFrames++; if (ordinal < frameKinds.Length) frameKinds[ordinal] = 3; }
            else if (recorded < domains.Length)
            {
                if (ordinal < frameKinds.Length) frameKinds[ordinal] = (byte)domain;
                domains[recorded] = (byte)domain; ordinals[recorded] = ordinal + 1; scopesPerFrame[recorded] = frameScopes;
                Array.Copy(pending, 0, frames, recorded * Stages, Stages);
                int offset = ((int)domain - 1) * familyPending.Length;
                for (int i = 0; i < familyPending.Length; i++) Add(ref familyTotals[offset + i], familyPending[i]);
                offset = ((int)domain - 1) * Works;
                for (int i = 0; i < Works; i++) workTotals[offset + i] += work[i];
                recorded++;
            }
            else invalid++;
        }
        else if (open && depth != 0) invalid++;
        ClearPending(); open = false; domain = ParticleDetailDomain.None; ordinal++;
    }
    internal bool IsOrdinaryFrame(int frame) => (uint)frame < frameKinds.Length && frameKinds[frame] == 3;
    internal void WriteFrames(string path)
    {
        using var writer = new System.IO.StreamWriter(path, false, new System.Text.UTF8Encoding(false));
        writer.WriteLine("frame,domain,stage,calls,inclusive_ms,self_ms,self_bytes,scope_count");
        for (int f = 0; f < recorded; f++) for (int s = 0; s < Stages; s++)
        {
            var m = frames[f * Stages + s];
            writer.WriteLine(FormattableString.Invariant($"{ordinals[f]},{(ParticleDetailDomain)domains[f]},{(ParticleDetailStage)s},{m.Calls},{Ms(m.Ticks):F6},{Ms(m.SelfTicks):F6},{m.SelfBytes},{scopesPerFrame[f]}"));
        }
    }
    internal void Stop()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Particle detail stop must run on its owner thread.");
        if (ReferenceEquals(current, this)) current = null;
        if (depth != 0) invalid++;
        stopped = true; ClearPending(); open = false; domain = ParticleDetailDomain.None;
    }
    private void ClearPending()
    {
        Array.Clear(pending); Array.Clear(familyPending); Array.Clear(work); depth = 0; frameScopes = 0;
    }
    internal static Scope MeasureNoise() => Measure(current?.domain == ParticleDetailDomain.Geometry
        ? ParticleDetailStage.GeometryNoise : ParticleDetailStage.Noise);

    internal static Scope Measure(ParticleDetailStage stage, int systemId = 0)
    {
        var c = current;
        if (c == null || !c.open || !c.valid || c.domain == ParticleDetailDomain.None) return default;
        if (Environment.CurrentManagedThreadId != c.thread) { Interlocked.Increment(ref c.crossThread); return default; }
        bool simulation = stage >= ParticleDetailStage.Simulation;
        if (simulation != (c.domain == ParticleDetailDomain.Simulation)) return default;
        if (c.depth == c.stack.Length) { c.invalid++; c.valid = false; return default; }
        int slot = c.depth++;
        int family = slot == 0 ? Families - 1 : c.stack[slot - 1].Family;
        if (systemId != 0) family = Math.Clamp(c.familyFor?.Invoke(systemId) ?? Families - 1, 0, Families - 1);
        long id = ++c.serial;
        c.stack[slot] = new Entry { Stage = (int)stage, Family = family, Serial = id,
            Bytes = GC.GetAllocatedBytesForCurrentThread(), Start = Stopwatch.GetTimestamp() };
        c.frameScopes++;
        return new Scope(c, slot, id);
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly LabParticleDetail owner;
        private readonly int slot;
        private readonly long serial;
        internal Scope(LabParticleDetail owner, int slot, long serial) { this.owner = owner; this.slot = slot; this.serial = serial; }
        public void Dispose() => owner?.End(slot, serial);
    }
    private void End(int slot, long id)
    {
        if (stopped) return;
        if (Environment.CurrentManagedThreadId != thread) { Interlocked.Increment(ref crossThread); return; }
        if (depth != slot + 1 || stack[slot].Serial != id) { invalid++; valid = false; return; }
        ref var e = ref stack[slot];
        long ticks = Stopwatch.GetTimestamp() - e.Start, bytes = GC.GetAllocatedBytesForCurrentThread() - e.Bytes;
        var m = new Metric { Calls = 1, Ticks = ticks, SelfTicks = Math.Max(0, ticks - e.Children),
            Bytes = bytes, SelfBytes = Math.Max(0, bytes - e.ChildBytes), MaxTicks = ticks };
        Add(ref pending[e.Stage], m); Add(ref familyPending[e.Family * Stages + e.Stage], m);
        if (--depth > 0) { stack[depth - 1].Children += ticks; stack[depth - 1].ChildBytes += bytes; }
    }
    internal static void Work(ParticleDetailWork counter, long amount = 1)
    {
        var c = current;
        if (c == null || !c.open || !c.valid || c.depth == 0) return;
        if (Environment.CurrentManagedThreadId != c.thread) { Interlocked.Increment(ref c.crossThread); return; }
        c.work[(int)counter] += amount;
    }
    private static void Add(ref Metric a, Metric b)
    {
        a.Calls += b.Calls; a.Ticks += b.Ticks; a.SelfTicks += b.SelfTicks;
        a.Bytes += b.Bytes; a.SelfBytes += b.SelfBytes; a.MaxTicks = Math.Max(a.MaxTicks, b.MaxTicks);
    }
    internal sealed record StageRow(string Stage, long Calls, double CallsPerSelectedFrame, double InclusiveMs,
        double SelfMs, double P95SelfMs, double P99SelfMs, double MaxCallMs, double SelfBytes)
    { public string Status => Calls == 0 ? "NoCallsInSelectedFrames" : "Measured"; }
    internal sealed record FamilyRow(int Family, string Stage, long Calls, double InclusiveMs, double SelfMs);
    internal sealed record WorkRow(string Counter, long Count, double PerSelectedFrame);
    internal sealed record DomainReport(string Domain, int SelectedFrames, long ScopeCount, StageRow[] Stages, FamilyRow[] Families, WorkRow[] Work);
    internal sealed record Report(int Schema, int Stride, int GeometryOffset, int SimulationOffset, long OrdinaryFrames,
        long InvalidFramesOrScopes, long CrossThreadCalls, long BufferBytes, double EmptyScopeNanoseconds,
        DomainReport[] Domains, string Caveats);
    internal Report Snapshot()
    {
        var reports = new List<DomainReport>();
        for (int domainId = 1; domainId <= 2; domainId++)
        {
            int n = 0; long scopes = 0;
            for (int f = 0; f < recorded; f++) if (domains[f] == domainId) { n++; scopes += scopesPerFrame[f]; }
            var stages = new List<StageRow>(); var families = new List<FamilyRow>(); var counters = new List<WorkRow>();
            for (int stage = 0; stage < Stages; stage++)
            {
                if ((stage >= (int)ParticleDetailStage.Simulation) != (domainId == 2)) continue;
                Metric total = default; var self = new long[n]; int at = 0;
                for (int f = 0; f < recorded; f++) if (domains[f] == domainId)
                { var m = frames[f * Stages + stage]; Add(ref total, m); self[at++] = m.SelfTicks; }
                Array.Sort(self);
                double d = Math.Max(1, n);
                stages.Add(new StageRow(((ParticleDetailStage)stage).ToString(), total.Calls, total.Calls / d,
                    Ms(total.Ticks) / d, Ms(total.SelfTicks) / d, n == 0 ? 0 : Ms(self[(int)Math.Ceiling(n * .95) - 1]),
                    n == 0 ? 0 : Ms(self[(int)Math.Ceiling(n * .99) - 1]), Ms(total.MaxTicks), total.SelfBytes / d));
                for (int family = 0; family < Families; family++)
                {
                    var m = familyTotals[((domainId - 1) * Families + family) * Stages + stage];
                    if (m.Calls != 0) families.Add(new FamilyRow(family, ((ParticleDetailStage)stage).ToString(), m.Calls, Ms(m.Ticks) / d, Ms(m.SelfTicks) / d));
                }
            }
            for (int i = 0; i < Works; i++)
            { long value = workTotals[(domainId - 1) * Works + i]; counters.Add(new WorkRow(((ParticleDetailWork)i).ToString(), value, value / (double)Math.Max(1, n))); }
            reports.Add(new DomainReport(((ParticleDetailDomain)domainId).ToString(), n, scopes, stages.ToArray(), families.ToArray(), counters.ToArray()));
        }
        return new Report(1, Stride, 0, 15, OrdinaryFrames, invalid, crossThread, BufferBytes, EmptyScopeNanoseconds,
            reports.ToArray(), "Analysis build only; CPU main thread, not GPU. Raw probe-inclusive timings, not an optimization baseline. Means/percentiles use completed selected frames of that domain, including zero-call stages. Inclusive contains children; do not sum. Self retains uninstrumented work and probe overhead. Geometry and simulation use different frames. OrdinaryFrames excludes selected/invalid/partial frames. Family 12 means unknown. Empty scope calibration is not subtracted. Sampling can alias periodic work; repeat with a different camera/workload when needed.");
    }
    private static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
#endif
