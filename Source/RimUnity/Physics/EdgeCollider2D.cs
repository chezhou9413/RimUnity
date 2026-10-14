using System;
using System.Collections.Generic;

namespace RimUnity;

//开放折线只包含相邻点之间的边，首尾不会被自动闭合。
public sealed class EdgeCollider2D : Collider2D
{
    public Vector2[] points = { new(-.5f, 0), new(.5f, 0) };
    public float edgeRadius;
    public bool useAdjacentStartPoint, useAdjacentEndPoint;
    public Vector2 adjacentStartPoint, adjacentEndPoint;
    public int pointCount => points.Length;
    public int edgeCount => Math.Max(0, points.Length - 1);

    //脚本设置的无效折线明确拒绝，不在运行时补出隐藏边。
    public bool SetPoints(List<Vector2> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2) return false;
        points = values.ToArray(); return true;
    }
    public int GetPoints(List<Vector2> values)
    {
        ArgumentNullException.ThrowIfNull(values); values.Clear(); values.AddRange(points); return points.Length;
    }
    internal override ParticlePlanarShape PlanarShape
    {
        get
        {
            if (points == null || points.Length < 2) throw new InvalidOperationException("EdgeCollider2D 至少需要两个点。");
            if (useAdjacentStartPoint || useAdjacentEndPoint)
                throw new NotSupportedException("EdgeCollider2D 的相邻虚拟端点法线尚未实现，不能忽略该设置。");
            if (!float.IsFinite(edgeRadius) || edgeRadius < 0) throw new InvalidOperationException("EdgeCollider2D.edgeRadius 必须为有限非负值。");
            var path = new Vector3[points.Length];
            for (int i = 0; i < path.Length; i++) path[i] = WorldPoint(points[i]);
            return new ParticlePlanarShape { kind = ParticlePlanarShape.Kind.Edge, paths = new[] { path },
                radius = edgeRadius * Mathf.Max(WorldScale.x, WorldScale.y) };
        }
    }
}
