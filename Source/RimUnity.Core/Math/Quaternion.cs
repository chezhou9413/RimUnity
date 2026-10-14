using System;
using System.Runtime.InteropServices;
using Native = System.Numerics.Quaternion;

namespace RimUnity;

//四元数使用 System.Numerics 运算，欧拉角沿 Unity 的 Z、X、Y 顺序组合。
[Serializable, StructLayout(LayoutKind.Sequential)]
public struct Quaternion : IEquatable<Quaternion>
{
    public float x, y, z, w;
    public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    public static Quaternion identity => new(0, 0, 0, 1);
    public readonly Quaternion normalized => Dot(this, this) > 1e-12f ? FromNumerics(Native.Normalize(ToNumerics(this))) : identity;
    public void Normalize() => this = normalized;
    public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
    public static Quaternion Inverse(Quaternion value) => FromNumerics(Native.Inverse(ToNumerics(value)));
    public static Quaternion AngleAxis(float angle, Vector3 axis) => axis.sqrMagnitude < 1e-12f ? identity : FromNumerics(Native.CreateFromAxisAngle(Vector3.ToNumerics(axis.normalized), angle * Mathf.Deg2Rad));
    public static Quaternion Euler(float x, float y, float z) => AngleAxis(y, Vector3.up) * AngleAxis(x, Vector3.right) * AngleAxis(z, Vector3.forward);
    public static Quaternion Euler(Vector3 euler) => Euler(euler.x, euler.y, euler.z);
    public readonly Vector3 eulerAngles
    {
        get
        {
            Matrix4x4 m = Matrix4x4.Rotate(normalized);
            float pitch = MathF.Asin(Math.Clamp(-m.m12, -1, 1));
            float yaw, roll;
            if (MathF.Abs(MathF.Cos(pitch)) > 1e-5f) { yaw = MathF.Atan2(m.m02, m.m22); roll = MathF.Atan2(m.m10, m.m11); }
            else { yaw = MathF.Atan2(-m.m20, m.m00); roll = 0; }
            return new(Mathf.Repeat(pitch * Mathf.Rad2Deg, 360), Mathf.Repeat(yaw * Mathf.Rad2Deg, 360), Mathf.Repeat(roll * Mathf.Rad2Deg, 360));
        }
    }
    public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => SlerpUnclamped(a, b, Mathf.Clamp01(t));
    public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t) => FromNumerics(Native.Slerp(ToNumerics(a.normalized), ToNumerics(b.normalized), t));
    public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => FromNumerics(Native.Lerp(ToNumerics(a), ToNumerics(b), Mathf.Clamp01(t))).normalized;
    public static Quaternion FromToRotation(Vector3 from, Vector3 to)
    {
        if (from.sqrMagnitude < 1e-12f || to.sqrMagnitude < 1e-12f) return identity;
        from.Normalize(); to.Normalize(); float dot = Vector3.Dot(from, to);
        if (dot < -.999999f) { Vector3 axis = Vector3.Cross(from, Vector3.right); if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(from, Vector3.up); return AngleAxis(180, axis); }
        Vector3 cross = Vector3.Cross(from, to);
        return new Quaternion(cross.x, cross.y, cross.z, 1 + dot).normalized;
    }
    public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.up);
    public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
    {
        if (forward.sqrMagnitude < 1e-12f) return identity;
        Vector3 f = forward.normalized, r = Vector3.Cross(upwards, f).normalized;
        if (r.sqrMagnitude < 1e-12f) return FromToRotation(Vector3.forward, f);
        Vector3 u = Vector3.Cross(f, r);
        var m = new System.Numerics.Matrix4x4(r.x, r.y, r.z, 0, u.x, u.y, u.z, 0, f.x, f.y, f.z, 0, 0, 0, 0, 1);
        return FromNumerics(Native.CreateFromRotationMatrix(m)).normalized;
    }
    public static Quaternion operator *(Quaternion a, Quaternion b) => FromNumerics(ToNumerics(a) * ToNumerics(b));
    public static Vector3 operator *(Quaternion q, Vector3 v) => Vector3.FromNumerics(System.Numerics.Vector3.Transform(Vector3.ToNumerics(v), ToNumerics(q)));
    public static bool operator ==(Quaternion a, Quaternion b) => Dot(a, b) > .999999f;
    public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);
    public readonly bool Equals(Quaternion q) => x.Equals(q.x) && y.Equals(q.y) && z.Equals(q.z) && w.Equals(q.w);
    public override readonly bool Equals(object obj) => obj is Quaternion q && Equals(q);
    public override readonly int GetHashCode() => HashCode.Combine(x, y, z, w);
    public static Native ToNumerics(Quaternion value) => new(value.x, value.y, value.z, value.w);
    public static Quaternion FromNumerics(Native value) => new(value.X, value.Y, value.Z, value.W);
}
