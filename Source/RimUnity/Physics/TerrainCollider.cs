using System;

namespace RimUnity;

//地形碰撞查询真实高度场三角面，固定使用每格左下到右上的对角线。
public sealed class TerrainCollider : Collider
{
    public TerrainData terrainData;
    private ParticleTerrainHeightfield geometry;
    private TerrainData cachedData;
    private int cachedRevision, geometryOwner;
    internal override ParticlePhysicsShape Shape
    {
        get
        {
            if (terrainData == null) throw new InvalidOperationException("TerrainCollider 缺少 terrainData。");
            if (isTrigger) throw new NotSupportedException("TerrainCollider 高度表面不是封闭 Trigger 体积。");
            if (attachedRigidbody is { isKinematic: false }) throw new NotSupportedException("TerrainCollider 不能作为动态刚体的碰撞形状。");
            if (geometry == null || cachedData != terrainData || cachedRevision != terrainData.revision || geometryOwner != GetInstanceID())
            {
                geometry = new ParticleTerrainHeightfield(terrainData); cachedData = terrainData;
                cachedRevision = terrainData.revision; geometryOwner = GetInstanceID();
            }
            geometry.Transform(transform.localToWorldMatrix);
            return new ParticlePhysicsShape { kind = ParticlePhysicsShape.Kind.Terrain, terrain = geometry,
                center = (geometry.minimum + geometry.maximum) * .5f, extents = (geometry.maximum - geometry.minimum) * .5f };
        }
    }

    //直接编辑公开数组后显式重建；SetHeights/SetHoles 会自动更新版本。
    public void RefreshTerrain() => geometry = null;
}
