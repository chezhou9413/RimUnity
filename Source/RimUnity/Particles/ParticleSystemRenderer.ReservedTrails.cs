namespace RimUnity;

public sealed partial class ParticleSystemRenderer
{
    // Combining reservation/rollback with general streams regressed tiered JIT.
    // Keep this fast path separate. Curve/UV/corner expressions match AppendTrail;
    // differential tests cover all nine channels, streams and capacity boundaries.
    //每个采样点只转换一次世界坐标，距离同时供贴图计算使用。
    private void AppendReservedTrail(ParticleSystem system, ParticleSystem.TrailState trail, bool ribbon, Vector3 forward,
        in ParticleSystem.DrawSpace drawSpace)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailSegments);
#endif
        var module = system.trails;
        bool world = ribbon || module.worldSpace;
        var points = trail.points;
        if (points.Count < 2) return;
        trailWorldPoints.Clear(); trailDistances.Clear();
        float totalDistance = 0;
        bool needsDistance = module.textureMode != ParticleSystemTrailTextureMode.DistributePerSegment
            && module.textureMode != ParticleSystemTrailTextureMode.RepeatPerSegment && module.textureMode != ParticleSystemTrailTextureMode.Static;

#if RIMUNITY_PARTICLE_ANALYSIS
        using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailPositions))
#endif
        {
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 position = world ? points[i].position : drawSpace.WorldPosition(points[i].position, trail.space);
            if (needsDistance && i > 0) totalDistance += Vector3.Distance(trailWorldPoints[i - 1], position);
            trailWorldPoints.Add(position); trailDistances.Add(totalDistance);
        }
        }
        int vertexStart = vertices.Count, indexStart = indices.Count;
        bool complete = false;
        try
        {
            ReserveTrail(points.Count - 1);
            // Shared segment endpoints have identical curve inputs. Carry the previous
            // endpoint only within this draw; no state survives edits or another camera.
            float previousWidth = module.widthOverTrail.Evaluate(0) * (ribbon ? points[0].width : trail.width) * .5f;
            var first = points[0];
            Color previousColor = (ribbon ? first.color : trail.color) * module.colorOverTrail.Evaluate(0)
                * module.colorOverLifetime.Evaluate(float.IsPositiveInfinity(first.lifetime) ? 0 : 1 - first.remaining / first.lifetime);
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i];
                Vector3 p0 = trailWorldPoints[i - 1], p1 = trailWorldPoints[i];
                //按相机前向与轨迹方向生成横轴，让三角形正面朝向相机。
                Vector3 side = Vector3.Cross(forward, p1 - p0).normalized;
                float t0 = (i - 1f) / (points.Count - 1), t1 = i / (float)(points.Count - 1);
                float width0 = previousWidth, width1;
                Color c0 = previousColor, c1;

#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailAppearance))
#endif
                {
                    width1 = module.widthOverTrail.Evaluate(t1) * (ribbon ? b.width : trail.width) * .5f;
                    c1 = (ribbon ? b.color : trail.color) * module.colorOverTrail.Evaluate(t1) * module.colorOverLifetime.Evaluate(float.IsPositiveInfinity(b.lifetime) ? 0 : 1 - b.remaining / b.lifetime);
                }
                float startDistance = trailDistances[i - 1], distance = trailDistances[i];
                float u0, u1;
                switch (module.textureMode)
                {
                    case ParticleSystemTrailTextureMode.Tile: u0 = startDistance; u1 = distance; break;
                    case ParticleSystemTrailTextureMode.DistributePerSegment: u0 = t0; u1 = t1; break;
                    case ParticleSystemTrailTextureMode.RepeatPerSegment: u0 = 0; u1 = 1; break;
                    case ParticleSystemTrailTextureMode.Static: u0 = a.distance; u1 = b.distance; break;
                    default: u0 = totalDistance > 0 ? startDistance / totalDistance : 0; u1 = totalDistance > 0 ? distance / totalDistance : 0; break;
                }
                WriteReservedTrailQuad(vertexStart + (i - 1) * 4, indexStart + (i - 1) * 6,
                    p0 - side * width0, p0 + side * width0, p1 + side * width1, p1 - side * width1, u0, u1, c0, c1);
                previousWidth = width1; previousColor = c1;
            }
            complete = true;
        }
        finally
        {
            // Numeric channels are aligned on entry; never expose unwritten slots.
            if (!complete) RollbackTrail(vertexStart, indexStart);
        }
    }

}
