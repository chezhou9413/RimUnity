using System.Collections.Generic;

namespace RimUnity;

//轨迹以面向相机的条带提交，支持贴图平铺和沿长度颜色宽度变化。
public sealed partial class ParticleSystemRenderer
{
    private readonly List<Vector3> trailWorldPoints = new();
    private readonly List<float> trailDistances = new();

    //分别遍历两种容器，避免经 IEnumerable 装箱枚举器。
    private void DrawTrails(ParticleSystem system, Camera camera)
    {
        ClearMesh();
        var module = system.trails;
        bool ribbon = module.mode == ParticleSystemTrailMode.Ribbon;
        Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;
        if (ribbon)
            foreach (var trail in system.RibbonTrails()) AppendTrail(system, trail, true, forward);
        else
            foreach (var trail in system.trailStates.Values) AppendTrail(system, trail, false, forward);
        Submit(ref trailBatch, trailMaterial, camera);
    }

    //每个采样点只转换一次世界坐标，距离同时供贴图计算使用。
    private void AppendTrail(ParticleSystem system, ParticleSystem.TrailState trail, bool ribbon, Vector3 forward)
    {
        var module = system.trails;
        bool world = ribbon || module.worldSpace;
        var points = trail.points;
        if (points.Count < 2) return;
        trailWorldPoints.Clear(); trailDistances.Clear();
        float totalDistance = 0;
        bool needsDistance = module.textureMode != ParticleSystemTrailTextureMode.DistributePerSegment
            && module.textureMode != ParticleSystemTrailTextureMode.RepeatPerSegment && module.textureMode != ParticleSystemTrailTextureMode.Static;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 position = world ? points[i].position : system.WorldPosition(points[i].position, trail.space);
            if (needsDistance && i > 0) totalDistance += Vector3.Distance(trailWorldPoints[i - 1], position);
            trailWorldPoints.Add(position); trailDistances.Add(totalDistance);
        }
        for (int i = 1; i < points.Count; i++)
        {
            var a = points[i - 1]; var b = points[i];
            Vector3 p0 = trailWorldPoints[i - 1], p1 = trailWorldPoints[i];
            Vector3 side = Vector3.Cross(p1 - p0, forward).normalized;
            float t0 = (i - 1f) / (points.Count - 1), t1 = i / (float)(points.Count - 1);
            float width0 = module.widthOverTrail.Evaluate(t0) * (ribbon ? a.width : trail.width) * .5f, width1 = module.widthOverTrail.Evaluate(t1) * (ribbon ? b.width : trail.width) * .5f;
            Color c0 = (ribbon ? a.color : trail.color) * module.colorOverTrail.Evaluate(t0) * module.colorOverLifetime.Evaluate(float.IsPositiveInfinity(a.lifetime) ? 0 : 1 - a.remaining / a.lifetime);
            Color c1 = (ribbon ? b.color : trail.color) * module.colorOverTrail.Evaluate(t1) * module.colorOverLifetime.Evaluate(float.IsPositiveInfinity(b.lifetime) ? 0 : 1 - b.remaining / b.lifetime);
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
            int start = vertices.Count;
            Vertex(p0 - side * width0, new Vector2(u0, 0), c0); Vertex(p0 + side * width0, new Vector2(u0, 1), c0);
            Vertex(p1 + side * width1, new Vector2(u1, 1), c1); Vertex(p1 - side * width1, new Vector2(u1, 0), c1); Quad(start);
        }
    }
}
