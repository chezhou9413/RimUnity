using System;
using System.Collections.Generic;

namespace RimUnity;

//一次触发独立持有发射时钟，多个父粒子不会修改同一个子系统的播放状态。
public sealed partial class ParticleSystem
{
    private sealed class SubEmissionEvent
    {
        internal ParticleSystem child;
        internal Particle parent;
        internal int properties;
        internal bool follow, begun;
        internal float elapsed, delay, carry, distanceCarry, firstOffset;
        internal Vector3 position;
        internal EventSpace space;
    }
    private readonly record struct ParentMotion(Particle Particle, Vector3 From, Vector3 To, float AliveTime);
    private readonly List<SubEmissionEvent> subEmissionEvents = new();
    private readonly Dictionary<ulong, ParentMotion> subParentMotions = new();

    //瞬时 Burst 立即触发，后续连续发射与延迟 Burst 留在所属父系统推进。
    private void StartSubEmission(Particle parent, SubEmitter item, float offset)
    {
        ParticleSystem child = item.system;
        if (child == null) throw new InvalidOperationException("子发射器引用为空。");
        if (!child.emission.enabled || !child.gameObject.activeInHierarchy) return;
        var instance = new SubEmissionEvent { child = child, parent = parent, properties = item.properties,
            follow = item.type == 0, firstOffset = offset, position = WorldPosition(parent),
            delay = Mathf.Max(0, child.main.startDelay.Evaluate(SystemPhase, Random(parent.randomSeed, 121))) };
        subEmissionEvents.Add(instance);
        instance.space = child.CreateEventSpace(instance.position);
        float childOffset = coordinatedStep && currentStepDelta > 0 ? offset / currentStepDelta * child.currentStepDelta : 0;
        AdvanceSubEmission(instance, 0, instance.position, instance.position, childOffset);
    }

    //记录本小步真实的父粒子轨迹，死亡后也能在正确的最后位置完成发射。
    private void CaptureParentMotion(Particle particle, Vector3 from, float aliveTime)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.SubMotion);
#endif

        if (subEmissionEvents.Count != 0)
        {
            if (subParentMotions.TryGetValue(particle.identity, out var previous))
            { from = previous.From; aliveTime += previous.AliveTime; }
            subParentMotions[particle.identity] = new ParentMotion(particle, from, WorldPosition(particle), aliveTime);
        }
    }

    //Birth 跟随父粒子存活区间，Death 独立执行一次子系统时长。
    private void AdvanceSubEmissions(float dt)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.SubAdvance);
#endif

        for (int i = subEmissionEvents.Count - 1; i >= 0; i--)
        {
            SubEmissionEvent instance = subEmissionEvents[i];
            if (instance.child == null) throw new InvalidOperationException("正在执行的子发射器已被销毁。");
            float start = dt > 0 ? Mathf.Clamp01(instance.firstOffset / dt) : 0, end = 1;
            Vector3 from = instance.position, to = from;
            bool parentDead = false;
            if (instance.follow)
            {
                if (subParentMotions.TryGetValue(instance.parent.identity, out ParentMotion motion))
                {
                    instance.parent = motion.Particle; from = motion.From; to = motion.To;
                    if (dt > 0) end = Mathf.Min(1, start + motion.AliveTime / dt);
                    parentDead = motion.Particle.remainingLifetime <= 0;
                }
                else { parentDead = true; end = start; }
            }
            if (instance.child.main.simulationSpeed < 0 || !float.IsFinite(instance.child.main.simulationSpeed)) throw new InvalidOperationException("子发射器 simulationSpeed 必须为有限非负数。");
            float childDt = coordinatedStep ? instance.child.currentStepDelta : dt * instance.child.main.simulationSpeed;
            AdvanceSubEmission(instance, childDt * (end - start), from, to, childDt * start);
            instance.firstOffset = 0; instance.position = to;
            instance.space.position = to; instance.space.rotation = instance.child.transform.rotation;
            instance.space.scale = instance.child.EventScale;
            if (parentDead || !instance.follow && instance.elapsed - instance.delay >= instance.child.main.duration) subEmissionEvents.RemoveAt(i);
        }
        subParentMotions.Clear();
    }

    //时间、距离和 Burst 分别计数，出生点在本次父粒子运动区间中插值。
    private void AdvanceSubEmission(SubEmissionEvent instance, float dt, Vector3 fromPosition, Vector3 toPosition, float birthOffset)
    {
        ParticleSystem child = instance.child;
        if (child.main.duration <= 0 || !float.IsFinite(child.main.duration)) throw new InvalidOperationException("子发射器 duration 必须为有限正数。");
        float duration = child.main.duration;
        float before = instance.elapsed - instance.delay;
        instance.elapsed += dt;
        float now = instance.elapsed - instance.delay;
        bool loop = instance.follow && child.main.loop;
        if (now < 0 || !child.emission.enabled) return;
        float from = Mathf.Max(0, before), to = loop ? now : Mathf.Min(now, duration);
        if (to < from) return;
        float phase = Mathf.Repeat(to, duration) / duration;
        float start = dt > 0 ? Mathf.Clamp01((from - before) / dt) : 0;
        float end = dt > 0 ? Mathf.Clamp01((to - before) / dt) : 0;
        float rate = instance.follow ? Mathf.Max(0, child.emission.rateOverTime.Evaluate(phase, Random(instance.parent.randomSeed, 122))) : 0;
        EmitSubContinuous(instance, ref instance.carry, rate * (to - from), from, to, Vector3.Lerp(fromPosition, toPosition, start), Vector3.Lerp(fromPosition, toPosition, end), birthOffset + dt * start, dt * (end - start));
        float distanceRate = instance.follow ? Mathf.Max(0, child.emission.rateOverDistance.Evaluate(phase, Random(instance.parent.randomSeed, 123))) : 0;
        EmitSubContinuous(instance, ref instance.distanceCarry, distanceRate * Vector3.Distance(fromPosition, toPosition) * (end - start), from, to, Vector3.Lerp(fromPosition, toPosition, start), Vector3.Lerp(fromPosition, toPosition, end), birthOffset + dt * start, dt * (end - start));
        int first = (int)(from / duration), last = loop ? (int)(to / duration) : 0;
        for (int cycle = first; cycle <= last; cycle++) foreach (Burst burst in child.emission.bursts)
            for (int repeat = 0; repeat < child.BurstCycles(burst); repeat++)
            {
                float at = cycle * duration + burst.time + repeat * burst.repeatInterval;
                if (at >= (cycle + 1) * duration || at > to || !(at > from || at == 0 && !instance.begun)) continue;
                if (Random01() >= burst.probability) continue;
                int amount = Math.Max(0, Mathf.RoundToInt(burst.count.Evaluate((at - cycle * duration) / duration, Random01())));
                Vector3 position = Vector3.Lerp(fromPosition, toPosition, dt > 0 ? Mathf.Clamp01((at - before) / dt) : 0);
                EmitSub(instance, position, (at - cycle * duration) / duration, amount, birthOffset + Mathf.Max(0, at - before));
            }
        instance.begun = true;
    }

    //每个事件独立保存不足一粒的余量，不能与另一个父粒子的余量相加。
    private void EmitSubContinuous(SubEmissionEvent instance, ref float carry, float amount, float from, float to, Vector3 start, Vector3 end, float birthOffset, float dt)
    {
        if (!float.IsFinite(amount)) throw new InvalidOperationException("子发射器速率必须产生有限粒子数量。");
        float old = carry, total = old + amount;
        int number = (int)Math.Min(int.MaxValue, Math.Floor(total));
        carry = total - (float)Math.Floor(total);
        for (int i = 0; i < number && instance.child.count < instance.child.main.maxParticles; i++)
        {
            float fraction = (i + 1 - old) / amount;
            EmitSub(instance, Vector3.Lerp(start, end, fraction), Mathf.Repeat(Mathf.Lerp(from, to, fraction), instance.child.main.duration) / instance.child.main.duration, 1, birthOffset + dt * fraction);
        }
    }

    //继承值乘在子系统自己的出生属性上，Duration 只改变本事件的曲线采样时间。
    private void EmitSub(SubEmissionEvent instance, Vector3 position, float phase, int amount, float birthOffset)
    {
        if (!instance.child.gameObject.activeInHierarchy) return;
        if (subDepth >= 32) throw new InvalidOperationException("子发射器递归深度超限，请检查循环引用。");
        Particle parent = instance.parent;
        instance.space.position = position;
        var args = new EmitParams { position = instance.child.FromWorld(position, instance.space), applyShapeToPosition = true,
            eventSpace = instance.space, emitterWorld = position, sampleTime = (instance.properties & 16) != 0 ? SystemPhase : phase };
        if (coordinatedStep) args.scheduledOffset = birthOffset;
        if ((instance.properties & 1) != 0) args.inheritedColor = ColorAt(parent);
        if ((instance.properties & 2) != 0) args.inheritedSize = SizeAt(parent);
        if ((instance.properties & 4) != 0) args.inheritedRotation = parent.rotation3D;
        if ((instance.properties & 8) != 0) args.inheritedLifetime = 1 - Age(parent);
        subDepth++;
        try { instance.child.Emit(args, amount); }
        finally { subDepth--; }
    }

    //非循环系统到达末尾时保留末端曲线值，循环系统才回到起点。
    private float SystemPhase => main.loop ? Mathf.Repeat(Mathf.Max(0, elapsed - delay), main.duration) / main.duration : Mathf.Clamp01((elapsed - delay) / main.duration);
}
