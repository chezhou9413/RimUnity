using System;
using System.Runtime.InteropServices;

namespace RimUnity;

//材质参数和顶点通道的四维向量。
[Serializable, StructLayout(LayoutKind.Sequential)]
public struct Vector4 : IEquatable<Vector4>
{
    public float x, y, z, w;
    public Vector4(float x, float y, float z = 0, float w = 0) { this.x = x; this.y = y; this.z = z; this.w = w; }
    public float this[int index] { readonly get => index switch { 0 => x, 1 => y, 2 => z, 3 => w, _ => throw new IndexOutOfRangeException() }; set { if (index == 0) x = value; else if (index == 1) y = value; else if (index == 2) z = value; else if (index == 3) w = value; else throw new IndexOutOfRangeException(); } }
    public static Vector4 zero => default;
    public static Vector4 one => new(1, 1, 1, 1);
    public readonly float sqrMagnitude => x * x + y * y + z * z + w * w;
    public readonly float magnitude => MathF.Sqrt(sqrMagnitude);
    public readonly Vector4 normalized => magnitude > 1e-5f ? this / magnitude : zero;
    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * Mathf.Clamp01(t);
    public static Vector4 operator +(Vector4 a, Vector4 b) => new(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
    public static Vector4 operator -(Vector4 a, Vector4 b) => new(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
    public static Vector4 operator -(Vector4 a) => a * -1;
    public static Vector4 operator *(Vector4 a, float b) => new(a.x * b, a.y * b, a.z * b, a.w * b);
    public static Vector4 operator *(float a, Vector4 b) => b * a;
    public static Vector4 operator /(Vector4 a, float b) => a * (1 / b);
    public static bool operator ==(Vector4 a, Vector4 b) => (a - b).sqrMagnitude < 1e-10f;
    public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);
    public readonly bool Equals(Vector4 other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
    public override readonly bool Equals(object other) => other is Vector4 value && Equals(value);
    public override readonly int GetHashCode() => HashCode.Combine(x, y, z, w);
    public static implicit operator Vector4(Vector3 value) => new(value.x, value.y, value.z, 0);
    public static implicit operator Vector3(Vector4 value) => new(value.x, value.y, value.z);
    public static System.Numerics.Vector4 ToNumerics(Vector4 value) => new(value.x, value.y, value.z, value.w);
}
