using System;

namespace RimUnity;

//2D 形状在世界 XY 平面计算，Z 位置不参与 2D 碰撞判断。
internal sealed class ParticlePlanarShape
{
    internal enum Kind { Circle, Capsule, Polygon, Edge }
    internal Kind kind;
    internal Vector3 center, axis;
    internal float radius, halfSegment;
    internal Vector3[][] paths = Array.Empty<Vector3[]>();

    internal float Distance(Vector3 point, out Vector3 normal)
    {
        point.z = 0;
        Vector3 delta = point - center;
        if (kind == Kind.Capsule) delta -= axis * Mathf.Clamp(Vector3.Dot(delta, axis), -halfSegment, halfSegment);
        if (kind != Kind.Polygon && kind != Kind.Edge)
        {
            float distance = delta.magnitude; normal = distance > 1e-7f ? delta / distance : Vector3.up; return distance - radius;
        }
        bool inside = false; float best = float.PositiveInfinity; Vector3 closest = default, outward = Vector3.up;
        foreach (Vector3[] path in paths)
            for (int i = 0; i < (kind == Kind.Edge ? path.Length - 1 : path.Length); i++)
            {
                Vector3 a = path[i], b = path[(i + 1) % path.Length], edge = b - a;
                Vector3 at = a + edge * (edge.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(point - a, edge) / edge.sqrMagnitude) : 0);
                float distance = (point - at).sqrMagnitude;
                if (distance < best) { best = distance; closest = at; outward = new Vector3(edge.y, -edge.x, 0).normalized; }
                if (kind == Kind.Polygon && (a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
        float length = Mathf.Sqrt(best);
        normal = length > 1e-7f ? (point - closest) / length * (inside ? -1 : 1) : outward;
        return (inside ? -length : length) - radius;
    }

    //边的胶囊扫掠覆盖凹多边形边界，孔洞仍由有符号距离判定内外。
    internal bool Sweep(Vector3 from, Vector3 delta, float particleRadius, out float fraction, out Vector3 normal)
    {
        from.z = delta.z = 0;
        fraction = float.PositiveInfinity;
        if (kind == Kind.Polygon || kind == Kind.Edge)
        {
            foreach (Vector3[] path in paths)
                for (int i = 0; i < (kind == Kind.Edge ? path.Length - 1 : path.Length); i++) ParticlePhysicsShape.CapsuleTime(from, delta, path[i], path[(i + 1) % path.Length], radius + particleRadius, ref fraction);
        }
        else ParticlePhysicsShape.CapsuleTime(from, delta, center - axis * halfSegment, center + axis * halfSegment, radius + particleRadius, ref fraction);
        normal = default;
        if (!float.IsFinite(fraction)) return false;
        Distance(from + delta * fraction, out normal);
        return Vector3.Dot(delta, normal) < 0;
    }
    internal static Vector3 XY(Vector3 value) => new(value.x, value.y, 0);
}
