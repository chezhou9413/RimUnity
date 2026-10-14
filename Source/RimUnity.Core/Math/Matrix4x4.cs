using System;
using System.Runtime.InteropServices;
using Native = System.Numerics.Matrix4x4;

namespace RimUnity;

//对外使用列向量变换；仅在与 System.Numerics 行向量函数交互时转置。
[Serializable, StructLayout(LayoutKind.Sequential)]
public struct Matrix4x4 : IEquatable<Matrix4x4>
{
    public float m00, m10, m20, m30, m01, m11, m21, m31, m02, m12, m22, m32, m03, m13, m23, m33;
    public Matrix4x4(Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3) : this() { SetColumn(0, c0); SetColumn(1, c1); SetColumn(2, c2); SetColumn(3, c3); }
    public float this[int row, int column] { readonly get => this[row + column * 4]; set => this[row + column * 4] = value; }
    public float this[int index]
    {
        readonly get => index switch { 0 => m00, 1 => m10, 2 => m20, 3 => m30, 4 => m01, 5 => m11, 6 => m21, 7 => m31, 8 => m02, 9 => m12, 10 => m22, 11 => m32, 12 => m03, 13 => m13, 14 => m23, 15 => m33, _ => throw new IndexOutOfRangeException() };
        set { switch (index) { case 0: m00 = value; break; case 1: m10 = value; break; case 2: m20 = value; break; case 3: m30 = value; break; case 4: m01 = value; break; case 5: m11 = value; break; case 6: m21 = value; break; case 7: m31 = value; break; case 8: m02 = value; break; case 9: m12 = value; break; case 10: m22 = value; break; case 11: m32 = value; break; case 12: m03 = value; break; case 13: m13 = value; break; case 14: m23 = value; break; case 15: m33 = value; break; default: throw new IndexOutOfRangeException(); } }
    }
    public static Matrix4x4 identity => new() { m00 = 1, m11 = 1, m22 = 1, m33 = 1 };
    public static Matrix4x4 zero => default;
    public readonly Matrix4x4 inverse => Native.Invert(ToNumerics(this), out var result) ? FromNumerics(result) : zero;
    public readonly Matrix4x4 transpose => FromNumerics(Native.Transpose(ToNumerics(this)));
    public readonly float determinant => ToNumerics(this).GetDeterminant();
    public readonly Vector3 lossyScale => new(new Vector3(m00, m10, m20).magnitude * (determinant < 0 ? -1 : 1), new Vector3(m01, m11, m21).magnitude, new Vector3(m02, m12, m22).magnitude);
    public readonly Quaternion rotation => Quaternion.LookRotation(new(m02, m12, m22), new(m01, m11, m21));
    public readonly Vector4 GetColumn(int index) => new(this[0, index], this[1, index], this[2, index], this[3, index]);
    public readonly Vector4 GetRow(int index) => new(this[index, 0], this[index, 1], this[index, 2], this[index, 3]);
    public void SetColumn(int index, Vector4 v) { for (int i = 0; i < 4; i++) this[i, index] = v[i]; }
    public void SetRow(int index, Vector4 v) { for (int i = 0; i < 4; i++) this[index, i] = v[i]; }
    public static Matrix4x4 TRS(Vector3 position, Quaternion rotation, Vector3 scale) => Translate(position) * Rotate(rotation) * Scale(scale);
    public static Matrix4x4 Translate(Vector3 v) { var m = identity; m.m03 = v.x; m.m13 = v.y; m.m23 = v.z; return m; }
    public static Matrix4x4 Scale(Vector3 v) => new() { m00 = v.x, m11 = v.y, m22 = v.z, m33 = 1 };
    public static Matrix4x4 Rotate(Quaternion q) => FromNumerics(Native.Transpose(Native.CreateFromQuaternion(Quaternion.ToNumerics(q.normalized))));
    public readonly Vector3 MultiplyPoint3x4(Vector3 p) => new(m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
    public readonly Vector3 MultiplyPoint(Vector3 p) => MultiplyPoint3x4(p) / (m30 * p.x + m31 * p.y + m32 * p.z + m33);
    public readonly Vector3 MultiplyVector(Vector3 p) => new(m00 * p.x + m01 * p.y + m02 * p.z, m10 * p.x + m11 * p.y + m12 * p.z, m20 * p.x + m21 * p.y + m22 * p.z);
    public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => FromNumerics(ToNumerics(a) * ToNumerics(b));
    public static Vector4 operator *(Matrix4x4 m, Vector4 v) => new(m.m00 * v.x + m.m01 * v.y + m.m02 * v.z + m.m03 * v.w, m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13 * v.w, m.m20 * v.x + m.m21 * v.y + m.m22 * v.z + m.m23 * v.w, m.m30 * v.x + m.m31 * v.y + m.m32 * v.z + m.m33 * v.w);
    public readonly bool Equals(Matrix4x4 other) => ToNumerics(this).Equals(ToNumerics(other));
    public override readonly bool Equals(object obj) => obj is Matrix4x4 m && Equals(m);
    public override readonly int GetHashCode() => ToNumerics(this).GetHashCode();
    public static bool operator ==(Matrix4x4 a, Matrix4x4 b) => a.Equals(b);
    public static bool operator !=(Matrix4x4 a, Matrix4x4 b) => !a.Equals(b);
    //Waylith 接收的仍是列向量矩阵的逐元素表示，不在提交时再次转置。
    public static Native ToNumerics(Matrix4x4 m) => new(m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33);
    public static Matrix4x4 FromNumerics(Native m) => new() { m00 = m.M11, m01 = m.M12, m02 = m.M13, m03 = m.M14, m10 = m.M21, m11 = m.M22, m12 = m.M23, m13 = m.M24, m20 = m.M31, m21 = m.M32, m22 = m.M33, m23 = m.M34, m30 = m.M41, m31 = m.M42, m32 = m.M43, m33 = m.M44 };
}
