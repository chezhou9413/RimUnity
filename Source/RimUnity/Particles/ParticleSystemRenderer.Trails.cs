using System.Collections.Generic;

namespace RimUnity;

//轨迹以面向相机的条带提交，支持贴图平铺和沿长度颜色宽度变化。
public sealed partial class ParticleSystemRenderer
{
    // Diagnostic clone state only; default false, no saved/global ownership.
    internal bool LabLegacyTrailWrites;
    private readonly List<Vector3> trailWorldPoints = new();
    private readonly List<float> trailDistances = new();

    //分别遍历两种容器，避免经 IEnumerable 装箱枚举器。
    private void DrawTrails(ParticleSystem system, Camera camera)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailGeometry, system.GetInstanceID());
#endif

        using var cpu = Diagnostics.LabCpuMetrics.Measure(Diagnostics.LabCpuStage.TrailGeometry);
        using (Diagnostics.LabTrailCpu.Measure(Diagnostics.LabTrailCpu.Stage.Geometry, system.GetInstanceID(), LabLegacyTrailWrites))
        {
            ClearMesh();
            var module = system.trails;
            var drawSpace = new ParticleSystem.DrawSpace(system);
            bool ribbon = module.mode == ParticleSystemTrailMode.Ribbon;
            Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;
            bool reserved = streamPacking != StreamPacking.General;
            if (ribbon)
            {
                if (LabLegacyTrailWrites)
                    foreach (var trail in system.RibbonTrails()) AppendLegacyTrail(system, trail, true, forward, in drawSpace);
                else if (reserved)
                    foreach (var trail in system.RibbonTrails()) AppendReservedTrail(system, trail, true, forward, in drawSpace);
                else
                    foreach (var trail in system.RibbonTrails()) AppendTrail(system, trail, true, forward, in drawSpace);
            }
            else
            {
                if (LabLegacyTrailWrites)
                    foreach (var trail in system.trailStates.Values) AppendLegacyTrail(system, trail, false, forward, in drawSpace);
                else if (reserved)
                    foreach (var trail in system.trailStates.Values) AppendReservedTrail(system, trail, false, forward, in drawSpace);
                else
                    foreach (var trail in system.trailStates.Values) AppendTrail(system, trail, false, forward, in drawSpace);
            }
        }
        using var trailSubmitCpu = Diagnostics.LabTrailCpu.Measure(Diagnostics.LabTrailCpu.Stage.Submit, system.GetInstanceID());
        Submit(ref trailBatch, trailMaterial, camera);
    }

    //每个采样点只转换一次世界坐标，距离同时供贴图计算使用。
    private void AppendTrail(ParticleSystem system, ParticleSystem.TrailState trail, bool ribbon, Vector3 forward,
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
#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.TrailSegments, points.Count - 1);
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.TrailPointVisits, points.Count);
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.TrailDistanceSteps, needsDistance ? points.Count - 1 : 0);
#endif
        // Shared segment endpoints have identical curve inputs. Carry the previous
        // endpoint only within this draw; no state survives edits or another camera.
        float previousWidth; Color previousColor;
#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailAppearance))
#endif
        {
            previousWidth = module.widthOverTrail.Evaluate(0) * (ribbon ? points[0].width : trail.width) * .5f;
            var first = points[0];
            previousColor = (ribbon ? first.color : trail.color) * module.colorOverTrail.Evaluate(0)
                * module.colorOverLifetime.Evaluate(float.IsPositiveInfinity(first.lifetime) ? 0 : 1 - first.remaining / first.lifetime);
        }
        for (int i = 1; i < points.Count; i++)
        {
            var a = points[i - 1]; var b = points[i];
            Vector3 p0 = trailWorldPoints[i - 1], p1 = trailWorldPoints[i];
            //按相机前向与轨迹方向生成横轴，让三角形正面朝向相机。
            Vector3 side; float t0, t1;
#if RIMUNITY_PARTICLE_ANALYSIS
                using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailDirections))
#endif
            {
                side = Vector3.Cross(forward, p1 - p0).normalized;
                t0 = (i - 1f) / (points.Count - 1); t1 = i / (float)(points.Count - 1);
            }
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
#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailUv))
#endif
            {
                switch (module.textureMode)
                {
                    case ParticleSystemTrailTextureMode.Tile: u0 = startDistance; u1 = distance; break;
                    case ParticleSystemTrailTextureMode.DistributePerSegment: u0 = t0; u1 = t1; break;
                    case ParticleSystemTrailTextureMode.RepeatPerSegment: u0 = 0; u1 = 1; break;
                    case ParticleSystemTrailTextureMode.Static: u0 = a.distance; u1 = b.distance; break;
                    default: u0 = totalDistance > 0 ? startDistance / totalDistance : 0; u1 = totalDistance > 0 ? distance / totalDistance : 0; break;
                }
            }
            Vector3 q0, q1, q2, q3; Color32 tint0, tint1;
#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailCorners))
#endif
            {
                q0 = p0 - side * width0; q1 = p0 + side * width0;
                q2 = p1 + side * width1; q3 = p1 - side * width1;
                tint0 = c0; tint1 = c1;
            }
            int start = vertices.Count;
            AppendTrailQuad(q0, q1, q2, q3,
                u0, u1, tint0, tint1); AppendTrailIndices(start);
            previousWidth = width1; previousColor = c1;
        }
    }

    // Trails use zero custom channels in these layouts. Reserve each channel once
    // per segment; general streams retain their established per-vertex inputs.
    private void AppendTrailQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
        float u0, float u1, Color32 c0, Color32 c1)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detail = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.VertexWrite);
#endif
        Vector2 t0 = new(u0, 0), t1 = new(u0, 1), t2 = new(u1, 1), t3 = new(u1, 0);
        if (streamPacking == StreamPacking.General)
        {
            Vertex(p0, t0, c0); Vertex(p1, t1, c0); Vertex(p2, t2, c1); Vertex(p3, t3, c1);
            return;
        }
        AppendFour(vertices, p0, p1, p2, p3);
        AppendFour(uv, t0, t1, t2, t3);
        if (streamPacking == StreamPacking.Zero) FourSlots(packedUV).Clear();
        else AppendFour(packedUV, new(u0, 0, 0, 0), new(u0, 1, 0, 0), new(u1, 1, 0, 0), new(u1, 0, 0, 0));
        FourSlots(custom1).Clear(); FourSlots(custom2).Clear();
        AppendFour(colors, c0, c0, c1, c1);
        FourSlots(normals).Fill(streamNormal); FourSlots(tangents).Fill(streamTangent);
    }
}
