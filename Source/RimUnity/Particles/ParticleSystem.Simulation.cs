
namespace RimUnity;

//模块求值仅使用当前实例状态和粒子种子。
public sealed partial class ParticleSystem
{
    private void Integrate(ref Particle p, float dt)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Integrate);
#endif


#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.Integrations, 1);
#endif
        float age = Age(p), r = Random(p.randomSeed, 20);
        Vector3 force = new(0, -9.81f * main.gravityModifier.Evaluate(age, r), 0);
        force = ConvertSpace(force, ParticleSystemSimulationSpace.World, p.eventSpace);
        if (forceOverLifetime.enabled)
        {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detailforceOverLifetime = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Forces);
#endif

            float f = forceOverLifetime.randomized ? Random01() : r;
            force += ConvertSpace(new Vector3(forceOverLifetime.x.Evaluate(age, f), forceOverLifetime.y.Evaluate(age, f), forceOverLifetime.z.Evaluate(age, f)), forceOverLifetime.space, p.eventSpace);
        }
        force += ExternalAcceleration(p);
        p.velocity += force * dt;
        Vector3 v = p.velocity + InheritedVelocity(p);
        if (velocityOverLifetime.enabled)
        {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detailvelocityOverLifetime = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Velocity);
#endif

            var m = velocityOverLifetime;
            float speed = m.speedModifier.Evaluate(age, r);
            Vector3 linear = new(m.x.Evaluate(age, r), m.y.Evaluate(age, r), m.z.Evaluate(age, r));
            Vector3 offset = new(m.orbitalOffsetX.Evaluate(age, r), m.orbitalOffsetY.Evaluate(age, r), m.orbitalOffsetZ.Evaluate(age, r));
            Vector3 center = (p.eventSpace?.position ?? transform.position) + (m.space == ParticleSystemSimulationSpace.Local ? transform.TransformDirection(offset) : offset);
            Vector3 radius = WorldPosition(p) - center;
            if (m.space == ParticleSystemSimulationSpace.Local) radius = transform.InverseTransformDirection(radius);
            linear += radius.normalized * m.radial.Evaluate(age, r);
            Vector3 angular = new(m.orbitalX.Evaluate(age, r), m.orbitalY.Evaluate(age, r), m.orbitalZ.Evaluate(age, r));
            //以旋转位移求轨道速度，避免显式叉积积分让圆轨道逐帧膨胀。
            Vector3 orbital = dt > 0 ? (Quaternion.Euler(angular * (dt * speed * Mathf.Rad2Deg)) * radius - radius) / dt : Vector3.zero;
            v = (v + ConvertSpace(linear, m.space, p.eventSpace)) * speed + ConvertSpace(orbital, m.space, p.eventSpace);
        }
        if (limitVelocityOverLifetime.enabled)
        {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detaillimitVelocityOverLifetime = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.LimitDrag);
#endif

            var m = limitVelocityOverLifetime;
            float drag = m.drag.Evaluate(age, r);
            if (m.multiplyDragByParticleSize) drag *= SizeAt(p).magnitude;
            if (m.multiplyDragByParticleVelocity) drag *= v.magnitude;
            float dragFactor = Mathf.Exp(-Mathf.Max(0, drag) * dt);
            p.velocity *= dragFactor; v *= dragFactor;
            Vector3 limited;
            if (m.separateAxes)
            {
                Vector3 inSpace = ToModuleSpace(v, m.space, p.eventSpace);
                limited = ConvertSpace(new Vector3(Mathf.Clamp(inSpace.x, -m.limitX.Evaluate(age, r), m.limitX.Evaluate(age, r)),
                    Mathf.Clamp(inSpace.y, -m.limitY.Evaluate(age, r), m.limitY.Evaluate(age, r)), Mathf.Clamp(inSpace.z, -m.limitZ.Evaluate(age, r), m.limitZ.Evaluate(age, r))), m.space, p.eventSpace);
            }
            else limited = Vector3.ClampMagnitude(v, Mathf.Max(0, m.limit.Evaluate(age, r)));
            Vector3 change = (limited - v) * (1 - Mathf.Pow(1 - Mathf.Clamp01(m.dampen), dt * 60));
            v += change; p.velocity += change;
        }
        if (noise.enabled)
        {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detailnoise = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.NoiseModule);
#endif

            Vector3 n = NoiseVector(p, age);
            v += n * noise.positionAmount.Evaluate(age, r);
            p.rotation3D += n * (noise.rotationAmount.Evaluate(age, r) * dt * Mathf.Rad2Deg);
        }
        p.moduleVelocity = v - p.velocity;
        p.position += v * dt;
        p.rotation3D += p.angularVelocity3D * dt;
        if (rotationOverLifetime.enabled)
        {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detailrotationOverLifetime = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Rotation);
#endif

            var m = rotationOverLifetime;
            p.rotation3D += new Vector3(m.separateAxes ? m.x.Evaluate(age, r) : 0, m.separateAxes ? m.y.Evaluate(age, r) : 0, m.z.Evaluate(age, r)) * (dt * Mathf.Rad2Deg);
        }
        if (rotationBySpeed.enabled)
        {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detailrotationBySpeed = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Rotation);
#endif

            var m = rotationBySpeed;
            float speed = SpeedPhase(p, m.range);
            p.rotation3D += new Vector3(m.separateAxes ? m.x.Evaluate(speed, r) : 0, m.separateAxes ? m.y.Evaluate(speed, r) : 0, m.z.Evaluate(speed, r)) * (dt * Mathf.Rad2Deg);
        }
    }
    //无限寿命保持曲线起点；有限寿命仍尊重脚本对粒子快照剩余时间的修改。
    internal static float Age(in Particle p) => p.startLifetime > 0
        ? float.IsPositiveInfinity(p.startLifetime) ? 0 : Mathf.Clamp01(1 - p.remainingLifetime / p.startLifetime) : 1;
    internal Color ColorAt(in Particle p)
    {
        // Color32 conversion is finite; multiplying by white twice cannot change it.
        if (!colorOverLifetime.enabled && !colorBySpeed.enabled) return p.startColor;
        return (Color)p.startColor * (colorOverLifetime.enabled ? colorOverLifetime.color.Evaluate(Age(p), Random(p.randomSeed, 30)) : Color.white)
            * (colorBySpeed.enabled ? colorBySpeed.color.Evaluate(SpeedPhase(p, colorBySpeed.range), Random(p.randomSeed, 32)) : Color.white);
    }
    internal Vector3 SizeAt(in Particle p)
    {
        if (!sizeOverLifetime.enabled && !sizeBySpeed.enabled && !noise.enabled) return p.startSize3D;
        float age = Age(p), r = Random(p.randomSeed, 31);
        Vector3 size = p.startSize3D;
        if (sizeOverLifetime.enabled)
        {
            var m = sizeOverLifetime;
            size = Vector3.Scale(size, m.separateAxes ? new Vector3(m.x.Evaluate(age, r), m.y.Evaluate(age, r), m.z.Evaluate(age, r)) : Vector3.one * m.size.Evaluate(age, r));
        }
        if (sizeBySpeed.enabled)
        {
            var m = sizeBySpeed; float speed = SpeedPhase(p, m.range);
            size = Vector3.Scale(size, m.separateAxes ? new Vector3(m.x.Evaluate(speed, r), m.y.Evaluate(speed, r), m.z.Evaluate(speed, r)) : Vector3.one * m.size.Evaluate(speed, r));
        }
        if (noise.enabled)
        {
            float amount = noise.sizeAmount.Evaluate(age, r);
            if (amount != 0) size += NoiseVector(p, age) * amount;
        }
        return size;
    }

    //坐标轴模块显式转换空间，避免世界力随发射器旋转。
    private Vector3 ConvertSpace(Vector3 v, ParticleSystemSimulationSpace space, EventSpace eventSpace)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.SpaceConversions);
#endif

        Vector3 world = space == ParticleSystemSimulationSpace.Local ? transform.TransformDirection(v) : v;
        return FromWorldVector(world, eventSpace);
    }
    private Vector3 ToModuleSpace(Vector3 v, ParticleSystemSimulationSpace space, EventSpace eventSpace)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.SpaceConversions);
#endif

        Vector3 world = WorldVector(v, eventSpace);
        return space == ParticleSystemSimulationSpace.Local ? transform.InverseTransformDirection(world) : world;
    }

    //同一播放周期共享三维旋涡场，粒子种子只影响强度和重映射曲线。
    private Vector3 NoiseVector(Particle p, float age)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.MeasureNoise();
#endif


#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.NoiseCalls, 1);
#endif
        float frequency = Mathf.Max(.0001f, noise.frequency), amplitude = 1, total = 0;
        Vector3 point = p.position * frequency + Vector3.one * (elapsed * noise.scrollSpeed.Evaluate(age));
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < noise.octaveCount; i++)
        {

#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.NoiseOctaves, 1);
#endif
            sum += ParticleNoise.Curl(point, randomSeed, noise.quality) * amplitude;
            total += amplitude; amplitude *= noise.octaveMultiplier; point *= noise.octaveScale;
        }
        float r = Random(p.randomSeed, 40);
        Vector3 strength = noise.separateAxes ? new Vector3(noise.strengthX.Evaluate(age, r), noise.strengthY.Evaluate(age, r), noise.strengthZ.Evaluate(age, r)) : Vector3.one * noise.strength.Evaluate(age, r);
        Vector3 value = sum / Mathf.Max(total, .0001f);
        if (noise.remapEnabled)
            value = new Vector3((noise.separateAxes ? noise.remapX : noise.remap).Evaluate(value.x * .5f + .5f, r),
                (noise.separateAxes ? noise.remapY : noise.remap).Evaluate(value.y * .5f + .5f, r),
                (noise.separateAxes ? noise.remapZ : noise.remap).Evaluate(value.z * .5f + .5f, r));
        return Vector3.Scale(value, strength) / (noise.damping ? frequency : 1);
    }
}
