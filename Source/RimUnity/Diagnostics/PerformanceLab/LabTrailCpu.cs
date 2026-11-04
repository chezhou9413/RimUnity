using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RimUnity.Diagnostics;

// Numeric, main-thread-only capture owned by one explicit lab sample. No engine
// references, reflection, per-particle scopes or per-frame allocation. Stop clears
// the only static owner; frame capacity and registered identities are bounded.
internal sealed class LabTrailCpu
{
    internal enum Stage { Geometry, Submit, GlobalFlush }
    private struct Metric
    {
        internal long Calls, Ticks, Bytes;
        internal void Add(Metric x) { Calls += x.Calls; Ticks += x.Ticks; Bytes += x.Bytes; }
    }
    internal sealed record Row(string Stage, long Calls, double CallsPerFrame, double MeanMs, double? P95Ms, double? P99Ms, double? MaxMs, double BytesPerFrame);
    internal sealed record SystemRow(int Slot, int InstanceId, Row Geometry, Row Submit);
    internal sealed record Report(int Frames, int InvalidFrames, int UnknownSystemScopes, int WrongThreadScopes,
        long LegacyGeometryCalls, long CurrentGeometryCalls, Row[] Stages, SystemRow[] Systems, string Caveats, int Schema = 1);
    private static volatile LabTrailCpu current;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<int, int> slots = new();
    private readonly List<int> identities = new();
    private readonly Metric[] pending = new Metric[3], pendingSystems = new Metric[2048], systems = new Metric[2048];
    private readonly Metric[] frames;
    private int frameCount, epoch, open, invalid, unknown, wrongThread;
    private long pendingLegacy, pendingCurrent, legacyCalls, currentCalls;
    private bool stopped;
    private Report cached;

    internal LabTrailCpu(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        frames = new Metric[checked(capacity * 3)];
    }
    internal void Register(int id)
    {
        if (ReferenceEquals(current, this) || stopped) throw new InvalidOperationException("Register before capture starts.");
        if (slots.ContainsKey(id)) return;
        if (identities.Count == 1024) throw new InvalidOperationException("Trail capture identity limit reached.");
        slots.Add(id, identities.Count); identities.Add(id);
    }
    internal void Start()
    {
        if (current != null || stopped || Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Invalid trail capture owner.");
        current = this;
    }
    internal void Stop()
    {
        if (ReferenceEquals(current, this)) current = null;
        if (open != 0) invalid++;
        stopped = true; open = 0; epoch++;
        Array.Clear(pending); Array.Clear(pendingSystems); slots.Clear();
    }
    internal static Scope Measure(Stage stage, int systemId = 0, bool legacy = false)
    {
        var owner = current;
        if (owner == null) return default;
        if (Environment.CurrentManagedThreadId != owner.thread) { System.Threading.Interlocked.Increment(ref owner.wrongThread); return default; }
        int slot = -1;
        if (stage != Stage.GlobalFlush && !owner.slots.TryGetValue(systemId, out slot)) { owner.unknown++; return default; }
        owner.open++;
        if (stage == Stage.Geometry) { if (legacy) owner.pendingLegacy++; else owner.pendingCurrent++; }
        return new(owner, stage, slot, owner.epoch);
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly LabTrailCpu owner;
        private readonly Stage stage;
        private readonly int slot, epoch;
        private readonly long start, bytes;
        internal Scope(LabTrailCpu owner, Stage stage, int slot, int epoch)
        {
            this.owner = owner; this.stage = stage; this.slot = slot; this.epoch = epoch;
            bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            if (owner == null || owner.stopped || owner.epoch != epoch) return;
            if (Environment.CurrentManagedThreadId != owner.thread) { System.Threading.Interlocked.Increment(ref owner.wrongThread); return; }
            var value = new Metric { Calls = 1, Ticks = Stopwatch.GetTimestamp() - start, Bytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - bytes) };
            owner.pending[(int)stage].Add(value);
            if (slot >= 0) owner.pendingSystems[slot * 2 + (int)stage].Add(value);
            owner.open--;
        }
    }
    internal void CompleteFrame(bool validWindow)
    {
        if (open != 0) { invalid++; validWindow = false; }
        if (validWindow)
        {
            if ((frameCount + 1) * 3 > frames.Length) throw new InvalidOperationException("Trail frame capacity reached.");
            Array.Copy(pending, 0, frames, frameCount * 3, 3); frameCount++;
            for (int i = 0; i < identities.Count * 2; i++) systems[i].Add(pendingSystems[i]);
            legacyCalls += pendingLegacy; currentCalls += pendingCurrent;
        }
        Array.Clear(pending); Array.Clear(pendingSystems, 0, identities.Count * 2);
        pendingLegacy = pendingCurrent = 0; open = 0; epoch++;
    }
    internal Report Snapshot()
    {
        if (stopped && cached != null) return cached;
        double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
        Row Summarize(Stage stage, Metric total, double[] values)
        {
            Array.Sort(values); int n = Math.Max(1, frameCount);
            double? Q(double p) => values.Length == 0 ? null : values[Math.Max(0, (int)Math.Ceiling(values.Length * p) - 1)];
            return new(stage.ToString(), total.Calls, total.Calls / (double)n, Ms(total.Ticks) / n, Q(.95), Q(.99), Q(1), total.Bytes / (double)n);
        }
        var rows = new Row[3];
        for (int s = 0; s < 3; s++)
        {
            var total = new Metric(); var values = new double[frameCount];
            for (int f = 0; f < frameCount; f++) { var x = frames[f * 3 + s]; total.Add(x); values[f] = Ms(x.Ticks); }
            rows[s] = Summarize((Stage)s, total, values);
        }
        // Per-system rows report totals/means only. Their zero quantiles are not
        // emitted: use the aggregate frame-stage distribution above instead.
        var perSystem = identities.Select((id, slot) => new SystemRow(slot, id,
            Summarize(Stage.Geometry, systems[slot * 2], Array.Empty<double>()),
            Summarize(Stage.Submit, systems[slot * 2 + 1], Array.Empty<double>()))).ToArray();
        var report = new Report(frameCount, invalid, unknown, wrongThread, legacyCalls, currentCalls, rows, perSystem,
            "CPU elapsed time/allocation only. Geometry excludes synchronous Submit. Submit includes batching and any flush it triggers; GlobalFlush includes all particle pages, may overlap Submit, and is not attributable to one system. Never add GlobalFlush to Submit or these stages to Bridge. Only registered lab system IDs enter Geometry/Submit; GlobalFlush may include background objects. Per-system quantiles are unavailable. Both variants retain these coarse probes; not zero-probe, render-thread or GPU time.");
        if (stopped) cached = report;
        return report;
    }
}
