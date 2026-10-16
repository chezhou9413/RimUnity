using System;
using System.Collections.Generic;

namespace RimUnity;

//粒子按排序生成动态网格；数值流按导出的列表顺序紧密打包。
public sealed partial class ParticleSystemRenderer : MonoBehaviour
{
    public Material sharedMaterial, trailMaterial;
    public ParticleSystemRenderMode renderMode;
    public ParticleSystemRenderSpace alignment;
    public ParticleSystemSortMode sortMode;
    public float lengthScale = 2, velocityScale, normalDirection = 1;
    public Vector3 pivot, flip;
    public bool allowRoll = true;
    //最近一次世界网格提交，区分模拟和绘制问题。
    private Mesh batch, trailBatch;
    private readonly List<Vector3> vertices = new();
    private readonly List<Vector2> uv = new();
    private readonly List<Vector4> packedUV = new();
    private readonly List<Color32> colors = new();
    private readonly List<Vector4> custom1 = new(), custom2 = new();
    private readonly List<int> indices = new();
    private readonly List<int> order = new();
    private Comparison<int> particleComparison;
    private ParticleSystem sortingSystem;
    private Vector3 sortingPosition, sortingForward;
    private readonly List<ParticleSystemVertexStream> streams = new();
    private float particleRandom;
    private Mesh sourceMesh;
    private Vector3[] sourcePositions;
    private Vector2[] sourceUV;
    private int[] sourceTriangles;
    private readonly List<Material> ownedMaterials = new();
    //实例参数不能污染其他粒子系统共用的模板材质。
    public Material material
    {
        get
        {
            Material source = sharedMaterial;
            if (source == null || ownedMaterials.Contains(source)) return source;
            var clone = new Material(source);
            sharedMaterial = clone;
            ownedMaterials.Add(clone);
            return clone;
        }
        set => sharedMaterial = value;
    }
    public void GetActiveVertexStreams(List<ParticleSystemVertexStream> target) { target.Clear(); target.AddRange(streams); }
    public void SetActiveVertexStreams(List<ParticleSystemVertexStream> value) => vertexStreams = value.ToArray();

    //依照 Unity 的列表顺序将数值流紧密排列到 TEXCOORD。
    public ParticleSystemVertexStream[] vertexStreams
    {
        get => streams.ToArray();
        set
        {
            int width = 0;
            foreach (var stream in value) width += StreamWidth(stream);
            if (width > 12) throw new NotSupportedException("粒子数值流超过 TEXCOORD0～2 的容量。");
            streams.Clear(); streams.AddRange(value);
        }
    }

    //网格缓存随组件销毁，不持有已卸载实例。
    internal void ReleaseGeometry() { if (batch != null) Destroy(batch); if (trailBatch != null) Destroy(trailBatch); batch = trailBatch = null; ReleaseSpriteBatches(); meshGeometry.Clear(); foreach (var material in uiMaterials.Values) Destroy(material); uiMaterials.Clear(); }
    private void OnDestroy()
    {
        ReleaseGeometry();
        foreach (Material owned in ownedMaterials) Destroy(owned);
        ownedMaterials.Clear();
    }
    internal void Draw(ParticleSystem system, Camera camera)
    {
        if (!enabled || !gameObject.activeInHierarchy) return;
        preparedSpriteTextures.Clear();
        if (sharedMaterial != null && renderMode != ParticleSystemRenderMode.None && system.count != 0)
        {
            ClearMesh(); order.Clear();
            for (int i = 0; i < system.count; i++) order.Add(i);
            if (sortMode != ParticleSystemSortMode.None)
            {
                sortingSystem = system;
                sortingPosition = camera != null ? camera.transform.position : Vector3.zero;
                sortingForward = camera != null ? camera.transform.forward : Vector3.forward;
                try { order.Sort(particleComparison ??= CompareParticles); }
                finally { sortingSystem = null; }
            }
            DrawParticleBatches(system, camera);
        }
        if (system.trails.enabled && trailMaterial != null) DrawTrails(system, camera);
        TrimSpriteBatches();
    }
    //排序期间相机姿态不变，比较器不重复读层级变换或创建闭包。
    private int CompareParticles(int a, int b)
    {
        var s = sortingSystem;
        var x = s.particles[a]; var y = s.particles[b];
        return sortMode switch
        {
            ParticleSystemSortMode.OldestInFront => x.elapsedAge.CompareTo(y.elapsedAge),
            ParticleSystemSortMode.YoungestInFront => y.elapsedAge.CompareTo(x.elapsedAge),
            ParticleSystemSortMode.Depth => Vector3.Dot(s.WorldPosition(y) - s.WorldPosition(x), sortingForward).CompareTo(0),
            _ => (s.WorldPosition(y) - sortingPosition).sqrMagnitude.CompareTo((s.WorldPosition(x) - sortingPosition).sqrMagnitude)
        };
    }
    private void AppendParticle(ParticleSystem s, ParticleSystem.Particle p, Camera camera)
    {
        particleRandom = ParticleSystem.Random(p.randomSeed, 97);
        Vector3 center = s.WorldPosition(p), size = Vector3.Scale(s.SizeAt(p), s.ParticleScale(p));
        Color32 color = s.ColorAt(p);
        Vector4 c1 = s.CustomAt(p, 0), c2 = s.CustomAt(p, 1);
        streamParticle = p; streamSize = size; streamVelocity = s.WorldVelocity(p); streamCenter = center;
        Rect rect = UV(s, p);
        if (ParticleSystem.Random(p.randomSeed, 80) < flip.x) { rect.x += rect.width; rect.width = -rect.width; }
        if (ParticleSystem.Random(p.randomSeed, 81) < flip.y) { rect.y += rect.height; rect.height = -rect.height; }
        if (renderMode == ParticleSystemRenderMode.Mesh)
        {
            SelectMesh(p);
            int offset = vertices.Count;
            Matrix4x4 matrix = Matrix4x4.TRS(center, s.ParticleRotation(p) * Quaternion.Euler(p.rotation3D), size);
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i < sourcePositions.Length; i++)
            {
                streamNormal = i < sourceNormals.Length ? normalMatrix.MultiplyVector(sourceNormals[i]).normalized : Vector3.forward;
                streamTangent = i < sourceTangents.Length ? (Vector4)matrix.MultiplyVector((Vector3)sourceTangents[i]).normalized : new Vector4(1, 0, 0, 1);
                if (i < sourceTangents.Length) streamTangent.w = sourceTangents[i].w;
                Vector2 source = i < sourceUV.Length ? sourceUV[i] : Vector2.zero;
                streamVertexId = i; streamUV3 = i < sourceUV3.Length ? sourceUV3[i] : Vector2.zero; streamUV4 = i < sourceUV4.Length ? sourceUV4[i] : Vector2.zero;
                Vector3 local = sourcePositions[i] + pivot;
                if (ParticleSystem.Random(p.randomSeed, 82) < flip.z) local.z = -local.z;
                Vertex(matrix.MultiplyPoint3x4(local), new Vector2(rect.x + source.x * rect.width, rect.y + source.y * rect.height), color, c1, c2);
            }
            foreach (int index in sourceTriangles) indices.Add(offset + index);
            return;
        }
        Quaternion view = BillboardAlignment(s, p, camera);
        Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;
        Vector3 right = view * Vector3.right, up = view * Vector3.up;
        if (renderMode == ParticleSystemRenderMode.HorizontalBillboard) { right = Vector3.right; up = Vector3.forward; }
        if (renderMode == ParticleSystemRenderMode.VerticalBillboard) { up = Vector3.up; right = Vector3.Cross(up, forward).normalized; }
        if (renderMode == ParticleSystemRenderMode.Stretch)
        {
            Vector3 velocity = s.WorldVelocity(p);
            up = velocity.sqrMagnitude > .000001f ? velocity.normalized : up;
            right = Vector3.Cross(up, forward).normalized;
            size.y = Mathf.Abs(size.y) * lengthScale + velocity.magnitude * velocityScale;
        }
        float angle = p.rotation * Mathf.Deg2Rad, co = Mathf.Cos(angle), si = Mathf.Sin(angle);
        Vector3 x = right * co - up * si, y = up * co + right * si;
        if (p.alignedToDirection)
        {
            Quaternion rotation = s.ParticleRotation(p) * Quaternion.Euler(p.rotation3D);
            x = rotation * Vector3.right; y = rotation * Vector3.up;
        }
        else if (s.main.startRotation3D && renderMode == ParticleSystemRenderMode.Billboard)
        {
            Quaternion rotation = view * Quaternion.Euler(p.rotation3D.x, p.rotation3D.y, -p.rotation3D.z);
            x = rotation * Vector3.right; y = rotation * Vector3.up;
        }
        x *= size.x * .5f; y *= size.y * .5f;
        streamNormal = -Vector3.Cross(x, y).normalized;
        streamTangent = new Vector4(x.normalized.x, x.normalized.y, x.normalized.z, 1);
        center += x * (pivot.x * 2) + y * (pivot.y * 2);
        int start = vertices.Count;
        streamVertexId = 0; streamUV3 = streamUV4 = Vector2.zero;
        Vertex(center - x - y, new Vector2(rect.xMin, rect.yMin), color, c1, c2);
        streamVertexId = 1;
        Vertex(center - x + y, new Vector2(rect.xMin, rect.yMax), color, c1, c2);
        streamVertexId = 2;
        Vertex(center + x + y, new Vector2(rect.xMax, rect.yMax), color, c1, c2);
        streamVertexId = 3;
        Vertex(center + x - y, new Vector2(rect.xMax, rect.yMin), color, c1, c2);
        Quad(start);
    }
    private void ClearMesh() { vertices.Clear(); uv.Clear(); packedUV.Clear(); colors.Clear(); custom1.Clear(); custom2.Clear(); indices.Clear(); normals.Clear(); tangents.Clear(); particleRandom = 0; streamNormal = Vector3.forward; streamTangent = new Vector4(1, 0, 0, 1); }
    private void Vertex(Vector3 p, Vector2 t, Color32 c, Vector4 c1 = default, Vector4 c2 = default)
    {
        vertices.Add(p); uv.Add(t);
        PackStreams(t, c1, c2);
        colors.Add(c); normals.Add(streamNormal); tangents.Add(streamTangent);
    }
    private void Quad(int start) { indices.Add(start); indices.Add(start + 1); indices.Add(start + 2); indices.Add(start); indices.Add(start + 2); indices.Add(start + 3); }
    private void Submit(ref Mesh target, Material mat, Camera camera)
    {
        if (indices.Count == 0) return;
        if (uiEntry != null) { CaptureDiagnostics(mat, null); SubmitUI(mat); return; }
        if (cameraUiEntry != null)
        {
            for (int i = 0; i < colors.Count; i++) { Color color = colors[i]; color.a *= cameraUiEntry.Alpha; colors[i] = color; }
            UI.Runtime.UiMaskData.Apply(mat);
        }
        if (cameraUiEntry == null && ParticleDrawBatch.TryAppend(mat, gameObject.layer, propertyBlock,
            vertices, normals, tangents, colors, packedUV, custom1, custom2, indices)) return;
        if (target == null)
        {
            target = new Mesh { name = "RimUnity Particle Batch", indexFormat = IndexFormat.UInt32 };
            target.MarkDynamic();
        }
        target.SetVertices(vertices); target.SetColors(colors); target.SetUVs(0, packedUV); target.SetUVs(1, custom1); target.SetUVs(2, custom2); target.SetTriangles(indices, 0);
        target.SetNormals(normals); target.SetTangents(tangents);
        CaptureDiagnostics(mat, target);
        RimUnity.Graphics.DrawMesh(target, Matrix4x4.identity, mat, cameraUiEntry?.RenderLayer ?? gameObject.layer, 0, propertyBlock);
    }
}
