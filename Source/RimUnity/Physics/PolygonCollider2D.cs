using System;

namespace RimUnity;

//多边形路径保留凹边界与孔洞，粒子查询不填充外包矩形。
public sealed class PolygonCollider2D : Collider2D
{
    public Vector2[][] paths = { new[] { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(.5f, .5f), new Vector2(-.5f, .5f) } };
    public Vector2[] points { get => GetPath(0); set => SetPath(0, value); }
    public int pathCount
    {
        get => paths.Length;
        set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); Array.Resize(ref paths, value); }
    }
    public Vector2[] GetPath(int index) => (Vector2[])paths[index].Clone();
    public void SetPath(int index, Vector2[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 3) throw new ArgumentException("多边形路径至少需要三个点。", nameof(points));
        paths[index] = (Vector2[])points.Clone();
    }
    internal override ParticlePlanarShape PlanarShape
    {
        get
        {
            var world = new Vector3[paths.Length][];
            for (int p = 0; p < paths.Length; p++)
            {
                if (paths[p] == null || paths[p].Length < 3) throw new InvalidOperationException("多边形路径至少需要三个点。");
                world[p] = new Vector3[paths[p].Length];
                for (int i = 0; i < paths[p].Length; i++) world[p][i] = WorldPoint(paths[p][i]);
            }
            return new ParticlePlanarShape { kind = ParticlePlanarShape.Kind.Polygon, paths = world };
        }
    }
}
