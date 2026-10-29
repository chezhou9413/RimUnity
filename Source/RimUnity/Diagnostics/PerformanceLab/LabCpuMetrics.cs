using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;

namespace RimUnity.Diagnostics;

// Main-thread source scopes, owned by one explicit lab sample. No engine/resource references.
// Buffers are allocated before the sample clock starts and released with its report.
internal enum LabCpuMode { Off, BridgeOnly, Stages }

internal enum LabCpuStage
{
    BridgeUpdate, ParticleSimulation, SceneDraw, ParticleRenderer, ParticleSort,
    ParticleGeometry, TrailGeometry, SpriteMaterial, ParticleSubmit, SubmitUI,
    MeshFill, DiagnosticCapture, DrawMesh, BatchAttempt, BatchGeometry, BatchFlush,
    HdrSnapshot, BloomSnapshot, SimulationSchedule, SimulationAdvance, SimulationEvents,
    ParticleAttributes, SimulationSystemStep, Count
}

internal enum LabFrameWindow { EnqueueToPrePoll, PrePollToBridge, Bridge, BridgeToEnqueue, Count }

// One first decisive rejection/boundary per attempt; Accepted is not a rejection.
internal enum LabBatchReason
{
    Accepted, Inactive, DebugCapture, PropertyBlock, StorageBindings, InputVertexLimit,
    InputIndexLimit, MultiplePasses, UnknownShader, UnsafeShader,
    MaterialChanged, LayerChanged, MaterialVersionChanged, BatchVertexLimit, BatchIndexLimit,
    UI, CameraUI, Empty, Count
}

internal sealed class LabCpuMetrics
{
    internal struct Metric
    {
        public long Calls { get; set; }
        public long Ticks { get; set; }
        public long SelfTicks { get; set; }
        public long Bytes { get; set; }
        public long SelfBytes { get; set; }
        public long MaxCallTicks { get; set; }
        public long Vertices { get; set; }
        public long Indices { get; set; }
    }
    private struct Entry
    {
        internal LabCpuStage Stage;
        internal int Family;
        internal long Start, Allocated, ChildTicks, ChildBytes, Serial;
    }
    internal const int StageCount = (int)LabCpuStage.Count, ReasonCount = (int)LabBatchReason.Count;
    private const int StackCapacity = 32;
    private static volatile LabCpuMetrics current;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly Entry[] stack = new Entry[StackCapacity];
    private readonly Metric[] pending = new Metric[StageCount];
    // Numeric identities only; registered before the sample starts. Family totals
    // contain completed frames only and do not retain scene objects or per-frame rows.
    internal const int FamilyCount = 12;
    private readonly Dictionary<int, int> systemFamilies = new();
    private readonly Metric[] pendingFamilies = new Metric[FamilyCount * StageCount];
    private readonly Metric[] familyTotals = new Metric[FamilyCount * StageCount];
    private readonly long[] reasons = new long[ReasonCount];
    private readonly Metric[] frames;
    private readonly long[] frameReasons;
    internal readonly record struct SnapshotWork(long HdrCalls, long HdrCommands, long BloomCalls, long BloomCommands, int BloomMask);
    private SnapshotWork pendingSnapshot;
    private readonly SnapshotWork[] snapshotFrames;
    internal readonly record struct Window(long Ticks, long Bytes, int Gen0 = 0, int Gen1 = 0, int Gen2 = 0);
    private int bridgeGen0, bridgeGen1, bridgeGen2, bridgeEndGen0, bridgeEndGen1, bridgeEndGen2;
    internal LabCpuMode Mode { get; }
    internal bool StagesEnabled => Mode == LabCpuMode.Stages;
    private readonly Window[] frameWindows;
    private readonly bool[] validWindows;
    private long lastEnqueue, lastEnqueueBytes, prePoll, prePollBytes, bridgeStart, bridgeStartBytes, bridgeEnd, bridgeEndBytes;
    private int prePollVisits, bridgeVisits;
    private int depth;
    private long serial;
    private bool stopped;
    internal int FrameCount { get; private set; }
    internal long InvalidScopes { get; private set; }
    internal long CrossThreadScopes => Interlocked.Read(ref crossThreadScopes);
    private long crossThreadScopes;

    internal LabCpuMetrics(int capacity, LabCpuMode mode = LabCpuMode.Stages)
    {
        if (mode == LabCpuMode.Off) throw new ArgumentOutOfRangeException(nameof(mode));
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Mode = mode;
        frames = StagesEnabled ? new Metric[checked(capacity * StageCount)] : Array.Empty<Metric>();
        frameReasons = StagesEnabled ? new long[checked(capacity * ReasonCount)] : Array.Empty<long>();
        snapshotFrames = new SnapshotWork[capacity];
        frameWindows = new Window[checked(capacity * (int)LabFrameWindow.Count)];
        validWindows = new bool[capacity];
    }
    internal int DetailFamily(int systemId) => systemFamilies.TryGetValue(systemId, out int family) ? family : 12;

    internal void Start()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("CPU capture must start on its owner thread.");
        if (current != null || stopped) throw new InvalidOperationException("CPU capture already active or retired.");
        current = this;
    }
    internal void Stop()
    {
        if (ReferenceEquals(current, this)) current = null;
        stopped = true;
        // Unfinished tail scopes do not enter completed frame statistics.
        if (depth != 0) { InvalidScopes += depth; depth = 0; }
        Array.Clear(pending); Array.Clear(pendingFamilies); systemFamilies.Clear(); Array.Clear(reasons); pendingSnapshot = default;
    }
    internal void RegisterSystem(int instanceId, int family)
    {
        if (stopped || ReferenceEquals(current, this) || Environment.CurrentManagedThreadId != thread)
            throw new InvalidOperationException("Register diagnostic identities before capture starts.");
        if (StagesEnabled && (uint)family < FamilyCount && systemFamilies.Count < 1024) systemFamilies[instanceId] = family;
    }
    internal Metric Family(int family, LabCpuStage stage) => familyTotals[family * StageCount + (int)stage];
    internal static Scope Measure(LabCpuStage stage)
        => Begin(stage, 0, false);
    internal static Scope MeasureSystem(LabCpuStage stage, int instanceId)
        => Begin(stage, instanceId, true);
    private static Scope Begin(LabCpuStage stage, int instanceId, bool identifySystem)
    {
        var capture = current;
        if (capture == null || !capture.StagesEnabled) return default;
        if (Environment.CurrentManagedThreadId != capture.thread)
        { Interlocked.Increment(ref capture.crossThreadScopes); return default; }
        if (capture.depth == StackCapacity) { capture.InvalidScopes++; return default; }
        int slot = capture.depth++;
        int family = slot > 0 ? capture.stack[slot - 1].Family : -1;
        if (identifySystem) family = capture.systemFamilies.TryGetValue(instanceId, out int registered) ? registered : -1;
        long id = ++capture.serial;
        capture.stack[slot] = new Entry { Stage = stage, Serial = id, Family = family,
            Allocated = GC.GetAllocatedBytesForCurrentThread(), Start = Stopwatch.GetTimestamp() };
        return new Scope(capture, slot, id);
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly LabCpuMetrics owner;
        private readonly int slot;
        private readonly long serial;
        internal Scope(LabCpuMetrics owner, int slot, long serial) { this.owner = owner; this.slot = slot; this.serial = serial; }
        public void Dispose() { if (owner != null) owner.End(slot, serial); }
    }
    private void End(int slot, long id)
    {
        if (stopped) return;
        if (Environment.CurrentManagedThreadId != thread) { Interlocked.Increment(ref crossThreadScopes); return; }
        if (depth != slot + 1 || stack[slot].Serial != id)
        { InvalidScopes++; return; }
        long ticks = Stopwatch.GetTimestamp() - stack[slot].Start;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - stack[slot].Allocated;
        ref var entry = ref stack[slot];
        ref var metric = ref pending[(int)entry.Stage];
        metric.Calls++; metric.Ticks += ticks; metric.Bytes += bytes;
        metric.SelfTicks += Math.Max(0, ticks - entry.ChildTicks);
        metric.SelfBytes += Math.Max(0, bytes - entry.ChildBytes);
        metric.MaxCallTicks = Math.Max(metric.MaxCallTicks, ticks);
        if (entry.Family >= 0)
        {
            ref var family = ref pendingFamilies[entry.Family * StageCount + (int)entry.Stage];
            family.Calls++; family.Ticks += ticks; family.Bytes += bytes;
            family.SelfTicks += Math.Max(0, ticks - entry.ChildTicks);
            family.SelfBytes += Math.Max(0, bytes - entry.ChildBytes);
            family.MaxCallTicks = Math.Max(family.MaxCallTicks, ticks);
        }
        depth--;
        if (depth > 0) { stack[depth - 1].ChildTicks += ticks; stack[depth - 1].ChildBytes += bytes; }
    }
    internal static void Work(LabCpuStage stage, int vertices, int indices)
    {
        var capture = current;
        if (capture == null || !capture.StagesEnabled || Environment.CurrentManagedThreadId != capture.thread) return;
        ref var metric = ref capture.pending[(int)stage];
        metric.Vertices += vertices; metric.Indices += indices;
    }
    internal static void Batch(LabBatchReason reason)
    {
        var capture = current;
        if (capture?.StagesEnabled == true && Environment.CurrentManagedThreadId == capture.thread) capture.reasons[(int)reason]++;
    }
    // Called at FrameEnqueued, outside source scopes; rows correspond to the lab frame CSV.
    internal void CompleteFrame(long now = 0, long allocated = -1)
    {
        if (Environment.CurrentManagedThreadId != thread || stopped) return;
        if (now == 0) now = Stopwatch.GetTimestamp();
        if (allocated < 0) allocated = GC.GetAllocatedBytesForCurrentThread();
        if (depth != 0) { InvalidScopes += depth; depth = 0; }
        if (FrameCount == validWindows.Length) return;
        bool valid = prePollVisits == 1 && bridgeVisits == 1 && lastEnqueue != 0 && prePoll >= lastEnqueue && bridgeStart >= prePoll
            && bridgeEnd >= bridgeStart && now >= bridgeEnd;
        validWindows[FrameCount] = valid;
        if (valid)
        {
            int offset = FrameCount * (int)LabFrameWindow.Count;
            frameWindows[offset] = new(prePoll - lastEnqueue, Math.Max(0, prePollBytes - lastEnqueueBytes));
            frameWindows[offset + 1] = new(bridgeStart - prePoll, Math.Max(0, bridgeStartBytes - prePollBytes));
            frameWindows[offset + 2] = new(bridgeEnd - bridgeStart, Math.Max(0, bridgeEndBytes - bridgeStartBytes), bridgeEndGen0 - bridgeGen0, bridgeEndGen1 - bridgeGen1, bridgeEndGen2 - bridgeGen2);
            frameWindows[offset + 3] = new(now - bridgeEnd, Math.Max(0, allocated - bridgeEndBytes));
        }
        lastEnqueue = now; lastEnqueueBytes = allocated;
        prePoll = bridgeStart = bridgeEnd = 0;
        prePollVisits = bridgeVisits = 0;
        if (!StagesEnabled) { FrameCount++; return; }
        Array.Copy(pending, 0, frames, FrameCount * StageCount, StageCount);
        Array.Copy(reasons, 0, frameReasons, FrameCount * ReasonCount, ReasonCount);
        for (int i = 0; i < familyTotals.Length; i++)
        {
            ref var target = ref familyTotals[i]; ref var source = ref pendingFamilies[i];
            target.Calls += source.Calls; target.Ticks += source.Ticks; target.SelfTicks += source.SelfTicks;
            target.Bytes += source.Bytes; target.SelfBytes += source.SelfBytes;
            target.MaxCallTicks = Math.Max(target.MaxCallTicks, source.MaxCallTicks);
        }
        Array.Clear(pendingFamilies);
        snapshotFrames[FrameCount] = pendingSnapshot; pendingSnapshot = default;
        Array.Clear(pending); Array.Clear(reasons); FrameCount++;
    }
    // Numeric successful snapshot coverage only. Absent captures remain distinct from
    // empty captures; no draw/material references survive the constructor.
    internal static void RecordSnapshot(bool bloom, int commands, int mask)
    {
        var capture = current;
        if (capture == null || !capture.StagesEnabled || Environment.CurrentManagedThreadId != capture.thread) return;
        var previous = capture.pendingSnapshot;
        capture.pendingSnapshot = bloom
            ? previous with { BloomCalls = previous.BloomCalls + 1, BloomCommands = previous.BloomCommands + commands, BloomMask = previous.BloomMask | mask }
            : previous with { HdrCalls = previous.HdrCalls + 1, HdrCommands = previous.HdrCommands + commands };
    }
    internal SnapshotWork GetSnapshot(int frame) => snapshotFrames[frame];
    internal Metric Get(int frame, LabCpuStage stage) => StagesEnabled ? frames[frame * StageCount + (int)stage] : default;
    internal long Reason(int frame, LabBatchReason reason) => StagesEnabled ? frameReasons[frame * ReasonCount + (int)reason] : 0;
    internal bool HasWindows(int frame) => validWindows[frame];
    internal Window GetWindow(int frame, LabFrameWindow window) => frameWindows[frame * (int)LabFrameWindow.Count + (int)window];
    internal void BeginMainFrame()
    {
        if (stopped || Environment.CurrentManagedThreadId != thread) return;
        prePollVisits++;
        prePollBytes = GC.GetAllocatedBytesForCurrentThread(); prePoll = Stopwatch.GetTimestamp();
        bridgeStart = bridgeEnd = 0;
    }
    // Separate timeline windows, not nesting scopes: these partition a complete frame interval.
    internal static BridgeScope BridgeWindow()
    {
        var capture = current;
        if (capture == null || Environment.CurrentManagedThreadId != capture.thread) return default;
        capture.bridgeVisits++;
        capture.bridgeGen0 = GC.CollectionCount(0); capture.bridgeGen1 = GC.CollectionCount(1); capture.bridgeGen2 = GC.CollectionCount(2);
        capture.bridgeStartBytes = GC.GetAllocatedBytesForCurrentThread(); capture.bridgeStart = Stopwatch.GetTimestamp();
        capture.bridgeEnd = 0;
        return new BridgeScope(capture);
    }
    internal readonly struct BridgeScope : IDisposable
    {
        private readonly LabCpuMetrics owner;
        internal BridgeScope(LabCpuMetrics owner) => this.owner = owner;
        public void Dispose()
        {
            if (owner == null || owner.stopped || Environment.CurrentManagedThreadId != owner.thread) return;
            owner.bridgeEnd = Stopwatch.GetTimestamp(); owner.bridgeEndBytes = GC.GetAllocatedBytesForCurrentThread();
            owner.bridgeEndGen0 = GC.CollectionCount(0); owner.bridgeEndGen1 = GC.CollectionCount(1); owner.bridgeEndGen2 = GC.CollectionCount(2);
        }
    }
}
