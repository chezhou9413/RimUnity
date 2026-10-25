using System;
using System.Collections.Generic;

namespace RimUnity;

//快照使用独立身份关联拖尾和自定义流，公开随机种子可以重复或被脚本改变。
public sealed partial class ParticleSystem
{
    private static ulong nextIdentity;

    //offset 指向系统内粒子，结果从目标数组首元素写起。
    public int GetParticles(Particle[] target) => GetParticles(target, target?.Length ?? throw new ArgumentNullException(nameof(target)), 0);
    public int GetParticles(Particle[] target, int size) => GetParticles(target, size, 0);
    public int GetParticles(Particle[] target, int size, int offset)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (size < 0 || offset < 0) throw new ArgumentOutOfRangeException(size < 0 ? nameof(size) : nameof(offset));
        int number = Math.Min(Math.Min(size, target.Length), Math.Max(0, count - offset));
        if (number != 0) Array.Copy(particles, offset, target, 0, number);
        return number;
    }

    //无偏移写入替换快照；偏移写入保留其他粒子，负寿命立即移除。
    public void SetParticles(Particle[] source) => SetParticles(source, source?.Length ?? throw new ArgumentNullException(nameof(source)), 0);
    public void SetParticles(Particle[] source, int size) => SetParticles(source, size, 0);
    public void SetParticles(Particle[] source, int size, int offset)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (size < 0 || size > source.Length) throw new ArgumentOutOfRangeException(nameof(size));
        if (offset < 0 || offset > main.maxParticles || size > main.maxParticles - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        EnsureCapacity();
        HashSet<ulong> before = new();
        for (int i = 0; i < count; i++) before.Add(particles[i].identity);
        if (offset > count) Array.Clear(particles, count, offset - count);
        Array.Copy(source, 0, particles, offset, size);
        count = offset == 0 ? size : Math.Max(count, offset + size);
        HashSet<ulong> retained = new();
        int write = 0;
        for (int i = 0; i < count; i++)
        {
            Particle p = particles[i];
            if (p.remainingLifetime <= 0) continue;
            if (p.identity == 0 || !retained.Add(p.identity)) { p.identity = ++nextIdentity; retained.Add(p.identity); }
            p.birthOffset = 0;
            if (float.IsFinite(p.startLifetime) && float.IsFinite(p.remainingLifetime)) p.elapsedAge = Math.Max(0, p.startLifetime - p.remainingLifetime);
            particles[write++] = p;
        }
        count = write;
        foreach (ulong identity in before) if (!retained.Contains(identity)) ReleaseParticle(identity);
        if (count > 0) completed = false;
    }

    //所有移除路径同步释放附属数据，保留允许死亡后继续淡出的轨迹。
    private void ReleaseParticle(ulong identity)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.DeathRelease);
#endif

        ReleasePhysicsParticle(identity);

#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.Deaths, 1);
#endif
        FinishTrail(identity); customOverrides[0].Remove(identity); customOverrides[1].Remove(identity);
    }
}
