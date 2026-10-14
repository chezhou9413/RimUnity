using System;
using System.Runtime.InteropServices;

namespace RimUnity;

//局部和世界空间共用的三维向量。
[Serializable, StructLayout(LayoutKind.Sequential)]
public struct Vector3 : IEquatable<Vector3>
{
    public float x, y, z;
    public Vector3(float x, float y, float z = 0) { this.x = x; this.y = y; this.z = z; }
    public float this[int index] { readonly get => index switch { 0 => x, 1 => y, 2 => z, _ => throw new IndexOutOfRangeException() }; set { if (index == 0) x = value; else if (index == 1) y = value; else if (index == 2) z = value; else throw new IndexOutOfRangeException(); } }
    public static Vector3 zero => default;
    public static Vector3 one => new(1, 1, 1);
    public static Vector3 right => new(1, 0, 0);
    public static Vector3 left => new(-1, 0, 0);
    public static Vector3 up => new(0, 1, 0);
    public static Vector3 down => new(0, -1, 0);
    public static Vector3 forward => new(0, 0, 1);
    public static Vector3 back => new(0, 0, -1);
    public readonly float sqrMagnitude => x * x + y * y + z * z;
    public readonly float magnitude => MathF.Sqrt(sqrMagnitude);
    public readonly Vector3 normalized => magnitude > 1e-5f ? this / magnitude : zero;
    public void Normalize() => this = normalized;
    public void Set(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 Normalize(Vector3 value) => value.normalized;
    public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
    public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => LerpUnclamped(a, b, Mathf.Clamp01(t));
    public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    public static Vector3 Min(Vector3 a, Vector3 b) => new(MathF.Min(a.x, b.x), MathF.Min(a.y, b.y), MathF.Min(a.z, b.z));
    public static Vector3 Max(Vector3 a, Vector3 b) => new(MathF.Max(a.x, b.x), MathF.Max(a.y, b.y), MathF.Max(a.z, b.z));
    public static Vector3 ClampMagnitude(Vector3 value, float max) => value.sqrMagnitude > max * max ? value.normalized * max : value;
    public static Vector3 MoveTowards(Vector3 a, Vector3 b, float delta) => Distance(a, b) <= delta ? b : a + (b - a).normalized * delta;
    public static float Angle(Vector3 a, Vector3 b) => a.sqrMagnitude * b.sqrMagnitude < 1e-15f ? 0 : MathF.Acos(Math.Clamp(Dot(a, b) / MathF.Sqrt(a.sqrMagnitude * b.sqrMagnitude), -1, 1)) * Mathf.Rad2Deg;
    public static Vector3 Project(Vector3 a, Vector3 normal) => normal.sqrMagnitude < 1e-15f ? zero : normal * (Dot(a, normal) / normal.sqrMagnitude);
    public static Vector3 ProjectOnPlane(Vector3 a, Vector3 normal) => a - Project(a, normal);
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3 operator -(Vector3 a) => new(-a.x, -a.y, -a.z);
    public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
    public static Vector3 operator *(float a, Vector3 b) => b * a;
    public static Vector3 operator /(Vector3 a, float b) => new(a.x / b, a.y / b, a.z / b);
    public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;
    public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
    public readonly bool Equals(Vector3 other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);
    public override readonly bool Equals(object other) => other is Vector3 value && Equals(value);
    public override readonly int GetHashCode() => HashCode.Combine(x, y, z);
    public override readonly string ToString() => $"({x}, {y}, {z})";
    public static System.Numerics.Vector3 ToNumerics(Vector3 value) => new(value.x, value.y, value.z);
    public static Vector3 FromNumerics(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
}
