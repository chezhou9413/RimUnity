using System;

namespace RimUnity;

//基元在世界空间求距离与扫掠，粒子半径参与计算，避免高速粒子穿过薄表面。
internal struct ParticlePhysicsShape
{
    internal enum Kind { Sphere, Box, Capsule, Plane, Mesh, Planar, Terrain }
    internal Kind kind;
    internal Vector3 center, extents, axis;
    internal Quaternion rotation;
    internal float radius, halfSegment;
    internal ParticleTriangleMesh mesh;
    internal ParticlePlanarShape planar;
    internal ParticleTerrainHeightfield terrain;

    //有符号距离的梯度给出外法线；内部点使用最近的退出方向。
    internal float Distance(Vector3 point, out Vector3 normal)
    {
        if (kind == Kind.Terrain) return terrain.Distance(point, out normal);
        if (kind == Kind.Mesh) return mesh.Distance(point, out normal);
        if (kind == Kind.Planar) return planar.Distance(point, out normal);
        Vector3 d = point - center;
        if (kind == Kind.Plane) { normal = axis; return Vector3.Dot(d, normal); }
        if (kind == Kind.Capsule) d -= axis * Mathf.Clamp(Vector3.Dot(d, axis), -halfSegment, halfSegment);
        if (kind != Kind.Box)
        {
            float length = d.magnitude;
            normal = length > 1e-7f ? d / length : Vector3.up;
            return length - radius;
        }
        Vector3 local = Quaternion.Inverse(rotation) * d;
        Vector3 closest = new(Mathf.Clamp(local.x, -extents.x, extents.x), Mathf.Clamp(local.y, -extents.y, extents.y), Mathf.Clamp(local.z, -extents.z, extents.z));
        Vector3 delta = local - closest;
        if (delta.sqrMagnitude > 1e-14f) { normal = rotation * delta.normalized; return delta.magnitude; }
        Vector3 gaps = extents - Abs(local);
        int face = gaps.x < gaps.y ? 0 : 1;
        if (gaps.z < gaps[face]) face = 2;
        normal = Vector3.zero; normal[face] = local[face] < 0 ? -1 : 1;
        normal = rotation * normal;
        return -gaps[face];
    }

    //扫掠返回球心接触时间；盒子采用面、圆角边及端球的精确分解。
    internal bool Sweep(Vector3 from, Vector3 delta, float particleRadius, out float fraction, out Vector3 normal)
    {
        if (kind == Kind.Terrain) return terrain.Sweep(from, delta, particleRadius, out fraction, out normal);
        float distance = Distance(from, out normal) - particleRadius;
        fraction = 0;
        if (distance < -1e-5f || distance <= 1e-5f && Vector3.Dot(delta, normal) < 0) return true;
        if (kind == Kind.Mesh) return mesh.Sweep(from, delta, particleRadius, out fraction, out normal);
        if (kind == Kind.Planar) return planar.Sweep(from, delta, particleRadius, out fraction, out normal);
        if (kind == Kind.Plane)
        {
            float approach = Vector3.Dot(delta, axis);
            if (approach >= 0) return false;
            fraction = -distance / approach;
            return fraction >= 0 && fraction <= 1;
        }
        float best = float.PositiveInfinity;
        if (kind == Kind.Sphere) SphereTime(from - center, delta, radius + particleRadius, ref best);
        else if (kind == Kind.Capsule)
            CapsuleTime(from, delta, center - axis * halfSegment, center + axis * halfSegment, radius + particleRadius, ref best);
        else
        {
            Vector3 p = Quaternion.Inverse(rotation) * (from - center), v = Quaternion.Inverse(rotation) * delta;
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3, k = (i + 2) % 3;
                if (Math.Abs(v[i]) > 1e-12f)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        if (v[i] * sign >= 0) continue;
                        float t = (sign * (extents[i] + particleRadius) - p[i]) / v[i];
                        Vector3 at = p + v * t;
                        if (t >= 0 && t <= 1 && Math.Abs(at[j]) <= extents[j] && Math.Abs(at[k]) <= extents[k]) best = Math.Min(best, t);
                    }
                for (int sj = -1; sj <= 1; sj += 2)
                    for (int sk = -1; sk <= 1; sk += 2)
                    {
                        Vector3 a = Vector3.zero, b = Vector3.zero;
                        a[i] = -extents[i]; b[i] = extents[i];
                        a[j] = b[j] = sj * extents[j]; a[k] = b[k] = sk * extents[k];
                        CapsuleTime(p, v, a, b, particleRadius, ref best);
                    }
            }
        }
        if (!float.IsFinite(best)) return false;
        fraction = best; Distance(from + delta * best, out normal);
        return Vector3.Dot(delta, normal) < 0;
    }

    //球面二次方程使用未归一化位移，根直接落在本步的零到一范围内。
    private static void SphereTime(Vector3 p, Vector3 delta, float r, ref float best)
    {
        double a = Vector3.Dot(delta, delta), b = Vector3.Dot(p, delta), c = Vector3.Dot(p, p) - (double)r * r;
        double discriminant = b * b - a * c;
        if (a <= 1e-20 || b >= 0 || discriminant < 0) return;
        double t = (-b - Math.Sqrt(discriminant)) / a;
        if (t >= 0 && t <= 1 && t < best) best = (float)t;
    }

    //胶囊由有限圆柱与两个端球组成，端球同时覆盖零长度胶囊。
    internal static void CapsuleTime(Vector3 p, Vector3 delta, Vector3 a, Vector3 b, float r, ref float best)
    {
        Vector3 axis = b - a, offset = p - a;
        double length2 = axis.sqrMagnitude;
        if (length2 > 1e-14)
        {
            double along = Vector3.Dot(axis, delta), origin = Vector3.Dot(axis, offset);
            double aa = length2 * delta.sqrMagnitude - along * along;
            double bb = length2 * Vector3.Dot(offset, delta) - origin * along;
            double cc = length2 * (offset.sqrMagnitude - r * r) - origin * origin;
            double disc = bb * bb - aa * cc;
            if (aa > 1e-14 && disc >= 0)
            {
                double t = (-bb - Math.Sqrt(disc)) / aa, y = origin + t * along;
                if (t >= 0 && t <= 1 && t < best && y >= 0 && y <= length2) best = (float)t;
            }
        }
        SphereTime(p - a, delta, r, ref best); SphereTime(p - b, delta, r, ref best);
    }

    internal static Vector3 Abs(Vector3 value) => new(Math.Abs(value.x), Math.Abs(value.y), Math.Abs(value.z));
    internal static float Max(Vector3 value) => Math.Max(value.x, Math.Max(value.y, value.z));
}
