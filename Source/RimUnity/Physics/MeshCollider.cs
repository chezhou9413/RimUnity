using System;

namespace RimUnity;

//网格碰撞器缓存实际三角面；convex=true 时构建凸包供动态刚体接触使用。
public sealed class MeshCollider : Collider
{
    private Mesh source;
    private bool convexValue;
    private ParticleTriangleMesh geometry;
    private int geometryOwner;
    public Mesh sharedMesh { get => source; set { source = value; geometry = null; } }
    public bool convex { get => convexValue; set { if (convexValue != value) geometry = null; convexValue = value; } }

    internal override ParticlePhysicsShape Shape
    {
        get
        {
            if (source == null) throw new InvalidOperationException("MeshCollider 缺少 sharedMesh。");
            if (geometry == null || geometryOwner != GetInstanceID()) { geometry = new ParticleTriangleMesh(source, convex); geometryOwner = GetInstanceID(); }
            geometry.Transform(transform.localToWorldMatrix);
            return new ParticlePhysicsShape
            {
                kind = ParticlePhysicsShape.Kind.Mesh, mesh = geometry, center = (geometry.minimum + geometry.maximum) * .5f,
                extents = (geometry.maximum - geometry.minimum) * .5f
            };
        }
    }

    //脚本原地改写 Mesh 顶点后显式重建，避免长期持有过期三角形。
    public void RefreshMesh() => geometry = null;
}
