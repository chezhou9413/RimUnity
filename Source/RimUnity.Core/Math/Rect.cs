using System;

namespace RimUnity;

//二维矩形，边界修改保留另一侧的位置。
[Serializable]
public struct Rect : IEquatable<Rect>
{
    public float x, y, width, height;
    public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    public Rect(Vector2 position, Vector2 size) : this(position.x, position.y, size.x, size.y) { }
    public Vector2 position { readonly get => new(x, y); set { x = value.x; y = value.y; } }
    public Vector2 size { readonly get => new(width, height); set { width = value.x; height = value.y; } }
    public Vector2 center { readonly get => new(x + width * .5f, y + height * .5f); set { x = value.x - width * .5f; y = value.y - height * .5f; } }
    public float xMin { readonly get => x; set { width += x - value; x = value; } }
    public float yMin { readonly get => y; set { height += y - value; y = value; } }
    public float xMax { readonly get => x + width; set => width = value - x; }
    public float yMax { readonly get => y + height; set => height = value - y; }
    public Vector2 min { readonly get => new(xMin, yMin); set { xMin = value.x; yMin = value.y; } }
    public Vector2 max { readonly get => new(xMax, yMax); set { xMax = value.x; yMax = value.y; } }
    public static Rect zero => default;
    public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax) => new(xmin, ymin, xmax - xmin, ymax - ymin);
    public readonly bool Contains(Vector2 p) => p.x >= xMin && p.x < xMax && p.y >= yMin && p.y < yMax;
    public readonly bool Overlaps(Rect other) => other.xMax > xMin && other.xMin < xMax && other.yMax > yMin && other.yMin < yMax;
    public readonly bool Equals(Rect other) => x.Equals(other.x) && y.Equals(other.y) && width.Equals(other.width) && height.Equals(other.height);
    public override readonly bool Equals(object obj) => obj is Rect other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(x, y, width, height);
    public static bool operator ==(Rect a, Rect b) => a.Equals(b);
    public static bool operator !=(Rect a, Rect b) => !a.Equals(b);
}
