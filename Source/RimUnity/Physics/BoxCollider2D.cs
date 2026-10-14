
namespace RimUnity;

//矩形通过四条真实边界参与 2D 碰撞，支持圆角边半径。
public sealed class BoxCollider2D : Collider2D
{
    public Vector2 size = Vector2.one;
    public float edgeRadius;
    internal override ParticlePlanarShape PlanarShape => new()
    {
        kind = ParticlePlanarShape.Kind.Polygon, radius = Mathf.Max(0, edgeRadius) * Mathf.Max(WorldScale.x, WorldScale.y),
        paths = new[] { new[] { WorldPoint(new Vector2(-size.x, -size.y) * .5f), WorldPoint(new Vector2(size.x, -size.y) * .5f),
            WorldPoint(new Vector2(size.x, size.y) * .5f), WorldPoint(new Vector2(-size.x, size.y) * .5f) } }
    };
}
