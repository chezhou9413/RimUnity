using System;
using System.Collections.Generic;

namespace RimUnity;

public sealed partial class ParticleSystem
{
    //对象列表按稳定引用导出，运行时脚本可动态添加和删除力场。
    public sealed partial class ExternalForcesModule
    {
        public ParticleSystemForceField[] influences = Array.Empty<ParticleSystemForceField>();
        public int influenceCount => influences.Length;
        public float multiplier { get => multiplierCurve.constant; set => multiplierCurve = value; }
        public ParticleSystemForceField GetInfluence(int index) => influences[index];
        public void SetInfluence(int index, ParticleSystemForceField field) => influences[index] = field;
        public void AddInfluence(ParticleSystemForceField field) { Array.Resize(ref influences, influences.Length + 1); influences[^1] = field; }
        public void RemoveInfluence(int index) { var list = new List<ParticleSystemForceField>(influences); list.RemoveAt(index); influences = list.ToArray(); }
        public void RemoveInfluence(ParticleSystemForceField field) { var list = new List<ParticleSystemForceField>(influences); list.Remove(field); influences = list.ToArray(); }
        public void RemoveAllInfluences() => influences = Array.Empty<ParticleSystemForceField>();
        public bool IsAffectedBy(ParticleSystemForceField field)
        {
            bool mask = (influenceMask & (1 << field.gameObject.layer)) != 0;
            bool listed = Array.IndexOf(influences, field) >= 0;
            return influenceFilter switch { 0 => mask, 1 => listed, 2 => mask || listed, _ => throw new NotSupportedException("未知力场过滤模式。") };
        }
    }

    //按模块引用和 Layer 掩码累计力场与环境风力。
    private Vector3 ExternalAcceleration(Particle particle)
    {
        if (!externalForces.enabled) return Vector3.zero;
        Vector3 position = WorldPosition(particle), velocity = WorldVelocity(particle), result = Vector3.zero;
        foreach (var field in ParticleSystemForceField.Active)
            if (externalForces.IsAffectedBy(field))
                result += field.Acceleration(position, velocity, Mathf.Abs(particle.startSize), particle.randomSeed);
        foreach (var wind in WindZone.Active)
            result += wind.Acceleration(position, elapsed, particle.randomSeed);
        return FromWorldVector(result, particle.eventSpace) * externalForces.multiplierCurve.Evaluate(Age(particle), Random(particle.randomSeed, 214));
    }
}
