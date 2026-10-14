using System;
using System.Collections.Generic;

namespace RimUnity;

//2D 窄相使用圆、线段与多边形三角剖分，所有接触法线严格位于 XY 平面。
internal static class PlanarContactGeometry
{
    internal static bool Contact(ParticlePlanarShape a, ParticlePlanarShape b, out Vector3 normal, out Vector3 point, out float depth)
    {
        normal = point = default; depth = 0;
        if (a.kind == ParticlePlanarShape.Kind.Edge) return Edge(a, b, out normal, out point, out depth);
        if (b.kind == ParticlePlanarShape.Kind.Edge)
        {
            bool hit = Edge(b, a, out normal, out point, out depth); normal = -normal; return hit;
        }
        if (a.kind != ParticlePlanarShape.Kind.Polygon)
        {
            if (b.kind != ParticlePlanarShape.Kind.Polygon) return PhysicsContactGeometry.Contact(Primitive(a), Primitive(b), out normal, out point, out depth);
            Vector3 start = a.center - a.axis * a.halfSegment, end = a.center + a.axis * a.halfSegment;
            Vector3 bestPoint = start, bestNormal = default;
            float bestDistance = float.PositiveInfinity;
            Consider(start); Consider(end); Consider(a.center);
            foreach (Vector3[] path in b.paths)
                for (int i = 0; i < path.Length; i++)
                {
                    TriangleGeometry.Segments(start, end, path[i], path[(i + 1) % path.Length], out Vector3 along, out _); Consider(along);
                }
            depth = a.radius - bestDistance; normal = bestNormal; point = bestPoint - normal * a.radius; return depth >= 0;
            void Consider(Vector3 at)
            {
                float distance = b.Distance(at, out Vector3 n);
                if (distance < bestDistance) { bestDistance = distance; bestPoint = at; bestNormal = n; }
            }
        }
        if (b.kind != ParticlePlanarShape.Kind.Polygon)
        {
            bool hit = Contact(b, a, out normal, out point, out depth); normal = -normal; return hit;
        }
        List<Vector3[]> first = Triangles(a.paths), second = Triangles(b.paths);
        depth = -1;
        foreach (Vector3[] x in first) foreach (Vector3[] y in second)
            if (Polygons(x, y, a.radius + b.radius, out Vector3 n, out Vector3 p, out float distance) && distance > depth)
            { depth = distance; normal = n; point = p; }
        return depth >= 0;
    }

    private static ParticlePhysicsShape Primitive(ParticlePlanarShape shape) => new()
    {
        kind = shape.kind == ParticlePlanarShape.Kind.Circle ? ParticlePhysicsShape.Kind.Sphere : ParticlePhysicsShape.Kind.Capsule,
        center = shape.center, axis = shape.axis, radius = shape.radius, halfSegment = shape.halfSegment
    };

    //每条开放边使用自身的线段胶囊，不添加终点到起点的封口边。
    private static bool Edge(ParticlePlanarShape edges, ParticlePlanarShape other, out Vector3 normal, out Vector3 point, out float depth)
    {
        normal = point = default; depth = -1;
        foreach (Vector3[] path in edges.paths)
            for (int i = 0; i + 1 < path.Length; i++)
            {
                Vector3 delta = path[i + 1] - path[i];
                var segment = new ParticlePlanarShape { kind = ParticlePlanarShape.Kind.Capsule, center = (path[i] + path[i + 1]) * .5f,
                    axis = delta.normalized, halfSegment = delta.magnitude * .5f, radius = edges.radius };
                if (Contact(segment, other, out Vector3 n, out Vector3 p, out float penetration) && penetration > depth)
                { normal = n; point = p; depth = penetration; }
            }
        return depth >= 0;
    }

    //只取边法线，不能套用 3D SAT 的 Z 分离轴把平面接触深度压成零。
    private static bool Polygons(Vector3[] a, Vector3[] b, float rounding, out Vector3 normal, out Vector3 point, out float depth)
    {
        float best = float.PositiveInfinity; Vector3 direction = default;
        normal = point = default; depth = 0;
        if (!Edges(a) || !Edges(b)) return false;
        if (rounding > 0)
            foreach (Vector3 x in a) foreach (Vector3 y in b) if (!Axis(x - y)) return false;
        normal = direction; depth = best;
        point = (ConvexContactGeometry.Support(a, -normal) + ConvexContactGeometry.Support(b, normal)) * .5f;
        return true;
        bool Edges(Vector3[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 edge = points[(i + 1) % points.Length] - points[i];
                if (edge.sqrMagnitude < 1e-12f) continue;
                if (!Axis(new Vector3(edge.y, -edge.x, 0))) return false;
            }
            return true;
        }
        bool Axis(Vector3 axis)
        {
                if (axis.sqrMagnitude < 1e-12f) return true;
                axis.Normalize();
                float minA = float.PositiveInfinity, maxA = float.NegativeInfinity, minB = float.PositiveInfinity, maxB = float.NegativeInfinity;
                foreach (Vector3 p in a) { float value = Vector3.Dot(p, axis); minA = Math.Min(minA, value); maxA = Math.Max(maxA, value); }
                foreach (Vector3 p in b) { float value = Vector3.Dot(p, axis); minB = Math.Min(minB, value); maxB = Math.Max(maxB, value); }
                float negative = maxA - minB + rounding, positive = maxB - minA + rounding;
                if (negative < 0 || positive < 0) return false;
                float overlap = Math.Min(negative, positive);
                if (overlap < best) { best = overlap; direction = negative < positive ? -axis : axis; }
            return true;
        }
    }

    //耳切保留凹轮廓；嵌套路径需要带洞剖分，不能静默填满洞口。
    private static List<Vector3[]> Triangles(Vector3[][] paths)
    {
        for (int i = 0; i < paths.Length; i++) for (int j = 0; j < paths.Length; j++)
            if (i != j && new ParticlePlanarShape { kind = ParticlePlanarShape.Kind.Polygon, paths = new[] { paths[j] } }.Distance(paths[i][0], out _) < 0)
                throw new NotSupportedException("带孔 PolygonCollider2D 的刚体接触尚需带洞剖分；粒子扫掠与 Trigger 已支持真实孔洞。");
        var result = new List<Vector3[]>();
        foreach (Vector3[] path in paths)
        {
            var remaining = new List<Vector3>(path);
            float area = 0;
            for (int i = 0; i < path.Length; i++) area += Cross(path[i], path[(i + 1) % path.Length]);
            if (area < 0) remaining.Reverse();
            while (remaining.Count > 3)
            {
                bool removed = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    Vector3 a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (Cross(b - a, c - b) <= 1e-8f) continue;
                    bool contains = false;
                    for (int j = 0; j < remaining.Count; j++)
                    {
                        if (j == i || j == (i + 1) % remaining.Count || j == (i + remaining.Count - 1) % remaining.Count) continue;
                        Vector3 p = remaining[j];
                        if (Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0) { contains = true; break; }
                    }
                    if (contains) continue;
                    result.Add(new[] { a, b, c }); remaining.RemoveAt(i); removed = true; break;
                }
                if (!removed) throw new NotSupportedException("PolygonCollider2D 路径自交或退化，无法进行刚体接触剖分。");
            }
            result.Add(remaining.ToArray());
        }
        return result;
    }
    private static float Cross(Vector3 a, Vector3 b) => a.x * b.y - a.y * b.x;
}
