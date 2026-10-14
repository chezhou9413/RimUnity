
namespace RimUnity;

//圆形碰撞体按 XY 最大缩放求半径。
public sealed class CircleCollider2D : Collider2D
{
    public float radius = .5f;
    internal override ParticlePlanarShape PlanarShape => new()
    {
        kind = ParticlePlanarShape.Kind.Circle, center = WorldPoint(Vector2.zero), radius = Mathf.Max(0, radius) * Mathf.Max(WorldScale.x, WorldScale.y)
    };
}
