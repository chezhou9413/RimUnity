using System;
using System.Collections.Generic;

namespace RimUnity;

//一次播放统一准备所有目标，自动播放也在首帧合并预热，避免共享子系统重复推进。
public sealed partial class ParticleSystem
{
    private bool prewarmPending;

    //正在播放时不改变时间与位置；恢复暂停保留原有的发射状态。
    private void BeginPlayback()
    {
        if (playing && !paused) return;
        bool fresh = !playing && !paused;
        if (fresh) { ResetPlayback(); emitting = true; }
        playing = true; paused = false; started = true;
        previousPosition = transform.position; clock.Reset();
        if (fresh) prewarmPending = main.prewarm && main.loop && count == 0;
    }

    //恢复事件粒子的积分，但不启动子发射器独立的常规发射时钟。
    private void ResumeSubEmitter()
    {
        if (started && !paused) return;
        if (!started) ResetPlayback();
        started = true; paused = false;
        previousPosition = transform.position; clock.Reset();
    }

    //显式调用的根可以独立播放，其余事件目标只恢复事件驱动的模拟。
    private void PlaySystems(bool withChildren)
    {
        if (coordinatedStep) throw new InvalidOperationException("粒子正在推进，不能在同一步内重新播放系统。");
        List<ParticleSystem> systems = CollectSimulationSystems(withChildren, withChildren);
        HashSet<ParticleSystem> targets = SubEmitterTargets(systems);
        foreach (ParticleSystem system in systems)
        {
            if (system != this && (targets.Contains(system) || system.IsSubEmitter())) system.ResumeSubEmitter();
            else system.BeginPlayback();
        }
        PrewarmSystems(systems);
    }

    //从实际引用识别事件目标，不依赖它是否仍在父级 Transform 层级内。
    private static HashSet<ParticleSystem> SubEmitterTargets(List<ParticleSystem> systems)
    {
        var targets = new HashSet<ParticleSystem>();
        foreach (ParticleSystem system in systems)
        {
            foreach (SubEmitter entry in system.subEmitters.entries)
                if (entry.system != null) targets.Add(entry.system);
            foreach (SubEmissionEvent instance in system.subEmissionEvents)
                if (instance.child != null) targets.Add(instance.child);
        }
        return targets;
    }

    //不同根的一个周期在同一结束时刻对齐，共享子系统沿最长依赖区间只推进一次。
    private static void PrewarmSystems(List<ParticleSystem> candidates)
    {
        bool pending = false;
        foreach (ParticleSystem system in candidates)
            if (system.prewarmPending && !system.paused && system.isActiveAndEnabled) { pending = true; break; }
        if (!pending) return;
        var roots = new Dictionary<ParticleSystem, float>();
        float horizon = 0;
        foreach (ParticleSystem system in candidates)
        {
            if (!system.prewarmPending || system.paused || !system.isActiveAndEnabled) continue;
            if (!float.IsFinite(system.main.duration) || system.main.duration <= 0)
                throw new InvalidOperationException("预热 duration 必须为有限正数。");
            if (!float.IsFinite(system.main.simulationSpeed) || system.main.simulationSpeed < 0)
                throw new InvalidOperationException("预热 simulationSpeed 必须为有限非负数。");
            float span = system.main.duration / PrewarmSpeed(system, true);
            if (!float.IsFinite(span)) throw new InvalidOperationException("预热周期超出可表示的时间范围。");
            roots.Add(system, span); horizon = Math.Max(horizon, span);
        }
        if (roots.Count == 0) return;
        if (coordinatedStep) throw new InvalidOperationException("粒子正在推进，不能在同一步内预热。");
        var included = new HashSet<ParticleSystem>();
        foreach (ParticleSystem root in roots.Keys)
            foreach (ParticleSystem system in root.CollectSimulationSystems(false, true)) included.Add(system);
        List<ParticleSystem> systems = OrderSimulationSystems(included);
        var starts = new Dictionary<ParticleSystem, float>();
        var boundaries = new SortedSet<float> { 0, horizon };
        foreach (ParticleSystem system in systems)
        {
            if (!system.started) { system.ResetPlayback(); system.started = true; }
            starts[system] = roots.TryGetValue(system, out float span) ? horizon - span : float.PositiveInfinity;
        }
        foreach (ParticleSystem system in systems)
        {
            if (roots.ContainsKey(system))
            {
                system.elapsed = Math.Max(0, system.delay); system.prewarmPending = false;
                boundaries.Add(starts[system]);
            }
            if (system.subEmitters.enabled)
                foreach (SubEmitter entry in system.subEmitters.entries)
                    if (entry.system != null) starts[entry.system] = Math.Min(starts[entry.system], starts[system]);
            foreach (SubEmissionEvent instance in system.subEmissionEvents)
                starts[instance.child] = Math.Min(starts[instance.child], starts[system]);
        }
        float from = 0;
        foreach (float to in boundaries)
        {
            if (to <= from) continue;
            foreach (ParticleSystem system in systems)
            {
                float dt = system.paused || starts[system] > from ? 0 : (to - from) * PrewarmSpeed(system, roots.ContainsKey(system));
                system.PrepareSimulationTime(dt);
            }
            AdvanceSystems(systems, true, true);
            from = to;
        }
        foreach (ParticleSystem system in systems) system.clock.Reset();
    }

    //零速根仍可形成静态预热快照；事件子系统保持自己的零速设置。
    private static float PrewarmSpeed(ParticleSystem system, bool root) => root && system.main.simulationSpeed == 0 ? 1 : system.main.simulationSpeed;
}
