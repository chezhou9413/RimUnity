using System;

namespace RimUnity;

//窄相按真实形状求接触法线与穿透深度，法线始终从第二个物体指向第一个。
internal static class PhysicsContactGeometry
{
    internal static bool Contact(ParticlePhysicsShape a, ParticlePhysicsShape b, out Vector3 normal, out Vector3 point, out float depth)
    {
        normal = point = default; depth = 0;
        if (a.kind == ParticlePhysicsShape.Kind.Planar || b.kind == ParticlePhysicsShape.Kind.Planar)
            return a.kind == b.kind && PlanarContactGeometry.Contact(a.planar, b.planar, out normal, out point, out depth);
        if (a.kind == ParticlePhysicsShape.Kind.Terrain || b.kind == ParticlePhysicsShape.Kind.Terrain)
        {
            if (a.kind == b.kind || a.kind == ParticlePhysicsShape.Kind.Plane || b.kind == ParticlePhysicsShape.Kind.Plane) return false;
            if (b.kind == ParticlePhysicsShape.Kind.Terrain) return b.terrain.Contact(a, out normal, out point, out depth);
            bool hit = a.terrain.Contact(b, out normal, out point, out depth); normal = -normal; return hit;
        }
        if (a.kind == ParticlePhysicsShape.Kind.Plane)
        {
            if (b.kind == ParticlePhysicsShape.Kind.Plane) return false;
            bool hit = Contact(b, a, out normal, out point, out depth); normal = -normal; return hit;
        }
        if (b.kind == ParticlePhysicsShape.Kind.Plane)
        {
            normal = b.axis; point = Support(a, -normal);
            depth = -Vector3.Dot(point - b.center, normal); return depth >= 0;
        }
        if (!BoundsOverlap(a, b)) return false;
        if (a.kind == ParticlePhysicsShape.Kind.Sphere)
        {
            depth = a.radius - b.Distance(a.center, out normal); point = a.center - normal * a.radius; return depth >= 0;
        }
        if (b.kind == ParticlePhysicsShape.Kind.Sphere)
        {
            bool hit = Contact(b, a, out normal, out point, out depth); normal = -normal; return hit;
        }
        if (a.kind == ParticlePhysicsShape.Kind.Capsule) return Capsule(a, b, out normal, out point, out depth);
        if (b.kind == ParticlePhysicsShape.Kind.Capsule)
        {
            bool hit = Capsule(b, a, out normal, out point, out depth); normal = -normal; return hit;
        }
        if (a.kind == ParticlePhysicsShape.Kind.Mesh && !a.mesh.convex)
        {
            bool hit = Mesh(b, a.mesh, out normal, out point, out depth); normal = -normal; return hit;
        }
        if (b.kind == ParticlePhysicsShape.Kind.Mesh && !b.mesh.convex) return Mesh(a, b.mesh, out normal, out point, out depth);
        return ConvexContactGeometry.Contact(ConvexContactGeometry.Poly(a), ConvexContactGeometry.Poly(b), out normal, out point, out depth);
    }

    //静态凹网格逐面检测，动态凸体不会被错误填满的网格包围盒弹开。
    private static bool Mesh(ParticlePhysicsShape shape, ParticleTriangleMesh mesh, out Vector3 normal, out Vector3 point, out float depth)
    {
        normal = point = default; depth = -1;
        if (shape.kind == ParticlePhysicsShape.Kind.Mesh && !shape.mesh.convex) throw new NotSupportedException("动态非凸 MeshCollider 需要凸体分解，当前不支持。");
        var poly = ConvexContactGeometry.Poly(shape);
        for (int i = 0; i < mesh.triangles.Length; i += 3)
        {
            var triangle = new ConvexContactGeometry.Polyhedron(new[] { mesh.vertices[mesh.triangles[i]], mesh.vertices[mesh.triangles[i + 1]], mesh.vertices[mesh.triangles[i + 2]] }, new[] { 0, 1, 2 });
            if (ConvexContactGeometry.Contact(poly, triangle, out Vector3 n, out Vector3 p, out float distance) && distance > depth)
            { depth = distance; normal = n; point = p; }
        }
        return depth >= 0;
    }

    //胶囊的线段与球半径分别参与接触计算。
    private static bool Capsule(ParticlePhysicsShape a, ParticlePhysicsShape b, out Vector3 normal, out Vector3 point, out float depth)
    {
        Vector3 start = a.center - a.axis * a.halfSegment, end = a.center + a.axis * a.halfSegment;
        Vector3 at;
        if (b.kind == ParticlePhysicsShape.Kind.Capsule)
        {
            TriangleGeometry.Segments(start, end, b.center - b.axis * b.halfSegment, b.center + b.axis * b.halfSegment, out at, out Vector3 other);
            Vector3 delta = at - other; float distance = delta.magnitude;
            normal = distance > 1e-7f ? delta / distance : Vector3.up; depth = a.radius + b.radius - distance;
        }
        else if (b.kind == ParticlePhysicsShape.Kind.Mesh && !b.mesh.convex)
        {
            float best = float.PositiveInfinity; at = start;
            for (int i = 0; i < b.mesh.triangles.Length; i += 3)
            {
                Vector3 x = b.mesh.vertices[b.mesh.triangles[i]], y = b.mesh.vertices[b.mesh.triangles[i + 1]], z = b.mesh.vertices[b.mesh.triangles[i + 2]];
                float time = float.PositiveInfinity;
                TriangleGeometry.Sweep(start, end - start, 0, x, y, z, ref time);
                if (float.IsFinite(time)) { at = start + (end - start) * time; best = 0; break; }
                Consider(start, TriangleGeometry.Closest(start, x, y, z)); Consider(end, TriangleGeometry.Closest(end, x, y, z));
                Edge(x, y); Edge(y, z); Edge(z, x);
                void Edge(Vector3 first, Vector3 second)
                { TriangleGeometry.Segments(start, end, first, second, out Vector3 p, out Vector3 q); Consider(p, q); }
                void Consider(Vector3 p, Vector3 q) { float value = (p - q).sqrMagnitude; if (value < best) { best = value; at = p; } }
            }
            depth = a.radius - b.Distance(at, out normal);
        }
        else
        {
            //凸体的有符号距离沿线段为凸函数，用有界收敛求最深接触点。
            float low = 0, high = 1;
            for (int i = 0; i < 32; i++)
            {
                float left = (2 * low + high) / 3, right = (low + 2 * high) / 3;
                if (b.Distance(Vector3.Lerp(start, end, left), out _) < b.Distance(Vector3.Lerp(start, end, right), out _)) high = right;
                else low = left;
            }
            at = Vector3.Lerp(start, end, (low + high) * .5f); depth = a.radius - b.Distance(at, out normal);
        }
        point = at - normal * a.radius; return depth >= 0;
    }

    internal static Vector3 Support(ParticlePhysicsShape shape, Vector3 direction)
    {
        if (shape.kind == ParticlePhysicsShape.Kind.Sphere) return shape.center + direction.normalized * shape.radius;
        if (shape.kind == ParticlePhysicsShape.Kind.Capsule)
            return shape.center + shape.axis * (Vector3.Dot(direction, shape.axis) < 0 ? -shape.halfSegment : shape.halfSegment) + direction.normalized * shape.radius;
        return ConvexContactGeometry.Support(ConvexContactGeometry.Poly(shape).points, direction);
    }
    private static bool BoundsOverlap(ParticlePhysicsShape a, ParticlePhysicsShape b)
    {
        Vector3 x = Bounds(a), y = Bounds(b), delta = ParticlePhysicsShape.Abs(a.center - b.center);
        return delta.x <= x.x + y.x && delta.y <= x.y + y.y && delta.z <= x.z + y.z;
    }
    internal static Vector3 Bounds(ParticlePhysicsShape shape)
    {
        if (shape.kind == ParticlePhysicsShape.Kind.Sphere) return Vector3.one * shape.radius;
        if (shape.kind == ParticlePhysicsShape.Kind.Capsule) return ParticlePhysicsShape.Abs(shape.axis) * shape.halfSegment + Vector3.one * shape.radius;
        if (shape.kind == ParticlePhysicsShape.Kind.Mesh || shape.kind == ParticlePhysicsShape.Kind.Terrain) return shape.extents;
        return ParticlePhysicsShape.Abs(shape.rotation * Vector3.right) * shape.extents.x + ParticlePhysicsShape.Abs(shape.rotation * Vector3.up) * shape.extents.y
            + ParticlePhysicsShape.Abs(shape.rotation * Vector3.forward) * shape.extents.z;
    }
}
