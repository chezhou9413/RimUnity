
namespace RimUnity;

public enum CapsuleDirection2D { Vertical, Horizontal }

//胶囊的长轴与两端半圆由 size 和 direction 共同确定。
public sealed class CapsuleCollider2D : Collider2D
{
    public Vector2 size = new(1, 2);
    public CapsuleDirection2D direction;
    internal override ParticlePlanarShape PlanarShape
    {
        get
        {
            Vector2 scale = WorldScale;
            float width = Mathf.Abs(size.x * scale.x), height = Mathf.Abs(size.y * scale.y);
            bool vertical = direction == CapsuleDirection2D.Vertical;
            float radius = (vertical ? width : height) * .5f;
            return new ParticlePlanarShape
            {
                kind = ParticlePlanarShape.Kind.Capsule, center = WorldPoint(Vector2.zero), radius = radius,
                halfSegment = Mathf.Max(0, (vertical ? height : width) * .5f - radius),
                axis = ParticlePlanarShape.XY(transform.TransformDirection(vertical ? Vector3.up : Vector3.right)).normalized
            };
        }
    }
}
