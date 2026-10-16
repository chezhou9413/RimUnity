using System.Collections.Generic;

namespace RimUnity;

//为几何描边提供边端点和角点选择数据，扩展网格随源数据更新并共享缓存。
public sealed partial class Mesh
{
    private Mesh edgeMesh;
    private int edgeVersion, expandedVersion = -1;

    //每个三角形展开三条边，每条边四个三角形，端点圆角由顶点阶段决定是否退化。
    internal Mesh TriangleEdges()
    {
        Flush();
        if (edgeMesh != null && expandedVersion == edgeVersion) return edgeMesh;
        edgeMesh ??= new Mesh { name = name + " (triangle edges)", indexFormat = IndexFormat.UInt32 };
        edgeMesh.Clear();
        var vertices = new List<Vector3>();
        var ends = new List<Vector4>();
        var thirds = new List<Vector4>();
        var indices = new List<int>[activeSubmeshes];
        for (int submesh = 0; submesh < activeSubmeshes; submesh++)
        {
            indices[submesh] = new();
            var source = submeshes[submesh];
            for (int triangle = 0; triangle < source.Count; triangle += 3)
                for (int edge = 0; edge < 3; edge++)
                {
                    Vector3 from = positions[source[triangle + edge]], to = positions[source[triangle + (edge + 1) % 3]], third = positions[source[triangle + (edge + 2) % 3]];
                    for (int slot = 0; slot < 12; slot++)
                    {
                        indices[submesh].Add(vertices.Count);
                        vertices.Add(from); ends.Add(new Vector4(to.x, to.y, to.z, slot)); thirds.Add(new Vector4(third.x, third.y, third.z, 0));
                    }
                }
        }
        edgeMesh.SetVertices(vertices); edgeMesh.SetUVs(1, ends); edgeMesh.SetUVs(2, thirds);
        for (int submesh = 0; submesh < activeSubmeshes; submesh++) edgeMesh.SetTriangles(indices[submesh], submesh, false);
        edgeMesh.bounds = meshBounds;
        expandedVersion = edgeVersion;
        return edgeMesh;
    }
}
