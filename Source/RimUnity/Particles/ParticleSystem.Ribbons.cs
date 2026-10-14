using System;
using System.Collections.Generic;

namespace RimUnity;

//Ribbon 按出生顺序连接当前粒子，事件子发射器可选择独立成带。
public sealed partial class ParticleSystem
{
    private readonly Dictionary<(int, EventSpace), TrailState> ribbons = new();
    private readonly List<int> ribbonOrder = new();

    //每条带复用采样存储，不为每次绘制复制完整粒子数组。
    internal IEnumerable<TrailState> RibbonTrails()
    {
        foreach (var ribbon in ribbons.Values) ribbon.points.Clear();
        ribbonOrder.Clear();
        for (int i = 0; i < count; i++) ribbonOrder.Add(i);
        ribbonOrder.Sort((a, b) => particles[a].identity.CompareTo(particles[b].identity));
        int number = Math.Max(1, trails.ribbonCount);
        foreach (int index in ribbonOrder)
        {
            Particle p = particles[index];
            if (Random(p.randomSeed, 61) >= trails.ratio) continue;
            var key = ((int)(p.identity % (ulong)number), trails.splitSubEmitterRibbons ? p.eventSpace : null);
            if (!ribbons.TryGetValue(key, out var ribbon)) ribbons.Add(key, ribbon = new TrailState());
            Vector3 position = WorldPosition(p);
            float distance = ribbon.points.Count == 0 ? 0 : ribbon.points[^1].distance + Vector3.Distance(ribbon.points[^1].position, position);
            ribbon.points.Add(new TrailPoint { position = position, distance = distance, remaining = p.remainingLifetime, lifetime = p.startLifetime,
                width = trails.sizeAffectsWidth ? Mathf.Abs(SizeAt(p).x * ParticleScale(p).x) : 1,
                color = trails.inheritParticleColor ? ColorAt(p) : Color.white });
        }
        //结束的事件带不永久保留，避免长时间循环发射累积空容器。
        var expired = new List<(int, EventSpace)>();
        foreach (var pair in ribbons) if (pair.Value.points.Count == 0) expired.Add(pair.Key);
        foreach (var key in expired) ribbons.Remove(key);
        return ribbons.Values;
    }
}
