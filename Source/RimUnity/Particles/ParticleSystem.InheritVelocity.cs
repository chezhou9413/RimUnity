using System;

namespace RimUnity;

public enum ParticleSystemInheritVelocityMode { Initial, Current }

//继承速度作为速度模块求值，不永久累加到粒子的基础速度。
public sealed partial class ParticleSystem
{
    private Vector3 stepEmitterVelocity;
    private EventSpace cachedEmitterSpace;
    private ParticleSystem cachedEmitterOwner;
    private SubEmissionEvent cachedEmitterEvent;

    //显式 Emit 与子发射器共享整批索引，BurstSpread 不会把每粒都当成独立批次。
    private void EmitParticles(EmitParams args, int amount)
    {
        for (int i = 0; i < amount && CanEmitParticle; i++)
            Spawn(args, batchIndex: i, batchCount: amount);
    }

    //Initial 固定出生源速度，Current 读取当前源速度；Local/Custom 不应用此模块。
    private Vector3 InheritedVelocity(Particle particle)
    {
        if (!inheritVelocity.enabled || main.simulationSpace != ParticleSystemSimulationSpace.World) return Vector3.zero;
        Vector3 source = inheritVelocity.mode switch
        {
            ParticleSystemInheritVelocityMode.Initial => particle.initialEmitterVelocity,
            ParticleSystemInheritVelocityMode.Current => InheritedSourceVelocity(particle.eventSpace),
            _ => throw new NotSupportedException("不支持的继承速度模式：" + inheritVelocity.mode)
        };
        return source * inheritVelocity.curve.Evaluate(Age(particle), Random(particle.randomSeed, 141));
    }

    //事件粒子继承真实父粒子的速度，不能借用共享子组件的 Transform 速度。
    private Vector3 InheritedSourceVelocity(EventSpace space)
    {
        if (space != null)
        {
            if (space == cachedEmitterSpace && cachedEmitterOwner != null && cachedEmitterOwner.subEmissionEvents.Contains(cachedEmitterEvent))
                return cachedEmitterOwner.WorldVelocity(cachedEmitterEvent.parent);
            foreach (ParticleSystem owner in Active)
                foreach (SubEmissionEvent instance in owner.subEmissionEvents)
                    if (instance.child == this && instance.space == space)
                    {
                        cachedEmitterSpace = space; cachedEmitterOwner = owner; cachedEmitterEvent = instance;
                        return owner.WorldVelocity(instance.parent);
                    }
            //父事件结束后不再有当前速度，Initial 已在出生时留存。
            return Vector3.zero;
        }
        return main.emitterVelocityMode switch
        {
            ParticleSystemEmitterVelocityMode.Transform => stepEmitterVelocity,
            ParticleSystemEmitterVelocityMode.Custom => main.emitterVelocity,
            ParticleSystemEmitterVelocityMode.Rigidbody => this.GetComponentInParent<Rigidbody>()?.GetPointVelocity(transform.position) ?? Vector3.zero,
            _ => throw new NotSupportedException("不支持的发射器速度来源：" + main.emitterVelocityMode)
        };
    }
}
