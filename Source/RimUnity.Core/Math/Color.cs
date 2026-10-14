using System;
using System.Runtime.InteropServices;

namespace RimUnity;

//线性色值与通道运算，颜色空间转换由材质输入语义决定。
[Serializable, StructLayout(LayoutKind.Sequential)]
public struct Color : IEquatable<Color>
{
    public float r, g, b, a;
    public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
    public static Color white => new(1, 1, 1);
    public static Color black => new(0, 0, 0);
    public static Color clear => default;
    public static Color red => new(1, 0, 0);
    public static Color green => new(0, 1, 0);
    public static Color blue => new(0, 0, 1);
    public static Color yellow => new(1, 1, 0);
    public static Color gray => new(.5f, .5f, .5f);
    public static Color grey => gray;
    public readonly Color linear => new(Linear(r), Linear(g), Linear(b), a);
    public readonly Color gamma => new(Gamma(r), Gamma(g), Gamma(b), a);
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    private static float Gamma(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
    public static Color Lerp(Color x, Color y, float t) => LerpUnclamped(x, y, Mathf.Clamp01(t));
    public static Color LerpUnclamped(Color x, Color y, float t) => x + (y - x) * t;
    public static Color operator +(Color x, Color y) => new(x.r + y.r, x.g + y.g, x.b + y.b, x.a + y.a);
    public static Color operator -(Color x, Color y) => new(x.r - y.r, x.g - y.g, x.b - y.b, x.a - y.a);
    public static Color operator *(Color x, Color y) => new(x.r * y.r, x.g * y.g, x.b * y.b, x.a * y.a);
    public static Color operator *(Color x, float y) => new(x.r * y, x.g * y, x.b * y, x.a * y);
    public static Color operator *(float x, Color y) => y * x;
    public static Color operator /(Color x, float y) => x * (1 / y);
    public static implicit operator Vector4(Color c) => new(c.r, c.g, c.b, c.a);
    public static implicit operator Color(Vector4 c) => new(c.x, c.y, c.z, c.w);
    public static bool operator ==(Color x, Color y) => (Vector4)x == (Vector4)y;
    public static bool operator !=(Color x, Color y) => !(x == y);
    public readonly bool Equals(Color other) => r.Equals(other.r) && g.Equals(other.g) && b.Equals(other.b) && a.Equals(other.a);
    public override readonly bool Equals(object other) => other is Color c && Equals(c);
    public override readonly int GetHashCode() => HashCode.Combine(r, g, b, a);
}

//RGBA 字节布局直接用于顶点与像素上传。
[Serializable, StructLayout(LayoutKind.Sequential)]
public struct Color32
{
    public byte r, g, b, a;
    public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    public static implicit operator Color(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    public static implicit operator Color32(Color c) => new(Channel(c.r), Channel(c.g), Channel(c.b), Channel(c.a));
    private static byte Channel(float value) => (byte)MathF.Round(Mathf.Clamp01(value) * 255);
}
