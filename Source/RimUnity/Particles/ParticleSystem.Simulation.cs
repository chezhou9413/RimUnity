
namespace RimUnity;

//模块求值仅使用当前实例状态和粒子种子。
public sealed partial class ParticleSystem
{
    private void Integrate(ref Particle p, float dt)
    {
        float age = Age(p), r = Random(p.randomSeed, 20);
        Vector3 force = new(0, -9.81f * main.gravityModifier.Evaluate(age, r), 0);
        force = ConvertSpace(force, ParticleSystemSimulationSpace.World, p.eventSpace);
        if (forceOverLifetime.enabled)
        {
            float f = forceOverLifetime.randomized ? Random01() : r;
            force += ConvertSpace(new Vector3(forceOverLifetime.x.Evaluate(age, f), forceOverLifetime.y.Evaluate(age, f), forceOverLifetime.z.Evaluate(age, f)), forceOverLifetime.space, p.eventSpace);
        }
        force += ExternalAcceleration(p);
        p.velocity += force * dt;
        Vector3 v = p.velocity + InheritedVelocity(p);
        if (velocityOverLifetime.enabled)
        {
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
            Vector3 n = NoiseVector(p, age);
            v += n * noise.positionAmount.Evaluate(age, r);
            p.rotation3D += n * (noise.rotationAmount.Evaluate(age, r) * dt * Mathf.Rad2Deg);
        }
        p.moduleVelocity = v - p.velocity;
        p.position += v * dt;
        p.rotation3D += p.angularVelocity3D * dt;
        if (rotationOverLifetime.enabled)
        {
            var m = rotationOverLifetime;
            p.rotation3D += new Vector3(m.separateAxes ? m.x.Evaluate(age, r) : 0, m.separateAxes ? m.y.Evaluate(age, r) : 0, m.z.Evaluate(age, r)) * (dt * Mathf.Rad2Deg);
        }
        if (rotationBySpeed.enabled)
        {
            var m = rotationBySpeed;
            float speed = SpeedPhase(p, m.range);
            p.rotation3D += new Vector3(m.separateAxes ? m.x.Evaluate(speed, r) : 0, m.separateAxes ? m.y.Evaluate(speed, r) : 0, m.z.Evaluate(speed, r)) * (dt * Mathf.Rad2Deg);
        }
    }
    //无限寿命保持曲线起点；有限寿命仍尊重脚本对粒子快照剩余时间的修改。
    internal static float Age(Particle p) => p.startLifetime > 0
        ? float.IsPositiveInfinity(p.startLifetime) ? 0 : Mathf.Clamp01(1 - p.remainingLifetime / p.startLifetime) : 1;
    internal Color ColorAt(Particle p) => (Color)p.startColor * (colorOverLifetime.enabled ? colorOverLifetime.color.Evaluate(Age(p), Random(p.randomSeed, 30)) : Color.white)
        * (colorBySpeed.enabled ? colorBySpeed.color.Evaluate(SpeedPhase(p, colorBySpeed.range), Random(p.randomSeed, 32)) : Color.white);
    internal Vector3 SizeAt(Particle p)
    {
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
        if (noise.enabled) size += NoiseVector(p, age) * noise.sizeAmount.Evaluate(age, r);
        return size;
    }

    //坐标轴模块显式转换空间，避免世界力随发射器旋转。
    private Vector3 ConvertSpace(Vector3 v, ParticleSystemSimulationSpace space, EventSpace eventSpace)
    {
        Vector3 world = space == ParticleSystemSimulationSpace.Local ? transform.TransformDirection(v) : v;
        return FromWorldVector(world, eventSpace);
    }
    private Vector3 ToModuleSpace(Vector3 v, ParticleSystemSimulationSpace space, EventSpace eventSpace)
    {
        Vector3 world = WorldVector(v, eventSpace);
        return space == ParticleSystemSimulationSpace.Local ? transform.InverseTransformDirection(world) : world;
    }

    //连续值噪声按八角点插值，不以每帧随机抖动代替噪声场。
    private Vector3 NoiseVector(Particle p, float age)
    {
        float frequency = Mathf.Max(.0001f, noise.frequency), amplitude = 1, total = 0;
        Vector3 point = p.position * frequency + Vector3.one * (elapsed * noise.scrollSpeed.Evaluate(age));
        if (noise.quality == ParticleSystemNoiseQuality.Low) { point.y = 0; point.z = 0; }
        else if (noise.quality == ParticleSystemNoiseQuality.Medium) point.z = 0;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < noise.octaveCount; i++)
        {
            sum += new Vector3(ValueNoise(point, p.randomSeed), ValueNoise(point, p.randomSeed + 101), ValueNoise(point, p.randomSeed + 211)) * amplitude;
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
    private static float ValueNoise(Vector3 p, uint seed)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        float a = p.x - x, b = p.y - y, c = p.z - z;
        a = a * a * (3 - 2 * a); b = b * b * (3 - 2 * b); c = c * c * (3 - 2 * c);
        float H(int dx, int dy, int dz) => Random(seed, unchecked((uint)((x + dx) * 73856093 ^ (y + dy) * 19349663 ^ (z + dz) * 83492791))) * 2 - 1;
        return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(H(0, 0, 0), H(1, 0, 0), a), Mathf.Lerp(H(0, 1, 0), H(1, 1, 0), a), b),
            Mathf.Lerp(Mathf.Lerp(H(0, 0, 1), H(1, 0, 1), a), Mathf.Lerp(H(0, 1, 1), H(1, 1, 1), a), b), c);
    }
}
