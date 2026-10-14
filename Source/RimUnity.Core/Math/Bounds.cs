using System;

namespace RimUnity;

//轴对齐包围盒，用于网格裁剪和碰撞粗筛。
[Serializable]
public struct Bounds
{
    public Vector3 center, extents;
    public Bounds(Vector3 center, Vector3 size) { this.center = center; extents = size * .5f; }
    public Vector3 size { readonly get => extents * 2; set => extents = value * .5f; }
    public Vector3 min { readonly get => center - extents; set => SetMinMax(value, max); }
    public Vector3 max { readonly get => center + extents; set => SetMinMax(min, value); }
    public void SetMinMax(Vector3 min, Vector3 max) { extents = (max - min) * .5f; center = min + extents; }
    public void Encapsulate(Vector3 point) => SetMinMax(Vector3.Min(min, point), Vector3.Max(max, point));
    public void Encapsulate(Bounds bounds) { Encapsulate(bounds.min); Encapsulate(bounds.max); }
    public void Expand(float amount) => extents += Vector3.one * (amount * .5f);
    public readonly bool Contains(Vector3 point) => point.x >= min.x && point.y >= min.y && point.z >= min.z && point.x <= max.x && point.y <= max.y && point.z <= max.z;
    public readonly bool Intersects(Bounds other) => min.x <= other.max.x && max.x >= other.min.x && min.y <= other.max.y && max.y >= other.min.y && min.z <= other.max.z && max.z >= other.min.z;
    public readonly Vector3 ClosestPoint(Vector3 point) => new(Mathf.Clamp(point.x, min.x, max.x), Mathf.Clamp(point.y, min.y, max.y), Mathf.Clamp(point.z, min.z, max.z));
}
