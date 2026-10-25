using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Verse;
using Waylith;

namespace RimUnity.Diagnostics;

//只在测试场显式采样期间订阅帧事件，固定容量避免报告本身无界增长。
internal sealed partial class LabSampler : IDisposable
{
    private const int Capacity = 36000;
    private readonly PerformanceLab lab;
    private EngineApplication application;
    private Process process;
    private LabCpuMetrics cpu;
    private long mainAllocated;
    private Sample[] samples;
    private int count, previous0, previous1, previous2, submitted;
    private long origin, previous, allocated, memoryTime, workingSet, privateBytes, managedBytes;
    private long startWorkingSet, startPrivateBytes;
    private ParticleDrawBatch.PoolUsage startPool, endPool;
    private readonly HashSet<GameObject> populationRoots = new();
    private LabPopulation.Boundary startPopulation, endPopulation;
    private bool endMemoryCaptured;
    private string endMemoryError;
    private int duration;
    private JsonElement configuration;
    private string startedUtc, stopReason, lastReport;
    private int spawnStart, throttledStart;
    private readonly Dictionary<string, SpawnCost> spawnCosts = new();
    internal bool Sampling { get; private set; }
    internal string Display { get; private set; } = "尚未采样。";
    internal string LastReport => lastReport;
    internal int Count => count;
    internal double Seconds => count == 0 ? 0 : samples[count - 1].ElapsedMs / 1000;

    //帧时间与托管分配属于整个进程，粒子数量只统计测试实例。
    private readonly record struct Sample(int Frame, double ElapsedMs, double IntervalMs, long AllocatedBytes,
        int Gen0, int Gen1, int Gen2, int Instances, int Systems, int Particles, int SubmittedItems,
        long ManagedBytes, long WorkingSetBytes, long PrivateBytes, int Spawned, int ThrottledFrames, bool DetailedProbes,
        int ParticleDrawsBeforeBatch, int ParticleDrawsAfterBatch,
        long MainThreadAllocatedBytes, double ObserverCpuMs, long ObserverAllocatedBytes, int ParticleUploadPages,
        ParticleDrawBatch.PoolUsage ParticlePool, LabPopulation.State ParticlePopulation);

    //实例化阶段单独按资源汇总，预热和逐帧模拟耗时由详细探针报告定位。
    internal sealed class SpawnCost
    {
        public int Count { get; set; }
        public double FirstMs { get; set; }
        public double TotalMs { get; set; }
        public double MaxMs { get; set; }
        public long AllocatedBytes { get; set; }
    }

    //采样器的数组、进程句柄与元数据准备在计时起点之前完成。
    internal LabSampler(PerformanceLab lab) => this.lab = lab;

    internal void Start(int seconds)
    {
        if (!lab.IsCurrent) return;
        bool emptyBaseline = !lab.Playing && lab.LiveCount == 0;
        if (!lab.StableReady && !emptyBaseline) { lab.Status = "固定实例仍在建立或预热，完成后会自动开始采样。"; return; }
        Stop("重新开始采样", export: true);
        samples = new Sample[Capacity]; count = 0; submitted = 0; lastReport = null;
        cpu = lab.MeasureCpuStages ? new LabCpuMetrics(Capacity) : null;
        lab.RegisterCpuFamilies(cpu);
        spawnCosts.Clear();
        duration = seconds; startedUtc = DateTime.UtcNow.ToString("O"); stopReason = "";
        var bloom = RimUnityMod.Bloom.Snapshot();
        configuration = JsonSerializer.SerializeToElement(new
        {
            MapSize = "96x96", lab.Concurrency, lab.Rate, lab.Lifetime, lab.Radius, lab.Scale, lab.RotationX,
            PlaybackMode = lab.FixedInstances ? "FixedInstances" : "CreateDestroy", lab.WarmupSeconds,
            ParticleSeedsFixed = lab.FixedInstances,
            RequestedSeconds = seconds, MaxNewInstancesPerFrame = 16, SpawnBudgetMs = 8, RandomSeed = 9413,
            PlaybackEnabled = lab.Playing, EmptyBaseline = emptyBaseline,
            ParticleBatchVertexLimit = ParticleDrawBatch.VertexLimit, ParticleBatchIndexLimit = ParticleDrawBatch.IndexLimit,
            Scenario = lab.Scenario, Effects = lab.ActiveEffects, CameraRootSize = Find.CameraDriver?.RootSize,
            StressFamilies = lab.Scenario == LabStressScenario.Version ? LabStressScenario.Names : Array.Empty<string>(),
            FixedInstanceDistribution = lab.FixedDistribution,
            StressWorldLayer = lab.Scenario == LabStressScenario.Version ? LabStressScenario.WorldLayer : -1,
            StressMaxParticlesPerSystem = lab.Scenario == LabStressScenario.Version ? LabStressScenario.ParticleLimit : 0,
            AssetWarnings = lab.Effects.FindAll(e => lab.Scenario == "selected-prefabs" && e.Selected && e.Warnings.Length > 0)
                .ConvertAll(e => new { Effect = e.Mod.PackageIdPlayerFacing + "/" + e.Path, e.Warnings }),
            RimUnityVersion = typeof(PerformanceLab).Assembly.ManifestModule.ModuleVersionId.ToString(),
            GameVersion = typeof(Game).Assembly.ManifestModule.ModuleVersionId.ToString(),
            ProcessorCount = Environment.ProcessorCount, Resolution = UnityEngine.Screen.width + "x" + UnityEngine.Screen.height,
            BloomEnabled = bloom.Enabled, BloomLayerMask = bloom.LayerMask, BloomThreshold = bloom.Threshold, BloomIntensity = bloom.Intensity,
            BloomSoftKnee = bloom.SoftKnee, BloomScatter = bloom.Scatter,
            BloomSource = lab.Scenario == LabStressScenario.Version ? "TestMapOnlyOverride" : "PlayerAndContentSettings",
            CpuStagesEnabled = lab.MeasureCpuStages, CpuStageSchema = 4, ParticleAnalysisBuild,
            ParticleUploadMode = "OrderedSubmeshPages", ParticlePageSegmentLimit = ParticleDrawBatch.SegmentLimit,
            ProcessMemorySampling = "BeforeAndAfterOnly; frame process memory columns retain the before-sample snapshot",
            RuntimeVersion = RuntimeInfo.Version, RuntimeProvider = RuntimeInfo.Provider,
            RuntimeIsStandalone = RuntimeInfo.IsStandalone, RuntimePayloadHash = RuntimeInfo.PayloadHash
        });
        spawnStart = lab.Spawned; throttledStart = lab.ThrottledFrames;
        process = Process.GetCurrentProcess(); ReadMemory();
        startWorkingSet = workingSet; startPrivateBytes = privateBytes;
        startPool = endPool = ParticleDrawBatch.ReadPoolUsage();
        startPopulation = endPopulation = LabPopulation.Capture(lab, populationRoots);
        endMemoryCaptured = false; endMemoryError = null;
#if RIMUNITY_PARTICLE_ANALYSIS
        StartParticleDetail();
#endif
        cpu?.Start();
        allocated = GC.GetTotalAllocatedBytes(false);
        mainAllocated = GC.GetAllocatedBytesForCurrentThread();
        previous0 = GC.CollectionCount(0); previous1 = GC.CollectionCount(1); previous2 = GC.CollectionCount(2);
        origin = previous = memoryTime = Stopwatch.GetTimestamp();
        application = EngineApplication.Instance;
        Sampling = true; Display = "正在采样……";
        ParticleDrawBatch.Sampling = true;
        application.FrameEnqueued += Frame;
        application.PrePollEvents += PrePoll;
    }

    //提交项是地图快照数量，不能当作驱动实际 DrawCall 或 GPU 耗时。
    internal void SetSubmittedItems(int value) => submitted = value;

    private void PrePoll(EngineApplication app)
    {
        cpu?.BeginMainFrame();
#if RIMUNITY_PARTICLE_ANALYSIS
        BeginParticleDetailFrame();
#endif
    }

    //只在采样开启时累计，字典规模最多等于本次选择的资源数。
    internal void RecordSpawn(string path, double milliseconds, long bytes)
    {
        if (!Sampling) return;
        if (!spawnCosts.TryGetValue(path, out var cost)) spawnCosts.Add(path, cost = new SpawnCost { FirstMs = milliseconds });
        cost.Count++; cost.TotalMs += milliseconds; cost.MaxMs = Math.Max(cost.MaxMs, milliseconds); cost.AllocatedBytes += bytes;
    }

    //进程查询只在采样边界进行；每秒只读取 GC 统计和更新界面摘要。
    private void Frame(EngineApplication app)
    {
        if (!Sampling) return;
        if (!lab.IsCurrent) { Stop("离开测试地图", export: true); return; }
        if (LongEventHandler.AnyEventNowOrWaiting) { Stop("进入加载流程", export: true); return; }
        try
        {
            long now = Stopwatch.GetTimestamp();
            long mainTotal = GC.GetAllocatedBytesForCurrentThread();
            long total = GC.GetTotalAllocatedBytes(false);
            cpu?.CompleteFrame(now, mainTotal);
#if RIMUNITY_PARTICLE_ANALYSIS
            CompleteParticleDetailFrame();
#endif
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            bool refresh = now - memoryTime >= Stopwatch.Frequency;
            if (refresh) { managedBytes = GC.GetTotalMemory(false); memoryTime = now; }
            var particles = lab.Counts();
            samples[count] = new Sample(count + 1, Ms(now - origin), Ms(now - previous), Math.Max(0, total - allocated),
                g0 - previous0, g1 - previous1, g2 - previous2, lab.LiveCount, particles.Systems, particles.Particles,
                submitted, managedBytes, workingSet, privateBytes, lab.Spawned - spawnStart,
                lab.ThrottledFrames - throttledStart, ParticleSpikeCapture.LabCaptureActive,
                ParticleDrawBatch.Before, ParticleDrawBatch.After,
                Math.Max(0, mainTotal - mainAllocated), 0, 0, ParticleDrawBatch.UploadPages, ParticleDrawBatch.ReadPoolUsage(), LabPopulation.Read(lab, populationRoots));
            count++;
            previous = now; allocated = total; mainAllocated = mainTotal; previous0 = g0; previous1 = g1; previous2 = g2;
            if (refresh)
                Display = $"采样 {Seconds:F1}/{duration} 秒 · {count} 帧\n" +
                    $"当前 {1000 / Math.Max(.001, samples[count - 1].IntervalMs):F1} FPS · {samples[count - 1].IntervalMs:F2} ms\n" +
                    $"实例 {lab.LiveCount} · 系统 {particles.Systems} · 粒子 {particles.Particles} · 提交项 {submitted}\n" +
                    $"粒子绘制提交：合批前 {ParticleDrawBatch.Before} → 合批后 {ParticleDrawBatch.After}\n" +
                    $"共享粒子网格上传页：{ParticleDrawBatch.UploadPages}\n" +
                    $"托管堆 {managedBytes / 1048576d:F1} MiB · 采样前工作集 {startWorkingSet / 1048576d:F1} MiB";
            samples[count - 1] = samples[count - 1] with
            {
                ObserverCpuMs = Ms(Stopwatch.GetTimestamp() - now),
                ObserverAllocatedBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - mainTotal)
            };
            if (Seconds >= duration) Stop("采样时间结束", export: true);
            else if (count == Capacity) Stop("达到 36000 帧容量上限", export: true);
        }
        catch (Exception ex)
        {
            Stop("采样异常：" + ex.Message, export: false);
            Log.Error("[RimUnity] 测试场采样失败：" + ex);
        }
    }

    //先取消事件订阅，再计算分位数与写文件，导出开销不进入当前报告。
    internal void Stop(string reason, bool export)
    {
        if (!Sampling) return;
        Sampling = false;
#if RIMUNITY_PARTICLE_ANALYSIS
        particleDetail?.Stop();
#endif
        cpu?.Stop();
        ParticleDrawBatch.Sampling = false;
        application.FrameEnqueued -= Frame;
        application.PrePollEvents -= PrePoll; application = null;
        endPool = ParticleDrawBatch.ReadPoolUsage();
        endPopulation = LabPopulation.Capture(lab, populationRoots);
        // Potentially slow OS queries run only after timing and event collection stop.
        try { ReadMemory(); endMemoryCaptured = true; }
        catch (Exception ex) { endMemoryError = ex.GetType().Name + ": " + ex.Message; }
        finally { process.Dispose(); process = null; }
        stopReason = reason;
        Display = Summary();
        if (export) Export();
    }

    //读取进程级内存，不触发 GC，也不声称这些值等于 GPU 显存。
    private void ReadMemory()
    {
        process.Refresh(); workingSet = process.WorkingSet64; privateBytes = process.PrivateMemorySize64;
        managedBytes = GC.GetTotalMemory(false);
    }

    private static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    //关闭窗口不会留下任何采样订阅。
    public void Dispose() => Stop("释放采样器", export: false);
}
