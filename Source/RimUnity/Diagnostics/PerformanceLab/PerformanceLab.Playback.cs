using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimUnity.Assets;
using RimUnity.Runtime;
using Verse;

namespace RimUnity.Diagnostics;

//有上限的连续实例化用于测量真实加载与销毁路径，不改写模板参数。
internal sealed partial class PerformanceLab
{
    internal int Concurrency = 20;
    internal float Rate = 5, Lifetime = 5, Radius = 8, Scale = 1, RotationX;
    internal int SampleSeconds = 60;
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
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    //每个实例保留组件列表，采样时不重新搜索整个对象树。
    private readonly record struct LiveEffect(GameObject Root, ParticleSystem[] Systems, double Expires);

    //选择或参数变更后重新开始，报告中的条件不会混杂。
    internal void StartPlayback()
    {
        Sampler.Stop("重新开始播放", export: true);
        StopPlayback();
        sequence.Clear(); sequence.AddRange(Effects.Where(e => e.Selected));
        if (sequence.Count == 0) { Status = "请先在特效列表勾选至少一个特效。"; return; }
        cursor = Spawned = ThrottledFrames = 0;
        random = new System.Random(9413);
        due = Now; Playing = true;
        Status = "轮播 " + sequence.Count + " 个特效；达到并发上限后等待旧实例结束。";
        Sampler.Start(SampleSeconds);
    }

    //暂停发射并清空已有实例，仍保留面板与测试地图。
    internal void StopPlayback()
    {
        Playing = false;
        foreach (var effect in live) if (effect.Root != null) RimUnity.Object.DestroyImmediate(effect.Root);
        live.Clear(); sequence.Clear();
    }

    //每帧最多新建 16 个实例，低帧率时不无限追赶积欠发射；限流次数写入报告。
    private void Update()
    {
        if (!IsCurrent) { Close(); return; }
        try
        {
            double now = Now;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var effect = live[i];
                if (effect.Root != null && now < effect.Expires) continue;
                if (effect.Root != null) RimUnity.Object.DestroyImmediate(effect.Root);
                live.RemoveAt(i);
            }
            if (!Playing) return;
            int created = 0;
            long started = Stopwatch.GetTimestamp();
            while (now >= due && live.Count < Concurrency && created < 16)
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
            if (now >= due)
            {
                if (live.Count < Concurrency) ThrottledFrames++;
                due = now + 1d / Rate;
            }
        }
        catch (Exception ex)
        {
            Sampler.Stop("播放异常：" + ex.Message, export: true);
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
            var template = PrefabLoader.Load(effect.Mod, effect.Path);
            using (NativeRuntime.DeferLifecycle())
            {
                root = RimUnity.Object.Instantiate(template);
                root.SetActive(false);
                double angle = random.NextDouble() * Math.PI * 2;
                float distance = (float)Math.Sqrt(random.NextDouble()) * Radius;
                root.transform.position = new Vector3(Map.Center.x + (float)Math.Cos(angle) * distance, 1,
                    Map.Center.z + (float)Math.Sin(angle) * distance);
                root.transform.rotation = Quaternion.Euler(RotationX, 0, 0) * root.transform.rotation;
                root.transform.localScale *= Scale;
                var systems = root.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var system in systems) { system.main.useUnscaledTime = true; system.main.playOnAwake = true; }
                root.SetActive(true);
                live.Add(new LiveEffect(root, systems, now + Lifetime));
            }
            Spawned++;
        }
        catch (Exception ex)
        {
            if (root != null) RimUnity.Object.DestroyImmediate(root);
            throw new InvalidOperationException(effect.Label + "：" + ex.Message, ex);
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

    //详细探针仍使用现有有界采集器，并由测试场负责停止自己启动的会话。
    internal void StartDetailedCapture()
    {
        var capture = ParticleSpikeCapture.BeginLabCapture();
        if (capture != null) detailedCapture = capture;
        Status = capture == null ? "已有详细采集或启动失败，请查看日志。" : "详细探针已开启 60 秒；其自身开销会影响常规采样。";
    }
}
