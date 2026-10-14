using System;
using System.Collections.Generic;

namespace RimUnity;

//形状资源只在配置变化时读取，出生采样复用顶点和累计权重。
public sealed partial class ParticleSystem
{
    private ShapeGeometry geometry;
    private Vector2 shapeUv;

    //网格使用顶点法线和颜色，精灵使用原始紧密网格而非外接矩形。
    private void SampleGeometry(float seconds, int index, int count, out Vector3 position, out Vector3 direction, out Color color)
    {
        ShapeModule source = ResolveGeometry(out Transform rendererTransform);
        if (geometry == null || !geometry.Matches(source)) geometry = new ShapeGeometry(source);
        float selection = ShapePhase(shape.meshSpawnMode, shape.meshSpawnSpeed, shape.meshSpawnSpread, seconds, index, count, 1, 1);
        geometry.Sample(selection, Random01(), Random01(), out position, out direction, out color, out shapeUv);
        position += direction * shape.normalOffset;
        if (rendererTransform != null)
        {
            position = transform.InverseTransformPoint(rendererTransform.TransformPoint(position));
            direction = transform.InverseTransformDirection(rendererTransform.TransformDirection(direction));
        }
        if (!shape.useMeshColors) color = Color.white;
    }

    //缓存一份资源几何；精灵的边模式仅使用外轮廓，避免内部三角剖分线发射。
    private sealed class ShapeGeometry
    {
        private readonly Mesh mesh;
        private readonly Vector3[] spriteVertices;
        private readonly int[] spriteTriangles;
        private readonly Vector3 scale;
        private readonly ParticleSystemShapeType type;
        private readonly ParticleSystemMeshShapeType mode;
        private readonly bool singleMaterial;
        private readonly int materialIndex;
        private readonly Vector3[] positions, normals;
        private readonly Color[] colors;
        private readonly Vector2[] uv;
        private readonly int uvChannel;
        private readonly List<int> elements = new();
        private readonly List<float> weights = new();
        private readonly float total;

        //单材质选择先约束三角形，再构造采样分布。
        internal ShapeGeometry(ShapeModule module)
        {
            mesh = module.mesh; spriteVertices = module.spriteVertices; spriteTriangles = module.spriteTriangles;
            type = module.shapeType; mode = module.meshShapeType; scale = module.scale; uvChannel = module.textureUVChannel;
            singleMaterial = module.useMeshMaterialIndex; materialIndex = module.meshMaterialIndex;
            int[] triangles;
            if (type == ParticleSystemShapeType.Mesh)
            {
                if (mesh == null) throw new InvalidOperationException("Mesh 发射形状缺少网格。");
                positions = mesh.vertices; normals = mesh.normals; colors = mesh.colors;
                var coordinates = new List<Vector2>(); mesh.GetUVs(uvChannel, coordinates); uv = coordinates.ToArray();
                if (singleMaterial)
                {
                    var selected = new List<int>(); mesh.GetTriangles(selected, materialIndex); triangles = selected.ToArray();
                }
                else triangles = mesh.triangles;
            }
            else
            {
                positions = spriteVertices; triangles = spriteTriangles;
                normals = new Vector3[positions.Length]; colors = Array.Empty<Color>();
                uv = module.sprite != null ? UI.SpriteGeometry.Get(module.sprite).UV : Array.Empty<Vector2>();
                Array.Fill(normals, Vector3.back);
            }
            if (positions.Length == 0 || triangles.Length % 3 != 0)
                throw new InvalidOperationException("粒子形状几何为空或三角形索引不完整。");
            foreach (int index in triangles)
                if (index < 0 || index >= positions.Length) throw new InvalidOperationException("粒子形状索引越界。");
            if (normals.Length != positions.Length)
            {
                normals = new Vector3[positions.Length];
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                    normals[a] += normal; normals[b] += normal; normals[c] += normal;
                }
                for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
            }
            if (mode == ParticleSystemMeshShapeType.Vertex)
            {
                var used = new HashSet<int>(triangles);
                for (int i = 0; i < positions.Length; i++)
                    if (!singleMaterial || used.Contains(i)) elements.Add(i);
                total = elements.Count;
            }
            else if (mode == ParticleSystemMeshShapeType.Triangle)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    float area = Vector3.Cross(Scaled(b) - Scaled(a), Scaled(c) - Scaled(a)).magnitude * .5f;
                    if (area == 0) continue;
                    elements.Add(a); elements.Add(b); elements.Add(c); total += area; weights.Add(total);
                }
            }
            else if (mode == ParticleSystemMeshShapeType.Edge)
            {
                var edges = new Dictionary<(int, int), int>();
                for (int i = 0; i < triangles.Length; i += 3)
                    for (int j = 0; j < 3; j++)
                    {
                        int a = triangles[i + j], b = triangles[i + (j + 1) % 3];
                        var edge = a < b ? (a, b) : (b, a);
                        edges.TryGetValue(edge, out int count); edges[edge] = count + 1;
                    }
                foreach (var edge in edges)
                {
                    if (type == ParticleSystemShapeType.Sprite && edge.Value != 1) continue;
                    int a = edge.Key.Item1, b = edge.Key.Item2;
                    float length = Vector3.Distance(Scaled(a), Scaled(b));
                    if (length == 0) continue;
                    elements.Add(a); elements.Add(b); total += length; weights.Add(total);
                }
            }
            else throw new NotSupportedException("粒子网格采样模式不支持：" + mode);
            if (total <= 0) throw new InvalidOperationException("粒子形状没有可采样的顶点、边或表面。");
        }

        //形状变换只改变采样权重，位置仍由外层统一变换。
        private Vector3 Scaled(int index) => Vector3.Scale(positions[index], scale);

        //原资源替换或单材质、缩放、模式变化时重建分布。
        internal bool Matches(ShapeModule module) => mesh == module.mesh && spriteVertices == module.spriteVertices &&
            spriteTriangles == module.spriteTriangles && type == module.shapeType && mode == module.meshShapeType &&
            scale == module.scale && singleMaterial == module.useMeshMaterialIndex && materialIndex == module.meshMaterialIndex && uvChannel == module.textureUVChannel;

        //三角形用平方根重心坐标均匀采样，边按长度选取。
        internal void Sample(float selection, float u, float v, out Vector3 position, out Vector3 normal, out Color color, out Vector2 coordinates)
        {
            int selected;
            if (mode == ParticleSystemMeshShapeType.Vertex) selected = Math.Min(elements.Count - 1, (int)(selection * elements.Count));
            else
            {
                float sample = selection * total;
                int low = 0, high = weights.Count - 1;
                while (low < high) { int mid = (low + high) / 2; if (sample < weights[mid]) high = mid; else low = mid + 1; }
                selected = low;
            }
            int stride = mode == ParticleSystemMeshShapeType.Triangle ? 3 : mode == ParticleSystemMeshShapeType.Edge ? 2 : 1;
            int a = elements[selected * stride], b = stride > 1 ? elements[selected * stride + 1] : a;
            int c = stride > 2 ? elements[selected * stride + 2] : a;
            float root = Mathf.Sqrt(u);
            Vector3 weight = stride == 3 ? new Vector3(1 - root, root * (1 - v), root * v) :
                stride == 2 ? new Vector3(1 - u, u, 0) : new Vector3(1, 0, 0);
            position = positions[a] * weight.x + positions[b] * weight.y + positions[c] * weight.z;
            normal = (normals[a] * weight.x + normals[b] * weight.y + normals[c] * weight.z).normalized;
            color = colors.Length == positions.Length ? colors[a] * weight.x + colors[b] * weight.y + colors[c] * weight.z : Color.white;
            coordinates = uv.Length == positions.Length ? uv[a] * weight.x + uv[b] * weight.y + uv[c] * weight.z : Vector2.zero;
        }
    }
}
