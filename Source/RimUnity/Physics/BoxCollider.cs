
namespace RimUnity;

//盒碰撞器保存自身旋转，不能用世界包围盒代替斜放的盒体。
public sealed class BoxCollider : Collider
{
    public Vector3 center, size = Vector3.one;
    internal override ParticlePhysicsShape Shape => new()
    {
        kind = ParticlePhysicsShape.Kind.Box, center = transform.TransformPoint(center), rotation = transform.rotation,
        extents = ParticlePhysicsShape.Abs(Vector3.Scale(size, transform.localToWorldMatrix.lossyScale)) * .5f
    };
}
