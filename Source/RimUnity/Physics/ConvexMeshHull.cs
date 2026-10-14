using System;
using System.Collections.Generic;

namespace RimUnity;

//增量凸包真实生成表面三角形，不能把 convex 标志直接套到凹网格上。
internal static class ConvexMeshHull
{
    private readonly record struct Face(int a, int b, int c);

    internal static int[] Build(Vector3[] vertices)
    {
        if (vertices.Length < 4) throw new NotSupportedException("凸 MeshCollider 至少需要四个非共面顶点。");
        int a = 0, b = 0, c = 0, d = 0;
        float farthest = 0;
        for (int i = 1; i < vertices.Length; i++)
            if ((vertices[i] - vertices[a]).sqrMagnitude > farthest) { b = i; farthest = (vertices[i] - vertices[a]).sqrMagnitude; }
        float epsilon = Math.Max(1e-7f, Mathf.Sqrt(farthest) * 1e-6f);
        farthest = 0;
        Vector3 edge = (vertices[b] - vertices[a]).normalized;
        for (int i = 0; i < vertices.Length; i++)
        {
            float distance = Vector3.Cross(vertices[i] - vertices[a], edge).sqrMagnitude;
            if (distance > farthest) { c = i; farthest = distance; }
        }
        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
        farthest = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            float distance = Math.Abs(Vector3.Dot(vertices[i] - vertices[a], normal));
            if (distance > farthest) { d = i; farthest = distance; }
        }
        if (farthest <= epsilon) throw new NotSupportedException("凸 MeshCollider 顶点共面，无法生成封闭凸体。");
        Vector3 inside = (vertices[a] + vertices[b] + vertices[c] + vertices[d]) * .25f;
        var faces = new List<Face>();
        Add(a, b, c); Add(a, d, b); Add(a, c, d); Add(b, d, c);
        for (int point = 0; point < vertices.Length; point++)
        {
            var horizon = new Dictionary<(int, int), (int, int)>();
            for (int i = faces.Count - 1; i >= 0; i--)
            {
                Face face = faces[i];
                Vector3 n = Vector3.Cross(vertices[face.b] - vertices[face.a], vertices[face.c] - vertices[face.a]).normalized;
                if (Vector3.Dot(n, vertices[point] - vertices[face.a]) <= epsilon) continue;
                Edge(face.a, face.b); Edge(face.b, face.c); Edge(face.c, face.a); faces.RemoveAt(i);
            }
            foreach (var boundary in horizon.Values) Add(boundary.Item1, boundary.Item2, point);
            void Edge(int first, int second)
            {
                var key = (Math.Min(first, second), Math.Max(first, second));
                if (!horizon.Remove(key)) horizon.Add(key, (first, second));
            }
        }
        var result = new int[faces.Count * 3];
        for (int i = 0; i < faces.Count; i++) { result[i * 3] = faces[i].a; result[i * 3 + 1] = faces[i].b; result[i * 3 + 2] = faces[i].c; }
        return result;
        void Add(int x, int y, int z)
        {
            if (Vector3.Dot(Vector3.Cross(vertices[y] - vertices[x], vertices[z] - vertices[x]), inside - vertices[x]) > 0) (y, z) = (z, y);
            faces.Add(new Face(x, y, z));
        }
    }
}
