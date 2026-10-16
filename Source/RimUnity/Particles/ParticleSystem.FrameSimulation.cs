using System;
using System.Collections.Generic;

namespace RimUnity;

//同一帧先模拟发射父级再模拟子级，子粒子只积分出生后的剩余时间。
public sealed partial class ParticleSystem
{
    private static int simulatedFrame = -1;
    private static bool coordinatedStep;
    private static readonly List<ParticleSystem> frameSystems = new(), orderedSystems = new();
    private static readonly Dictionary<ParticleSystem, int> visitState = new();
    private float frameDelta, currentStepDelta;
    private Vector3 frameFrom, frameTo;

    //原生生命周期的第一个 Update 推进全部桥接粒子，其余 Update 不重复推进。
    private static void SimulateFrame()
    {
        if (simulatedFrame == Time.frameCount) return;
        simulatedFrame = Time.frameCount;
        frameSystems.Clear(); frameSystems.AddRange(Active);
        orderedSystems.Clear(); visitState.Clear();
        try
        {
            foreach (ParticleSystem system in frameSystems) Visit(system, Active, orderedSystems, visitState);
            orderedSystems.Reverse();
            //Start 只登记预热，统一完成整组预热后再读取本帧时间和位置。
            foreach (ParticleSystem system in orderedSystems) system.Start();
            PrewarmSystems(orderedSystems);
            foreach (ParticleSystem system in orderedSystems)
            {
                float dt = system.clock.Step(system.main.useUnscaledTime);
                system.PrepareSimulationTime(system.paused ? 0 : dt * system.main.simulationSpeed);
            }
            AdvanceSystems(orderedSystems, true, true);
        }
        finally
        {
            frameSystems.Clear(); orderedSystems.Clear(); visitState.Clear();
        }
    }

    //帧更新、显式模拟和预热共用出生偏移与子系统时钟换算。
    private static void AdvanceSystems(List<ParticleSystem> systems, bool fixedTimeStep, bool activeOnly)
    {
        if (coordinatedStep) throw new InvalidOperationException("粒子正在推进，不能在同一步内再次调用 Simulate 或预热。");
        float maximum = 0;
        foreach (ParticleSystem system in systems) maximum = Math.Max(maximum, system.frameDelta);
        int steps = fixedTimeStep ? Math.Max(1, checked((int)Math.Ceiling(maximum * 60d))) : 1;
        coordinatedStep = true;
        try
        {
            ParticlePhysicsWorld.Refresh(activeOnly);
            foreach (var system in systems) system.BeginPhysicsEvents();
            for (int step = 0; step < steps; step++)
            {
                //所有子级的本步时间先确定，父级发射时可立即换算出生偏移。
                foreach (ParticleSystem system in systems) system.currentStepDelta = activeOnly && !system.isActiveAndEnabled ? 0 : system.frameDelta / steps;
                foreach (ParticleSystem system in systems)
                {
                    if (activeOnly && !system.isActiveAndEnabled) continue;
                    system.Step(system.currentStepDelta, Vector3.Lerp(system.frameFrom, system.frameTo, step / (float)steps),
                        Vector3.Lerp(system.frameFrom, system.frameTo, (step + 1f) / steps));
                }
            }
            foreach (var system in systems) system.DispatchPhysicsEvents();
        }
        finally
        {
            coordinatedStep = false;
            ParticlePhysicsWorld.colliders = Array.Empty<Collider>();
            foreach (ParticleSystem system in systems)
            {
                system.currentStepDelta = 0;
                system.collisionCandidates.Clear();
            }
        }
    }

    //保存本次推进的输入，避免父级发射过程中再读取不同的帧时间。
    private void PrepareSimulationTime(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0) throw new InvalidOperationException("粒子模拟时间必须为有限非负数。");
        frameDelta = dt; frameFrom = previousPosition; frameTo = transform.position;
    }

    //每帧读取当前引用图，脚本替换子发射器后无需额外缓存失效入口。
    private static void Visit(ParticleSystem system, HashSet<ParticleSystem> included, List<ParticleSystem> ordered, Dictionary<ParticleSystem, int> states)
    {
        if (!included.Contains(system)) return;
        if (states.TryGetValue(system, out int state))
        {
            if (state == 1) throw new InvalidOperationException("子发射器引用存在循环，无法确定模拟顺序。");
            return;
        }
        states[system] = 1;
        if (system.subEmitters.enabled)
            foreach (SubEmitter entry in system.subEmitters.entries)
                if (entry.system != null) Visit(entry.system, included, ordered, states);
        foreach (SubEmissionEvent instance in system.subEmissionEvents) Visit(instance.child, included, ordered, states);
        states[system] = 2; ordered.Add(system);
    }
}
