using System;

namespace RimUnity;

//环形模式保留到期粒子，只有新粒子超出容量时才按出生顺序替换。
public sealed partial class ParticleSystem
{
    private bool CanEmitParticle => main.maxParticles > 0 && (count < main.maxParticles || main.ringBufferMode != ParticleSystemRingBufferMode.Disabled);

    //身份单调递增，数组交换删除后仍可找到最早出生的粒子。
    private void ReserveParticleSlot()
    {
        if (count < main.maxParticles) return;
        if (!CanEmitParticle) throw new InvalidOperationException("粒子容量已满。");
        int oldest = 0;
        for (int i = 1; i < count; i++) if (particles[i].identity < particles[oldest].identity) oldest = i;
        ReleaseParticle(particles[oldest].identity);
        particles[oldest] = particles[--count];
    }

    //循环仅改变模块寿命相位，位置和速度继续积分，不回到出生位置。
    private void UpdateRingAge(ref Particle particle)
    {
        if (main.ringBufferMode == ParticleSystemRingBufferMode.PauseUntilReplaced)
        { particle.remainingLifetime = Math.Max(0, particle.remainingLifetime); return; }
        if (main.ringBufferMode != ParticleSystemRingBufferMode.LoopUntilReplaced)
            throw new NotSupportedException("未知 Ring Buffer 模式：" + main.ringBufferMode);
        if (float.IsPositiveInfinity(particle.startLifetime)) return;
        Vector2 range = main.ringBufferLoopRange;
        if (range.x < 0 || range.y > 1 || range.x >= range.y)
            throw new InvalidOperationException("Ring Buffer 循环范围必须满足 0 ≤ 起点 < 终点 ≤ 1。");
        float age = particle.startLifetime - particle.remainingLifetime;
        float start = range.x * particle.startLifetime, end = range.y * particle.startLifetime;
        if (age >= end) particle.remainingLifetime = particle.startLifetime - start - Mathf.Repeat(age - start, end - start);
    }
}
