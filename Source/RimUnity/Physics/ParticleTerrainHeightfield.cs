using System;
using System.Collections.Generic;

namespace RimUnity;

//高度场分块包围层级仅用于筛选候选，每次命中仍在真实三角面上计算。
internal sealed class ParticleTerrainHeightfield
{
    private sealed class Node
    {
        internal int x0, z0, x1, z1, left = -1, right = -1;
        internal Vector3 localMin, localMax, min, max;
    }
    private readonly TerrainData data;
    private readonly List<Node> nodes = new();
    private Matrix4x4 matrix;
    private bool transformed;
    internal Vector3 minimum => nodes[0].min;
    internal Vector3 maximum => nodes[0].max;

    internal ParticleTerrainHeightfield(TerrainData data)
    {
        data.Validate(); this.data = data;
        foreach (float height in data.heights)
            if (!float.IsFinite(height) || height < 0 || height > 1) throw new InvalidOperationException("TerrainData 高度必须位于零到一之间。");
        Build(0, 0, data.holesResolution, data.holesResolution);
    }

    //叶子包含最多八乘八个网格单元，父包围盒由子节点合并。
    private int Build(int x0, int z0, int x1, int z1)
    {
        var node = new Node { x0 = x0, z0 = z0, x1 = x1, z1 = z1 };
        int index = nodes.Count; nodes.Add(node);
        if (x1 - x0 > 8 || z1 - z0 > 8)
        {
            if (x1 - x0 >= z1 - z0)
            { int middle = (x0 + x1) / 2; node.left = Build(x0, z0, middle, z1); node.right = Build(middle, z0, x1, z1); }
            else
            { int middle = (z0 + z1) / 2; node.left = Build(x0, z0, x1, middle); node.right = Build(x0, middle, x1, z1); }
            node.localMin = Vector3.Min(nodes[node.left].localMin, nodes[node.right].localMin);
            node.localMax = Vector3.Max(nodes[node.left].localMax, nodes[node.right].localMax);
        }
        else
        {
            node.localMin = Vector3.one * float.PositiveInfinity; node.localMax = Vector3.one * float.NegativeInfinity;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            { Vector3 p = data.Point(x, z); node.localMin = Vector3.Min(node.localMin, p); node.localMax = Vector3.Max(node.localMax, p); }
        }
        return index;
    }

    internal void Transform(Matrix4x4 value)
    {
        if (transformed && matrix.Equals(value)) return;
        matrix = value; transformed = true;
        foreach (Node node in nodes)
        {
            node.min = Vector3.one * float.PositiveInfinity; node.max = Vector3.one * float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = matrix.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? node.localMin.x : node.localMax.x,
                    (i & 2) == 0 ? node.localMin.y : node.localMax.y, (i & 4) == 0 ? node.localMin.z : node.localMax.z));
                node.min = Vector3.Min(node.min, p); node.max = Vector3.Max(node.max, p);
            }
        }
    }

    //最近面距离用包围盒下界剪枝，孔洞内不存在可碰撞三角形。
    internal float Distance(Vector3 point, out Vector3 normal)
    {
        float best = float.PositiveInfinity; Vector3 n = Vector3.up;
        Visit(0); normal = n; return Mathf.Sqrt(best);
        void Visit(int index)
        {
            Node node = nodes[index];
            if (BoxDistance(point, node) > best) return;
            if (node.left >= 0)
            {
                int first = node.left, second = node.right;
                if (BoxDistance(point, nodes[first]) > BoxDistance(point, nodes[second])) (first, second) = (second, first);
                Visit(first); Visit(second); return;
            }
            Faces(node, (a, b, c) =>
            {
                Vector3 closest = TriangleGeometry.Closest(point, a, b, c), delta = point - closest;
                if (delta.sqrMagnitude >= best) return;
                best = delta.sqrMagnitude; n = best > 1e-14f ? delta.normalized : Vector3.Cross(b - a, c - a).normalized;
            });
        }
    }

    //地形仅从三角面的正侧接触，不把地表下方解释为无限实体。
    internal bool Sweep(Vector3 from, Vector3 delta, float radius, out float fraction, out Vector3 normal)
    {
        float best = float.PositiveInfinity; Vector3 n = default;
        Vector3 low = Vector3.Min(from, from + delta) - Vector3.one * radius, high = Vector3.Max(from, from + delta) + Vector3.one * radius;
        Query(0, low, high, (a, b, c) =>
        {
            Vector3 face = Vector3.Cross(b - a, c - a).normalized;
            Vector3 closest = TriangleGeometry.Closest(from, a, b, c), difference = from - closest;
            if (Vector3.Dot(from - a, face) >= -1e-6f && difference.sqrMagnitude <= radius * radius)
            {
                Vector3 candidate = difference.sqrMagnitude > 1e-14f ? difference.normalized : face;
                if (Vector3.Dot(delta, candidate) < 0) { best = 0; n = candidate; }
                return;
            }
            float time = float.PositiveInfinity;
            TriangleGeometry.Sweep(from, delta, radius, a, b, c, ref time);
            if (!float.IsFinite(time) || time >= best) return;
            Vector3 center = from + delta * time;
            if (Vector3.Dot(center - a, face) < -1e-6f) return;
            Vector3 at = center - TriangleGeometry.Closest(center, a, b, c);
            Vector3 normalAt = at.sqrMagnitude > 1e-14f ? at.normalized : face;
            if (Vector3.Dot(delta, normalAt) >= 0) return;
            best = time; n = normalAt;
        });
        fraction = best; normal = n; return float.IsFinite(best);
    }

    //刚体与高度场也使用实际候选三角面，地形本身只能静态或运动学。
    internal bool Contact(ParticlePhysicsShape other, out Vector3 normal, out Vector3 point, out float depth)
    {
        float best = -1; Vector3 n = default, p = default;
        Vector3 extents = PhysicsContactGeometry.Bounds(other);
        Query(0, other.center - extents, other.center + extents, (a, b, c) =>
        {
            if (TriangleContactGeometry.Contact(other, a, b, c, out Vector3 nextNormal, out Vector3 nextPoint, out float nextDepth) && nextDepth > best)
            { best = nextDepth; n = nextNormal; p = nextPoint; }
        });
        normal = n; point = p; depth = best; return best >= 0;
    }

    private void Query(int index, Vector3 low, Vector3 high, Action<Vector3, Vector3, Vector3> action)
    {
        Node node = nodes[index];
        if (low.x > node.max.x || high.x < node.min.x || low.y > node.max.y || high.y < node.min.y || low.z > node.max.z || high.z < node.min.z) return;
        if (node.left >= 0) { Query(node.left, low, high, action); Query(node.right, low, high, action); }
        else Faces(node, action);
    }

    //a 到 d 为固定对角线；两片三角形朝向局部正 Y，false 孔洞单元不生成面。
    private void Faces(Node node, Action<Vector3, Vector3, Vector3> action)
    {
        for (int z = node.z0; z < node.z1; z++) for (int x = node.x0; x < node.x1; x++)
        {
            if (!data.Surface(x, z)) continue;
            Vector3 a = matrix.MultiplyPoint3x4(data.Point(x, z)), b = matrix.MultiplyPoint3x4(data.Point(x + 1, z));
            Vector3 c = matrix.MultiplyPoint3x4(data.Point(x, z + 1)), d = matrix.MultiplyPoint3x4(data.Point(x + 1, z + 1));
            action(a, c, d); action(a, d, b);
        }
    }
    private static float BoxDistance(Vector3 point, Node node)
    {
        Vector3 closest = new(Mathf.Clamp(point.x, node.min.x, node.max.x), Mathf.Clamp(point.y, node.min.y, node.max.y), Mathf.Clamp(point.z, node.min.z, node.max.z));
        return (point - closest).sqrMagnitude;
    }
}
