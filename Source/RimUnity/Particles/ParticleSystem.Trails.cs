using System.Collections.Generic;

namespace RimUnity;

//拖尾状态随粒子种子关联；死亡后可独立淡出。
public sealed partial class ParticleSystem
{
    internal sealed class TrailState
    {
        internal readonly List<TrailPoint> points = new();
        internal Color color;
        internal float width;
        internal float distance;
        internal EventSpace space;
        internal bool dead;
    }
    internal struct TrailPoint { internal Vector3 position; internal float remaining, lifetime, distance, width; internal Color color; }
    internal readonly Dictionary<ulong, TrailState> trailStates = new();
    private readonly List<ulong> expiredTrails = new();
    private readonly Stack<TrailState> idleTrails = new();
    private int retainedTrailPoints;

    //每系统最多保留 32 条、合计 1024 个采样点的容量，长轨迹结束后交给 GC。
    private TrailState RentTrail()
    {
        if (!idleTrails.TryPop(out var trail)) return new TrailState();
        retainedTrailPoints -= trail.points.Capacity;
        return trail;
    }

    //清掉事件空间引用，缓存只能保留空容器。
    private void ReturnTrail(TrailState trail)
    {
        trail.points.Clear(); trail.space = null; trail.dead = false;
        trail.color = default; trail.width = trail.distance = 0;
        if (idleTrails.Count >= 32 || retainedTrailPoints + trail.points.Capacity > 1024) return;
        retainedTrailPoints += trail.points.Capacity; idleTrails.Push(trail);
    }

    //清空粒子时同时清除普通拖尾与 Ribbon。
    private void ClearTrails()
    {
        foreach (var trail in trailStates.Values) ReturnTrail(trail);
        foreach (var trail in ribbons.Values) ReturnTrail(trail);
        trailStates.Clear(); ribbons.Clear(); expiredTrails.Clear(); expiredRibbons.Clear(); ribbonOrder.Clear();
    }

    //采样容器在粒子死亡后复用，避免循环发射不断创建 List。
    private void RecordTrail(Particle p)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailRecord);
#endif

        if (!trails.enabled || trails.mode == ParticleSystemTrailMode.Ribbon || Random(p.randomSeed, 61) >= trails.ratio) return;
        if (!trailStates.TryGetValue(p.identity, out TrailState state)) trailStates.Add(p.identity, state = RentTrail());
        state.space = p.eventSpace;
        state.color = trails.inheritParticleColor ? ColorAt(p) : Color.white;
        state.width = trails.sizeAffectsWidth ? SizeAt(p).x : 1;
        Vector3 position = trails.worldSpace ? WorldPosition(p) : p.position;
        if (state.points.Count != 0 && Vector3.Distance(state.points[^1].position, position) < trails.minVertexDistance) return;
        // Unity stores this curve as a fraction of the particle's starting lifetime.
        float life = trails.lifetime.Evaluate(Age(p), Random(p.randomSeed, 62)) * p.startLifetime
            * (trails.sizeAffectsLifetime ? SizeAt(p).x : 1);
        if (life <= 0) return;
        if (state.points.Count > 0) state.distance += Vector3.Distance(state.points[^1].position, position);

#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.TrailPointsAdded, 1);
#endif
        state.points.Add(new TrailPoint { position = position, remaining = life, lifetime = life, distance = state.distance });
    }
    private void FinishTrail(ulong identity)
    {
        if (!trailStates.TryGetValue(identity, out var trail)) return;
        if (trails.dieWithParticles) { trailStates.Remove(identity); ReturnTrail(trail); }
        else trail.dead = true;
    }
    private void AgeTrails(float dt)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailAging);
#endif

        expiredTrails.Clear();
        foreach (var entry in trailStates)
        {
            var points = entry.Value.points;
            int alive = 0;
            for (int i = 0; i < points.Count; i++)
            {

#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.TrailPointVisits, 1);
#endif
                var p = points[i]; p.remaining -= dt;
                if (p.remaining > 0) points[alive++] = p;
            }
            if (alive < points.Count) points.RemoveRange(alive, points.Count - alive);
            if (points.Count == 0 && entry.Value.dead) expiredTrails.Add(entry.Key);
        }
        foreach (ulong key in expiredTrails) { ReturnTrail(trailStates[key]); trailStates.Remove(key); }
    }
}
