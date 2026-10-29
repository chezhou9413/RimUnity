using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimUnity.Assets;
using RimUnity.Runtime;
using Verse;

namespace RimUnity.Diagnostics;

//调试负载分为固定实例与持续创建/销毁两种模式；参数覆盖仅作用于测试克隆。
internal sealed partial class PerformanceLab
{
    internal int Concurrency = 20;
    internal float Rate = 5, Lifetime = 5, Radius = 8, Scale = 1, RotationX;
    internal int SampleSeconds = 60;
    internal bool FixedInstances = true;
    internal float WarmupSeconds = 10;
    private bool sampleAfterWarmup;
    private double warmupUntil = double.PositiveInfinity;
    internal bool StableReady => !FixedInstances || Playing && live.Count == Concurrency && Now >= warmupUntil;
    internal bool Playing { get; private set; }
    internal int Spawned { get; private set; }
    internal int ThrottledFrames { get; private set; }
    internal int LiveCount => live.Count;
    private readonly List<LiveEffect> live = new();
    private readonly List<LabEffect> sequence = new();
    private System.Random random = new(9413);
    private int cursor;
    private double due;
    private SpikeCaptureData detailedCapture;
    private LabStressScenario stress;
    internal bool StressBloom = true;
    internal bool MixedStressActive => stress != null;
    internal string Scenario => stress == null ? "selected-prefabs" : LabStressScenario.Version;
    internal string[] ActiveEffects => sequence.Select(e => e.Key).ToArray();
    internal Dictionary<string, int> FixedDistribution
    {
        get
        {
            var result = new Dictionary<string, int>();
            if (!Playing || !FixedInstances || sequence.Count == 0) return result;
            for (int i = 0; i < sequence.Count; i++)
            {
                int count = Concurrency / sequence.Count + (i < Concurrency % sequence.Count ? 1 : 0);
                result.TryGetValue(sequence[i].Key, out int prior); result[sequence[i].Key] = prior + count;
            }
            return result;
        }
    }
    internal void StartMixedStress()
    {
        // Bypasses authored emission limits only inside the developer test map.
        if (!IsCurrent || !DebugSettings.ShowDevGizmos) return;
        StressParticleTarget = 64; StressOnlyFamily = -1;
        Concurrency = LabStressScenario.Families * LabStressScenario.Copies;
        FixedInstances = true; WarmupSeconds = 12; SampleSeconds = 60;
        Radius = 14; Scale = 1; RotationX = 0; Rate = 16; Lifetime = 4;
        CameraJumper.TryJump(Map.Center, Map, CameraJumper.MovementMode.Cut);
        Find.CameraDriver.SetRootSize(24f);
        StartPlayback(mixedStress: true);
    }
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    //每个实例保留组件列表，采样时不重新搜索整个对象树。
    private readonly record struct LiveEffect(GameObject Root, ParticleSystem[] Systems, double Expires, int Family);

    internal void RegisterCpuFamilies(LabCpuMetrics capture)
    {
        if (capture == null) return;
        foreach (var effect in live)
            if (effect.Family >= 0)
                foreach (var system in effect.Systems)
                    if (system != null) capture.RegisterSystem(system.GetInstanceID(), effect.Family);
    }

    //选择或参数变更后重新开始，报告中的条件不会混杂。
    internal void StartPlayback(bool mixedStress = false)
    {
        if (mixedStress && (!IsCurrent || !DebugSettings.ShowDevGizmos)) return;
        Sampler.Stop("重新开始播放", export: true);
        StopPlayback();
        sequence.Clear();
        if (mixedStress)
        {
            try
            {
                stress = new LabStressScenario();
                for (int i = 0; i < LabStressScenario.Families; i++)
                    if (StressOnlyFamily < 0 || StressOnlyFamily == i) sequence.Add(new LabEffect { StressKind = i, Systems = i == 11 ? 2 : 1 });
            }
            catch (Exception ex) { stress?.Dispose(); stress = null; sequence.Clear(); Status = "混合场景创建失败：" + ex.Message; Log.Error("[RimUnity] " + ex); return; }
        }
        else sequence.AddRange(Effects.Where(e => e.Selected));
        if (sequence.Count == 0) { Status = "请先在特效列表勾选至少一个特效。"; return; }
        if (FixedInstances && sequence.Count > Concurrency) { StopPlayback(); Status = "固定实例总数少于选中特效数，请增加实例数或减少勾选。"; return; }
        cursor = Spawned = ThrottledFrames = 0;
        random = new System.Random(9413);
        due = Now; Playing = true;
        warmupUntil = double.PositiveInfinity;
        sampleAfterWarmup = FixedInstances;
        Status = FixedInstances ? "正在建立固定实例，填满后预热，再开始采样。" : "轮播 " + sequence.Count + " 个特效；达到并发上限后等待旧实例结束。";
        if (!FixedInstances) Sampler.Start(SampleSeconds);
    }

    //暂停发射并清空已有实例，仍保留面板与测试地图。
    internal void StopPlayback()
    {
        Playing = false;
        sampleAfterWarmup = false; warmupUntil = double.PositiveInfinity;
        foreach (var effect in live) if (effect.Root != null) RimUnity.Object.DestroyImmediate(effect.Root);
        live.Clear(); sequence.Clear();
        stress?.Dispose(); stress = null;
    }

    //每帧最多新建 16 个实例，低帧率时不无限追赶积欠发射；限流次数写入报告。
    private void Update()
    {
        if (!IsCurrent) { Close(); return; }
        try
        {
            if (stress != null && !DebugSettings.ShowDevGizmos)
            {
                Sampler.Stop("关闭开发权限", export: true); StopPlayback(); Status = "混合压力场景已清空：开发权限已关闭。"; return;
            }
            UpdateMeasurementSequence();
            double now = Now;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var effect = live[i];
                if (Playing && FixedInstances && (effect.Root == null || !effect.Root.activeInHierarchy))
                    throw new InvalidOperationException("固定场景中的特效自行禁用或销毁，已停止测试。请选择可持续播放的预制体。");
                if (effect.Root != null && now < effect.Expires) continue;
                if (effect.Root != null) RimUnity.Object.DestroyImmediate(effect.Root);
                live.RemoveAt(i);
            }
            if (!Playing) return;
            int created = 0;
            long started = Stopwatch.GetTimestamp();
            while ((FixedInstances || now >= due) && live.Count < Concurrency && created < 16)
            {
                var effect = sequence[cursor]; cursor = (cursor + 1) % sequence.Count;
                bool measure = Sampler.Sampling;
                long beforeSpawn = measure ? Stopwatch.GetTimestamp() : 0;
                long beforeBytes = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
                Spawn(effect, now);
                if (measure) Sampler.RecordSpawn(effect.Key,
                    Stopwatch.GetElapsedTime(beforeSpawn).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
                created++; due += 1d / Rate;
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 8) break;
            }
            if (FixedInstances)
            {
                if (live.Count < Concurrency) { ThrottledFrames++; return; }
                if (double.IsPositiveInfinity(warmupUntil)) warmupUntil = Now + WarmupSeconds;
                if (!sampleAfterWarmup) return;
                double remaining = warmupUntil - Now;
                if (remaining > 0) { Status = $"固定实例 {live.Count}/{Concurrency} · 预热剩余 {remaining:F1} 秒"; return; }
                sampleAfterWarmup = false;
                Status = "固定实例已就绪，正在采样；请保持相机位置、缩放和 Bloom 设置不变。";
                Sampler.Start(SampleSeconds);
            }
            else if (now >= due)
            {
                if (live.Count < Concurrency) ThrottledFrames++;
                due = now + 1d / Rate;
            }
        }
        catch (Exception ex)
        {
            Sampler.Stop("播放异常：" + ex.Message, export: true);
            EndMeasurementSequence("播放异常：" + ex.Message);
            StopPlayback();
            Status = "播放已停止：" + ex.Message;
            Log.Error("[RimUnity] 测试场播放失败：" + ex);
        }
    }

    //先配置真实时间与位置，再激活；只启动独立发射根，保留子发射器事件关系。
    private void Spawn(LabEffect effect, double now)
    {
        GameObject root = null;
        try
        {
            var template = effect.StressKind < 0 ? PrefabLoader.Load(effect.Mod, effect.Path) : null;
            using (NativeRuntime.DeferLifecycle())
            {
                root = effect.StressKind >= 0 ? stress.Create(effect.StressKind) : RimUnity.Object.Instantiate(template);
                root.SetActive(false);
                int instance = Spawned;
                double angle = FixedInstances ? instance * 2.399963229728653 : random.NextDouble() * Math.PI * 2;
                float distance = FixedInstances ? (Concurrency <= 1 ? 0 : Radius * (float)Math.Sqrt(instance / (double)(Concurrency - 1)))
                    : (float)Math.Sqrt(random.NextDouble()) * Radius;
                root.transform.position = new Vector3(Map.Center.x + (float)Math.Cos(angle) * distance, 1,
                    Map.Center.z + (float)Math.Sin(angle) * distance);
                root.transform.rotation = Quaternion.Euler(RotationX, 0, 0) * root.transform.rotation;
                root.transform.localScale *= Scale;
                var systems = root.GetComponentsInChildren<ParticleSystem>(true);
                if (FixedInstances) ConfigureFixedSystems(systems, instance);
                if (MeasurementRunning) ConfigureMeasurementLoad(systems);
                foreach (var system in systems) { system.main.useUnscaledTime = true; system.main.playOnAwake = true; }
                root.SetActive(true);
                live.Add(new LiveEffect(root, systems, FixedInstances ? double.PositiveInfinity : now + Lifetime, effect.StressKind));
            }
            Spawned++;
        }
        catch (Exception ex)
        {
            if (root != null) RimUnity.Object.DestroyImmediate(root);
            throw new InvalidOperationException(effect.Label + "：" + ex.Message, ex);
        }
    }

    //Only diagnostic clones are changed. Event-driven subemitters keep their emission contract;
    //ordinary emission roots loop instead of continuously rebuilding complete prefabs.
    private static void ConfigureFixedSystems(ParticleSystem[] systems, int instance)
    {
        var targets = new HashSet<ParticleSystem>();
        foreach (var system in systems)
            foreach (var entry in system.subEmitters.entries)
                if (entry.system != null) targets.Add(entry.system);
        for (int i = 0; i < systems.Length; i++)
        {
            var system = systems[i];
            system.useAutoRandomSeed = false;
            uint seed = unchecked(9413u + (uint)instance * 0x9e3779b9u + (uint)i * 0x85ebca6bu);
            system.randomSeed = seed == 0 ? 1u : seed;
            if (!targets.Contains(system)) system.main.loop = true;
        }
    }

    //统计测试场拥有的系统，不把正常地图的粒子混入数量指标。
    internal (int Systems, int Particles) Counts()
    {
        int systems = 0, particles = 0;
        foreach (var effect in live)
            foreach (var system in effect.Systems)
                if (system != null && system.isActiveAndEnabled) { systems++; particles += system.particleCount; }
        return (systems, particles);
    }

    // Observer-only point counts; no curve/geometry evaluation and no retained references.
    internal int TrailPointCount()
    {
        int count = 0;
        foreach (var effect in live) foreach (var system in effect.Systems)
            if (system != null && system.isActiveAndEnabled)
            {
                count += system.DiagnosticTrailPointCount();
            }
        return count;
    }

    // Include dynamically attached descendants by hierarchy, not only the initial component list.
    internal void CollectPopulationRoots(HashSet<GameObject> target)
    {
        target.Clear();
        foreach (var effect in live) if (effect.Root != null) target.Add(effect.Root);
    }

    //详细探针仍使用现有有界采集器，并由测试场负责停止自己启动的会话。
    internal void StartDetailedCapture()
    {
        var capture = ParticleSpikeCapture.BeginLabCapture();
        if (capture != null) detailedCapture = capture;
        Status = capture == null ? "已有详细采集或启动失败，请查看日志。" : "详细探针已开启 60 秒；其自身开销会影响常规采样。";
    }
}
