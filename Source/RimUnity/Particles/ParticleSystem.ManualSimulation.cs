using System;
using System.Collections.Generic;

namespace RimUnity;

//显式时间跳转和预热先确定完整系统集合，不在发射后递归重置子系统。
public sealed partial class ParticleSystem
{
    //保持已有粒子时只重置播放时钟；Clear 由调用方按 restart 单独决定。
    private void ResetPlayback()
    {
        elapsed = carry = distanceCarry = 0;
        //系统级随机曲线和逐粒子随机序列必须使用同一次播放的有效种子。
        if (useAutoRandomSeed)
        {
            randomSeed = (uint)Environment.TickCount ^ (uint)GetInstanceID();
            if (randomSeed == 0) randomSeed = 1;
        }
        rng = randomSeed;
        if (rng == 0) rng = 1;
        delay = main.startDelay.Evaluate(0, Random01()); completed = false;
        previousPosition = transform.position; clock.Reset();
    }

    //子发射器只接受事件发射，不额外启动自己的 rateOverTime 或 Burst。
    private void SimulateSystems(float seconds, bool withChildren, bool restart, bool fixedTimeStep)
    {
        if (coordinatedStep) throw new InvalidOperationException("粒子正在推进，不能在同一步内再次调用 Simulate。");
        List<ParticleSystem> systems = CollectSimulationSystems(withChildren, withChildren);
        HashSet<ParticleSystem> targets = SubEmitterTargets(systems);
        foreach (ParticleSystem system in systems)
        {
            bool initialize = restart || !system.started;
            if (restart) system.ClearParticles();
            if (initialize)
            {
                system.ResetPlayback();
                system.emitting = system == this || !targets.Contains(system) && !system.IsSubEmitter();
            }
            system.playing = true; system.paused = system.prewarmPending = false; system.started = true;
            system.PrepareSimulationTime(seconds * system.main.simulationSpeed);
        }
        try { AdvanceSystems(systems, fixedTimeStep, false); }
        finally
        {
            foreach (ParticleSystem system in systems)
            {
                system.playing = false; system.paused = true; system.clock.Reset();
            }
        }
    }

    //层级控制和事件依赖分别收集，去重后再按父发射器优先排序。
    private List<ParticleSystem> CollectSimulationSystems(bool withChildren, bool includeEvents)
    {
        var included = new HashSet<ParticleSystem> { this };
        if (withChildren)
            foreach (ParticleSystem system in GetComponentsInChildren<ParticleSystem>(true)) included.Add(system);
        if (includeEvents)
        {
            var pending = new Queue<ParticleSystem>(included);
            while (pending.Count != 0)
            {
                ParticleSystem system = pending.Dequeue();
                if (system.subEmitters.enabled)
                    foreach (SubEmitter entry in system.subEmitters.entries)
                        if (entry.system != null && included.Add(entry.system)) pending.Enqueue(entry.system);
                foreach (SubEmissionEvent instance in system.subEmissionEvents)
                    if (instance.child != null && included.Add(instance.child)) pending.Enqueue(instance.child);
            }
        }
        return OrderSimulationSystems(included);
    }

    //多根预热和单根显式模拟使用相同的拓扑顺序。
    private static List<ParticleSystem> OrderSimulationSystems(HashSet<ParticleSystem> included)
    {
        var ordered = new List<ParticleSystem>(included.Count);
        var states = new Dictionary<ParticleSystem, int>();
        foreach (ParticleSystem system in included) Visit(system, included, ordered, states);
        ordered.Reverse();
        return ordered;
    }
}
