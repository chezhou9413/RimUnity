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
    private void RecordTrail(Particle p)
    {
        if (!trails.enabled || trails.mode == ParticleSystemTrailMode.Ribbon || Random(p.randomSeed, 61) >= trails.ratio) return;
        if (!trailStates.TryGetValue(p.identity, out TrailState state)) trailStates.Add(p.identity, state = new());
        state.space = p.eventSpace;
        state.color = trails.inheritParticleColor ? ColorAt(p) : Color.white;
        state.width = trails.sizeAffectsWidth ? SizeAt(p).x : 1;
        Vector3 position = trails.worldSpace ? WorldPosition(p) : p.position;
        if (state.points.Count != 0 && Vector3.Distance(state.points[^1].position, position) < trails.minVertexDistance) return;
        float life = trails.lifetime.Evaluate(Age(p), Random(p.randomSeed, 62)) * (trails.sizeAffectsLifetime ? SizeAt(p).x : 1);
        if (life <= 0) return;
        if (state.points.Count > 0) state.distance += Vector3.Distance(state.points[^1].position, position);
        state.points.Add(new TrailPoint { position = position, remaining = life, lifetime = life, distance = state.distance });
    }
    private void FinishTrail(ulong identity)
    {
        if (!trailStates.TryGetValue(identity, out var trail)) return;
        if (trails.dieWithParticles) trailStates.Remove(identity); else trail.dead = true;
    }
    private void AgeTrails(float dt)
    {
        expiredTrails.Clear();
        foreach (var entry in trailStates)
        {
            var points = entry.Value.points;
            for (int i = points.Count - 1; i >= 0; i--) { var p = points[i]; p.remaining -= dt; if (p.remaining <= 0) points.RemoveAt(i); else points[i] = p; }
            if (points.Count == 0 && entry.Value.dead) expiredTrails.Add(entry.Key);
        }
        foreach (ulong key in expiredTrails) trailStates.Remove(key);
    }
}
