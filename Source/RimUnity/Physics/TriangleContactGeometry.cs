
namespace RimUnity;

//地形三角面的基础单点接触，保留面正侧并共用现有凸体求交。
internal static class TriangleContactGeometry
{
    internal static bool Contact(ParticlePhysicsShape shape, Vector3 a, Vector3 b, Vector3 c, out Vector3 normal, out Vector3 point, out float depth)
    {
        normal = Vector3.Cross(b - a, c - a).normalized; point = default; depth = 0;
        if (Vector3.Dot(shape.center - a, normal) < 0) return false;
        if (shape.kind == ParticlePhysicsShape.Kind.Sphere || shape.kind == ParticlePhysicsShape.Kind.Capsule)
        {
            Vector3 start = shape.center - shape.axis * shape.halfSegment, end = shape.center + shape.axis * shape.halfSegment;
            Vector3 onAxis = start, onFace = TriangleGeometry.Closest(start, a, b, c);
            float best = (onAxis - onFace).sqrMagnitude;
            Consider(end, TriangleGeometry.Closest(end, a, b, c));
            if (shape.kind == ParticlePhysicsShape.Kind.Capsule)
            {
                float time = float.PositiveInfinity;
                TriangleGeometry.Sweep(start, end - start, 0, a, b, c, ref time);
                if (float.IsFinite(time)) { onAxis = onFace = start + (end - start) * time; best = 0; }
                Edge(a, b); Edge(b, c); Edge(c, a);
            }
            float distance = Mathf.Sqrt(best); depth = shape.radius - distance;
            if (distance > 1e-7f) normal = (onAxis - onFace) / distance;
            point = onFace; return depth >= 0;
            void Edge(Vector3 first, Vector3 second)
            { TriangleGeometry.Segments(start, end, first, second, out Vector3 p, out Vector3 q); Consider(p, q); }
            void Consider(Vector3 p, Vector3 q)
            { float value = (p - q).sqrMagnitude; if (value < best) { best = value; onAxis = p; onFace = q; } }
        }
        var triangle = new ConvexContactGeometry.Polyhedron(new[] { a, b, c }, new[] { 0, 1, 2 });
        return ConvexContactGeometry.Contact(ConvexContactGeometry.Poly(shape), triangle, out normal, out point, out depth);
    }
}
