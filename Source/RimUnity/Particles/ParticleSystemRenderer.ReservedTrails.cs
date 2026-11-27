using System;
using System.Runtime.InteropServices;

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
        var points = CollectionsMarshal.AsSpan(trail.points);
        if (points.Length < 2) return;
        trailWorldPoints.Clear(); trailDistances.Clear();
        var textureMode = module.textureMode;
        float totalDistance = 0;
        bool needsDistance = textureMode != ParticleSystemTrailTextureMode.DistributePerSegment
            && textureMode != ParticleSystemTrailTextureMode.RepeatPerSegment && textureMode != ParticleSystemTrailTextureMode.Static;

        Span<Vector3> worldPoints = default;
        Span<float> distances = default;
#if RIMUNITY_PARTICLE_ANALYSIS
        using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailPositions))
#endif
        {
            if (!world) worldPoints = AppendSlots(trailWorldPoints, points.Length);
            if (needsDistance) distances = AppendSlots(trailDistances, points.Length);
            if (!world || needsDistance)
            {
                Vector3 previous = default;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 position = world ? points[i].position : drawSpace.WorldPosition(points[i].position, trail.space);
                    if (!world) worldPoints[i] = position;
                    if (needsDistance)
                    {
                        if (i > 0) totalDistance += Vector3.Distance(previous, position);
                        distances[i] = totalDistance;
                    }
                    previous = position;
                }
            }
        }
#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.TrailSegments, points.Length - 1);
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.TrailPointVisits, points.Length);
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.TrailDistanceSteps, needsDistance ? points.Length - 1 : 0);
#endif
        int vertexStart = vertices.Count, indexStart = indices.Count;
        bool complete = false;
        try
        {
            int segments = points.Length - 1;
            ReserveTrail(segments);
            Vector3 p0 = world ? points[0].position : worldPoints[0];
            var writer = new ReservedTrailWriter(this);
            // Shared segment endpoints have identical curve inputs. Carry the previous
            // endpoint only within this draw; no state survives edits or another camera.
            float previousWidth; Color32 previousTint;
#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailAppearance))
#endif
            {
                previousWidth = module.widthOverTrail.Evaluate(0) * (ribbon ? points[0].width : trail.width) * .5f;
                var first = points[0];
                previousTint = (ribbon ? first.color : trail.color) * module.colorOverTrail.Evaluate(0)
                    * module.colorOverLifetime.Evaluate(float.IsPositiveInfinity(first.lifetime) ? 0 : 1 - first.remaining / first.lifetime);
            }
            for (int i = 1; i < points.Length; i++)
            {
                ref readonly var b = ref points[i];
                Vector3 p1 = world ? b.position : worldPoints[i];
                //按相机前向与轨迹方向生成横轴，让三角形正面朝向相机。
                Vector3 side; float t1;
#if RIMUNITY_PARTICLE_ANALYSIS
                using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailDirections))
#endif
                {
                    side = Vector3.Cross(forward, p1 - p0).normalized;
                    t1 = i / (float)segments;
                }
                float width0 = previousWidth, width1;
                Color32 nextTint;

#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailAppearance))
#endif
                {
                    width1 = module.widthOverTrail.Evaluate(t1) * (ribbon ? b.width : trail.width) * .5f;
                    nextTint = (ribbon ? b.color : trail.color) * module.colorOverTrail.Evaluate(t1) * module.colorOverLifetime.Evaluate(float.IsPositiveInfinity(b.lifetime) ? 0 : 1 - b.remaining / b.lifetime);
                }
                float u0, u1;
#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailUv))
#endif
                {
                    switch (textureMode)
                    {
                        case ParticleSystemTrailTextureMode.Tile: u0 = distances[i - 1]; u1 = distances[i]; break;
                        case ParticleSystemTrailTextureMode.DistributePerSegment: u0 = (i - 1f) / segments; u1 = t1; break;
                        case ParticleSystemTrailTextureMode.RepeatPerSegment: u0 = 0; u1 = 1; break;
                        case ParticleSystemTrailTextureMode.Static: u0 = points[i - 1].distance; u1 = b.distance; break;
                        default: u0 = totalDistance > 0 ? distances[i - 1] / totalDistance : 0; u1 = totalDistance > 0 ? distances[i] / totalDistance : 0; break;
                    }
                }
                Vector3 q0, q1, q2, q3;
#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailCorners))
#endif
                {
                    Vector3 startSide = side * width0, endSide = side * width1;
                    q0 = p0 - startSide; q1 = p0 + startSide;
                    q2 = p1 + endSide; q3 = p1 - endSide;
                }
                writer.WriteQuad(vertexStart + (i - 1) * 4, indexStart + (i - 1) * 6,
                    q0, q1, q2, q3, u0, u1, previousTint, nextTint);
                p0 = p1;
                previousWidth = width1; previousTint = nextTint;
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
