using System;
using System.Collections.Generic;

namespace RimUnity;

//局部顶点去重后缓存三角拓扑，世界顶点只在 Transform 改变时重算。
internal sealed class ParticleTriangleMesh
{
    internal readonly Vector3[] local;
    internal readonly int[] triangles;
    internal readonly bool convex, closed;
    internal readonly Vector3[] vertices;
    internal Vector3 minimum, maximum;
    private Matrix4x4 matrix;
    private bool transformed;

    internal ParticleTriangleMesh(Mesh mesh, bool convex)
    {
        this.convex = convex;
        Vector3[] source = mesh.vertices;
        var unique = new List<Vector3>(); var lookup = new Dictionary<Vector3, int>();
        int[] remap = new int[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            if (!lookup.TryGetValue(source[i], out int index)) { index = unique.Count; lookup.Add(source[i], index); unique.Add(source[i]); }
            remap[i] = index;
        }
        local = unique.ToArray(); vertices = new Vector3[local.Length];
        var indices = new List<int>(); int[] input = mesh.triangles;
        for (int i = 0; i + 2 < input.Length; i += 3)
        {
            int a = remap[input[i]], b = remap[input[i + 1]], c = remap[input[i + 2]];
            if (Vector3.Cross(local[b] - local[a], local[c] - local[a]).sqrMagnitude < 1e-16f) continue;
            indices.Add(a); indices.Add(b); indices.Add(c);
        }
        triangles = convex ? ConvexMeshHull.Build(local) : indices.ToArray();
        if (triangles.Length == 0) throw new InvalidOperationException("MeshCollider 没有有效三角形。");
        var edges = new Dictionary<(int, int), int>();
        for (int i = 0; i < triangles.Length; i += 3)
            for (int j = 0; j < 3; j++)
            {
                int a = triangles[i + j], b = triangles[i + (j + 1) % 3];
                var key = (Math.Min(a, b), Math.Max(a, b)); edges.TryGetValue(key, out int number); edges[key] = number + 1;
            }
        closed = true;
        foreach (int number in edges.Values) if (number != 2) { closed = false; break; }
    }

    //负缩放保留真实三角顶点，包含判断使用绕数绝对值。
    internal void Transform(Matrix4x4 value)
    {
        if (transformed && matrix.Equals(value)) return;
        matrix = value; transformed = true;
        minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity); maximum = -minimum;
        for (int i = 0; i < local.Length; i++)
        {
            vertices[i] = value.MultiplyPoint3x4(local[i]); minimum = Vector3.Min(minimum, vertices[i]); maximum = Vector3.Max(maximum, vertices[i]);
        }
    }

    internal float Distance(Vector3 point, out Vector3 normal)
    {
        float best = float.PositiveInfinity; Vector3 closest = default, faceNormal = Vector3.up;
        double angle = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            Vector3 at = TriangleGeometry.Closest(point, a, b, c); float distance = (point - at).sqrMagnitude;
            if (distance < best) { best = distance; closest = at; faceNormal = Vector3.Cross(b - a, c - a).normalized; }
            if (closed)
            {
                a -= point; b -= point; c -= point;
                double denominator = (double)a.magnitude * b.magnitude * c.magnitude + Vector3.Dot(a, b) * (double)c.magnitude
                    + Vector3.Dot(b, c) * (double)a.magnitude + Vector3.Dot(c, a) * (double)b.magnitude;
                angle += 2 * Math.Atan2(Vector3.Dot(a, Vector3.Cross(b, c)), denominator);
            }
        }
        bool inside = closed && Math.Abs(angle) > Math.PI * 2;
        float length = Mathf.Sqrt(best);
        normal = length > 1e-7f ? (point - closest) / length * (inside ? -1 : 1) : faceNormal;
        return inside ? -length : length;
    }

    //每个真实三角面参与求交；当前版本没有以体素或包围盒替代网格表面。
    internal bool Sweep(Vector3 from, Vector3 delta, float radius, out float fraction, out Vector3 normal)
    {
        fraction = float.PositiveInfinity;
        for (int i = 0; i < triangles.Length; i += 3)
            TriangleGeometry.Sweep(from, delta, radius, vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]], ref fraction);
        normal = default;
        if (!float.IsFinite(fraction)) return false;
        Distance(from + delta * fraction, out normal);
        if (Vector3.Dot(normal, delta) > 0) normal = -normal;
        return true;
    }
}
