using System.Collections.Generic;

namespace RimUnity;

//静态扩展调用与 ParticleSystem 实例方法读取同一份真实事件缓冲。
public static class ParticlePhysicsExtensions
{
    public static int GetSafeCollisionEventSize(this ParticleSystem system) => system.GetSafeCollisionEventSize();
    public static int GetCollisionEvents(this ParticleSystem system, GameObject target, List<ParticleCollisionEvent> results) => system.GetCollisionEvents(target, results);
    public static int GetTriggerParticles(this ParticleSystem system, ParticleSystemTriggerEventType type, List<ParticleSystem.Particle> results)
        => system.GetTriggerParticles(type, results);
    public static int GetTriggerParticles(this ParticleSystem system, ParticleSystemTriggerEventType type, List<ParticleSystem.Particle> results, out ParticleSystem.ColliderData data)
        => system.GetTriggerParticles(type, results, out data);
    public static void SetTriggerParticles(this ParticleSystem system, ParticleSystemTriggerEventType type, List<ParticleSystem.Particle> values)
        => system.SetTriggerParticles(type, values);
}
