
namespace RimUnity;

//三角形最近点和球扫掠共用重心分区，细长三角形不会退成其包围盒。
internal static class TriangleGeometry
{
    internal static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 >= d3 && d5 >= d6) return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
        float inverse = 1 / (va + vb + vc);
        return a + ab * (vb * inverse) + ac * (vc * inverse);
    }

    //面片两侧均参与球扫掠，边界由三条胶囊覆盖。
    internal static void Sweep(Vector3 from, Vector3 delta, float radius, Vector3 a, Vector3 b, Vector3 c, ref float best)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        float speed = Vector3.Dot(delta, normal), distance = Vector3.Dot(from - a, normal);
        if (Mathf.Abs(speed) > 1e-10f)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                if (speed * sign >= 0) continue;
                float time = (sign * radius - distance) / speed;
                Vector3 point = from + delta * time - normal * (sign * radius);
                if (time >= 0 && time <= 1 && time < best && (Closest(point, a, b, c) - point).sqrMagnitude < 1e-10f) best = time;
            }
        ParticlePhysicsShape.CapsuleTime(from, delta, a, b, radius, ref best);
        ParticlePhysicsShape.CapsuleTime(from, delta, b, c, radius, ref best);
        ParticlePhysicsShape.CapsuleTime(from, delta, c, a, radius, ref best);
    }

    //线段最近点覆盖平行和退化为点的输入。
    internal static void Segments(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 first, out Vector3 second)
    {
        Vector3 u = b - a, v = d - c, w = a - c;
        float aa = u.sqrMagnitude, bb = Vector3.Dot(u, v), cc = v.sqrMagnitude, dd = Vector3.Dot(u, w), ee = Vector3.Dot(v, w);
        float s, t;
        if (aa < 1e-12f) { s = 0; t = cc > 1e-12f ? Mathf.Clamp01(ee / cc) : 0; }
        else if (cc < 1e-12f) { t = 0; s = Mathf.Clamp01(-dd / aa); }
        else
        {
            float denominator = aa * cc - bb * bb;
            s = denominator > 1e-12f ? Mathf.Clamp01((bb * ee - cc * dd) / denominator) : 0;
            t = (bb * s + ee) / cc;
            if (t < 0) { t = 0; s = Mathf.Clamp01(-dd / aa); }
            else if (t > 1) { t = 1; s = Mathf.Clamp01((bb - dd) / aa); }
        }
        first = a + u * s; second = c + v * t;
    }
}
