using System;

namespace RimUnity;

//发射和形状采样与每粒子更新分开，避免模块配置混入动态状态。
public sealed partial class ParticleSystem
{
    //位置分段与时间小步保持一致，距离发射不会全部挤在本帧终点。
    private void Step(float dt, Vector3 emitterFrom, Vector3 emitterTo)
    {
        stepEmitterVelocity = dt > 0 ? (emitterTo - emitterFrom) * (main.simulationSpeed / dt) : Vector3.zero;
        if (dt <= 0)
        {
            foreach (Particle particle in particles.AsSpan(0, count))
                CaptureParentMotion(particle, WorldPosition(particle), 0);
            AdvanceSubEmissions(0);
            previousPosition = emitterTo;
            return;
        }
        if (!playing && !emitting && count == 0 && trailStates.Count == 0 && subEmissionEvents.Count == 0) { previousPosition = emitterTo; return; }
        EnsureCapacity();
        if (main.duration <= 0 || !float.IsFinite(main.duration)) throw new InvalidOperationException("粒子 duration 必须为有限正数。");
        float before = elapsed - delay;
        elapsed += dt;
        float now = elapsed - delay;
        if (emitting && emission.enabled && now >= 0)
        {
            float from = Math.Max(0, before), to = main.loop ? now : Math.Min(now, main.duration);
            if (to >= from)
            {
                float normalized = Mathf.Repeat(to, main.duration) / main.duration;
                float start = Mathf.Clamp01((from - before) / dt), end = Mathf.Clamp01((to - before) / dt);
                float rate = Math.Max(0, emission.rateOverTime.Evaluate(normalized, Random(randomSeed, 2)));
                EmitContinuous(ref carry, rate * (to - from), from, to, start, end, emitterFrom, emitterTo, dt);
                float distanceRate = Math.Max(0, emission.rateOverDistance.Evaluate(normalized, Random(randomSeed, 3)));
                EmitContinuous(ref distanceCarry, distanceRate * Vector3.Distance(emitterFrom, emitterTo) * (end - start), from, to, start, end, emitterFrom, emitterTo, dt);
                int first = (int)(from / main.duration), last = main.loop ? (int)(to / main.duration) : 0;
                for (int loop = first; loop <= last; loop++) foreach (Burst burst in emission.bursts)
                    for (int cycle = 0; cycle < BurstCycles(burst); cycle++)
                    {
                        float at = loop * main.duration + burst.time + cycle * burst.repeatInterval;
                        if (at >= loop * main.duration + main.duration || at > to || !(at > from || at == 0 && before <= 0 && now > 0)) continue;
                        if (Random01() < burst.probability)
                        {
                            float phase = (at - loop * main.duration) / main.duration;
                            float offset = Mathf.Clamp01((at - before) / dt);
                            int amount = Math.Max(0, Mathf.RoundToInt(burst.count.Evaluate(phase, Random01())));
                            for (int n = 0; n < amount && CanEmitParticle; n++)
                                Spawn(default, Vector3.Lerp(emitterFrom, emitterTo, offset), phase, offset * dt, at, n, amount);
                        }
                    }
            }
        }
        //duration 与 emission 开关无关，禁用发射模块也必须结束非循环系统。
        if (emitting && !main.loop && now >= main.duration) emitting = false;
        previousPosition = emitterTo;
        for (int i = count - 1; i >= 0; i--)
        {
            ref Particle p = ref particles[i];
            float birthOffset = p.birthOffset;
            Vector3 fromPosition = WorldPosition(p);
            float particleDt = Mathf.Max(0, dt - birthOffset); p.birthOffset = 0;
            bool ring = main.ringBufferMode != ParticleSystemRingBufferMode.Disabled && p.remainingLifetime >= 0;
            float aliveDt = Mathf.Min(particleDt, Mathf.Max(0, p.remainingLifetime));
            if (ring && main.ringBufferMode == ParticleSystemRingBufferMode.LoopUntilReplaced) aliveDt = particleDt;
            p.elapsedAge += aliveDt;
            p.remainingLifetime -= particleDt;
            if (ring) UpdateRingAge(ref p);
            Integrate(ref p, aliveDt);
            ApplyParticlePhysics(ref p, fromPosition, aliveDt, birthOffset);
            RecordTrail(p);
            CaptureParentMotion(p, fromPosition, aliveDt);
            if (p.physicsKilled || p.remainingLifetime < 0 || p.remainingLifetime == 0 && !ring)
            {
                SubEmit(p, 2, birthOffset + aliveDt); ReleaseParticle(p.identity); particles[i] = particles[--count]; continue;
            }
        }
        AdvanceSubEmissions(dt);
        AgeTrails(dt);
        if (!emitting && count == 0 && trailStates.Count == 0 && subEmissionEvents.Count == 0 && !completed)
        {
            completed = true; playing = false;
            if (main.stopAction == ParticleSystemStopAction.Disable) gameObject.SetActive(false);
            else if (main.stopAction == ParticleSystemStopAction.Destroy) Destroy(gameObject);
            else if (main.stopAction == ParticleSystemStopAction.Callback) ParticleMessages.Send(gameObject, "OnParticleSystemStopped");
        }
    }

    //零循环次数表示在系统时长内重复；非法间隔不能产生无界循环。
    private int BurstCycles(Burst burst)
    {
        if (burst.cycleCount < 0 || burst.probability < 0 || burst.probability > 1 || !float.IsFinite(burst.time) || !float.IsFinite(burst.repeatInterval))
            throw new InvalidOperationException("Burst 的次数、概率或时间不合法。");
        if (burst.cycleCount == 1) return 1;
        if (burst.repeatInterval <= 0) throw new InvalidOperationException("重复 Burst 的间隔必须大于零。");
        int available = Math.Max(0, Mathf.CeilToInt((main.duration - burst.time) / burst.repeatInterval));
        return burst.cycleCount == 0 ? available : Math.Min(burst.cycleCount, available);
    }

    //分别累计时间与距离余量，在跨过每个整数阈值的位置出生。
    private void EmitContinuous(ref float remainder, float amount, float from, float to, float start, float end, Vector3 emitterFrom, Vector3 emitterTo, float dt)
    {
        if (!float.IsFinite(amount)) throw new InvalidOperationException("发射速率必须生成有限粒子数量。");
        float old = remainder, total = old + amount;
        int number = (int)Math.Min(int.MaxValue, Math.Floor(total));
        remainder = total - (float)Math.Floor(total);
        for (int i = 0; i < number && CanEmitParticle; i++)
        {
            float fraction = (i + 1 - old) / amount;
            float at = Mathf.Lerp(start, end, fraction);
            float phase = Mathf.Repeat(Mathf.Lerp(from, to, fraction), main.duration) / main.duration;
            Spawn(default, Vector3.Lerp(emitterFrom, emitterTo, at), phase, at * dt, Mathf.Lerp(from, to, fraction), i, number);
        }
    }

    //出生属性的不同随机通道互不影响。
    private void Spawn(EmitParams args, Vector3? emitterPosition = null, float? sampleTime = null, float birthOffset = 0,
        float? emissionTime = null, int batchIndex = 0, int batchCount = 1)
    {
        float t = args.sampleTime ?? sampleTime ?? (main.duration > 0 ? Mathf.Repeat(Math.Max(0, elapsed - delay), main.duration) / main.duration : 0);
        uint seed = args.seed ?? (uint)(Random01() * uint.MaxValue);
        float Eval(MinMaxCurve c, uint salt) => c.Evaluate(t, Random(seed, salt));
        float shapeTime = emissionTime ?? (args.sampleTime.HasValue ? t * main.duration : Math.Max(0, elapsed - delay));
        Shape(shapeTime, batchIndex, batchCount, out Vector3 point, out Vector3 direction, out Color shapeColor);
        if (shapeRejected) return;
        Vector3 emitter = args.emitterWorld ?? emitterPosition ?? transform.position;
        point = ShapeWorldPosition(point) + emitter - transform.position;
        direction = transform.TransformDirection(direction);
        var p = new Particle
        {
            randomSeed = seed,
            initialEmitterVelocity = inheritVelocity.enabled && main.simulationSpace == ParticleSystemSimulationSpace.World ? InheritedSourceVelocity(args.eventSpace) : Vector3.zero,
            angularVelocity3D = args.angular ?? Vector3.zero,
            identity = ++nextIdentity, birthOffset = args.scheduledOffset ?? birthOffset,
            eventSpace = args.eventSpace,
            position = args.p.HasValue ? args.p.Value + (args.applyShapeToPosition ? FromWorld(point, args.eventSpace) - FromWorld(emitter, args.eventSpace) : Vector3.zero) : FromWorld(point, args.eventSpace),
            velocity = args.v ?? FromWorldVector(direction, args.eventSpace) * Eval(main.startSpeed, 1),
            startLifetime = args.life ?? Eval(main.startLifetime, 2),
            startSize3D = args.s ?? (main.startSize3D ? new Vector3(Eval(main.startSizeX, 3), Eval(main.startSizeY, 4), Eval(main.startSizeZ, 5)) : Vector3.one * Eval(main.startSize, 3)),
            rotation3D = args.r ?? ((main.startRotation3D ? new Vector3(Eval(main.startRotationX, 6), Eval(main.startRotationY, 7), Eval(main.startRotationZ, 8)) : new Vector3(0, 0, Eval(main.startRotation, 8))) * Mathf.Rad2Deg),
            startColor = args.c ?? (Color32)main.startColor.Evaluate(t, Random(seed, 9))
        };
        if (shape.enabled && shape.alignToDirection && !args.r.HasValue && direction.sqrMagnitude > 0)
        {
            p.alignedToDirection = true;
            p.rotation3D = (Quaternion.FromToRotation(Vector3.forward, FromWorldVector(direction, args.eventSpace)) * Quaternion.Euler(p.rotation3D)).eulerAngles;
        }
        p.startColor = (Color)p.startColor * shapeColor;
        if (Random(seed, 223) < main.flipRotation) p.rotation3D = -p.rotation3D;
        if (lifetimeByEmitterSpeed.enabled)
        {
            float speed = InheritedSourceVelocity(args.eventSpace).magnitude;
            float phase = Mathf.InverseLerp(lifetimeByEmitterSpeed.range.x, lifetimeByEmitterSpeed.range.y, speed);
            p.startLifetime *= lifetimeByEmitterSpeed.curve.Evaluate(phase, Random(seed, 224));
        }
        if (args.inheritedColor.HasValue) p.startColor = (Color)p.startColor * args.inheritedColor.Value;
        if (args.inheritedSize.HasValue) p.startSize3D = Vector3.Scale(p.startSize3D, args.inheritedSize.Value);
        if (args.inheritedRotation.HasValue) p.rotation3D += args.inheritedRotation.Value;
        if (args.inheritedLifetime.HasValue) p.startLifetime *= args.inheritedLifetime.Value;
        p.remainingLifetime = p.startLifetime;
        if (p.startLifetime <= 0) return;
        ReserveParticleSlot();
        completed = false; particles[count++] = p; RecordTrail(p); SubEmit(p, 0, p.birthOffset);
    }

}
