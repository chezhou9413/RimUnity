using System;
using System.Collections.Generic;

namespace RimUnity;

//凸体使用面法线与边叉积的分离轴检测，不以包围球产生虚假接触。
internal static class ConvexContactGeometry
{
    internal readonly struct Polyhedron
    {
        internal readonly Vector3[] points;
        internal readonly int[] triangles;
        internal Polyhedron(Vector3[] points, int[] triangles) { this.points = points; this.triangles = triangles; }
    }
    private static readonly int[] boxTriangles = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };

    internal static Polyhedron Poly(ParticlePhysicsShape shape)
    {
        if (shape.kind == ParticlePhysicsShape.Kind.Mesh) return new Polyhedron(shape.mesh.vertices, shape.mesh.triangles);
        var points = new Vector3[8];
        for (int i = 0; i < 8; i++)
            points[i] = shape.center + shape.rotation * Vector3.Scale(shape.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        return new Polyhedron(points, boxTriangles);
    }

    //接触点取两侧支持面的中心，当前为单点接触流形。
    internal static bool Contact(Polyhedron first, Polyhedron second, out Vector3 normal, out Vector3 point, out float depth)
    {
        float best = float.PositiveInfinity; Vector3 direction = default;
        normal = point = default; depth = 0;
        if (!Faces(first) || !Faces(second)) return false;
        List<Vector3> edgesA = Edges(first), edgesB = Edges(second);
        foreach (Vector3 a in edgesA) foreach (Vector3 b in edgesB) if (!Axis(Vector3.Cross(a, b))) return false;
        if (!float.IsFinite(best)) return false;
        normal = direction; depth = best;
        point = (Support(first.points, -normal) + Support(second.points, normal)) * .5f;
        return true;

        bool Faces(Polyhedron poly)
        {
            for (int i = 0; i < poly.triangles.Length; i += 3)
                if (!Axis(Vector3.Cross(poly.points[poly.triangles[i + 1]] - poly.points[poly.triangles[i]], poly.points[poly.triangles[i + 2]] - poly.points[poly.triangles[i]]))) return false;
            return true;
        }
        bool Axis(Vector3 axis)
        {
            if (axis.sqrMagnitude < 1e-12f) return true;
            axis.Normalize(); Project(first.points, axis, out float minA, out float maxA); Project(second.points, axis, out float minB, out float maxB);
            float negative = maxA - minB, positive = maxB - minA;
            if (negative < 0 || positive < 0) return false;
            float overlap = Math.Min(negative, positive);
            if (overlap < best) { best = overlap; direction = negative < positive ? -axis : axis; }
            return true;
        }
    }

    private static void Project(Vector3[] points, Vector3 axis, out float min, out float max)
    {
        min = float.PositiveInfinity; max = float.NegativeInfinity;
        foreach (Vector3 point in points) { float value = Vector3.Dot(point, axis); min = Math.Min(min, value); max = Math.Max(max, value); }
    }
    internal static Vector3 Support(Vector3[] points, Vector3 direction)
    {
        float best = float.NegativeInfinity; Vector3 sum = default; int count = 0;
        foreach (Vector3 point in points)
        {
            float value = Vector3.Dot(point, direction);
            if (value > best + 1e-5f) { best = value; sum = point; count = 1; }
            else if (Math.Abs(value - best) <= 1e-5f) { sum += point; count++; }
        }
        return sum / Math.Max(1, count);
    }
    private static List<Vector3> Edges(Polyhedron poly)
    {
        var edges = new List<Vector3>();
        for (int i = 0; i < poly.triangles.Length; i += 3)
            for (int j = 0; j < 3; j++)
            {
                Vector3 edge = (poly.points[poly.triangles[i + (j + 1) % 3]] - poly.points[poly.triangles[i + j]]).normalized;
                bool duplicate = false;
                foreach (Vector3 existing in edges) if (Math.Abs(Vector3.Dot(existing, edge)) > .99999f) { duplicate = true; break; }
                if (!duplicate) edges.Add(edge);
            }
        return edges;
    }
}
