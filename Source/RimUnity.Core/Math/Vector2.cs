using System;
using System.Runtime.InteropServices;

namespace RimUnity;

//二维向量及常用数值运算。
[Serializable, StructLayout(LayoutKind.Sequential)]
public struct Vector2 : IEquatable<Vector2>
{
    public float x, y;
    public Vector2(float x, float y) { this.x = x; this.y = y; }
    public float this[int index] { readonly get => index switch { 0 => x, 1 => y, _ => throw new IndexOutOfRangeException() }; set { if (index == 0) x = value; else if (index == 1) y = value; else throw new IndexOutOfRangeException(); } }
    public static Vector2 zero => default;
    public static Vector2 one => new(1, 1);
    public static Vector2 right => new(1, 0);
    public static Vector2 left => new(-1, 0);
    public static Vector2 up => new(0, 1);
    public static Vector2 down => new(0, -1);
    public readonly float sqrMagnitude => x * x + y * y;
    public readonly float magnitude => MathF.Sqrt(sqrMagnitude);
    public readonly Vector2 normalized => magnitude > 1e-5f ? this / magnitude : zero;
    public void Normalize() => this = normalized;
    public void Set(float x, float y) { this.x = x; this.y = y; }
    public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
    public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
    public static Vector2 Scale(Vector2 a, Vector2 b) => new(a.x * b.x, a.y * b.y);
    public static Vector2 Min(Vector2 a, Vector2 b) => new(MathF.Min(a.x, b.x), MathF.Min(a.y, b.y));
    public static Vector2 Max(Vector2 a, Vector2 b) => new(MathF.Max(a.x, b.x), MathF.Max(a.y, b.y));
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => LerpUnclamped(a, b, Mathf.Clamp01(t));
    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) => a + (b - a) * t;
    public static Vector2 ClampMagnitude(Vector2 value, float max) => value.sqrMagnitude > max * max ? value.normalized * max : value;
    public static Vector2 MoveTowards(Vector2 a, Vector2 b, float delta) => Distance(a, b) <= delta ? b : a + (b - a).normalized * delta;
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);
    public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
    public static Vector2 operator *(float a, Vector2 b) => b * a;
    public static Vector2 operator /(Vector2 a, float b) => new(a.x / b, a.y / b);
    public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-10f;
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
    public readonly bool Equals(Vector2 other) => x.Equals(other.x) && y.Equals(other.y);
    public override readonly bool Equals(object other) => other is Vector2 value && Equals(value);
    public override readonly int GetHashCode() => HashCode.Combine(x, y);
    public override readonly string ToString() => $"({x}, {y})";
    public static implicit operator Vector3(Vector2 value) => new(value.x, value.y, 0);
    public static implicit operator Vector2(Vector3 value) => new(value.x, value.y);
    public static System.Numerics.Vector2 ToNumerics(Vector2 value) => new(value.x, value.y);
}
