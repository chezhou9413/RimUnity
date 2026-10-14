
namespace RimUnity;

//球半径使用最大缩放轴，匹配 Unity 基元而非将球隐式变为椭球。
public sealed class SphereCollider : Collider
{
    public Vector3 center;
    public float radius = .5f;
    internal override ParticlePhysicsShape Shape => new()
    {
        kind = ParticlePhysicsShape.Kind.Sphere, center = transform.TransformPoint(center),
        radius = Mathf.Max(0, radius) * ParticlePhysicsShape.Max(ParticlePhysicsShape.Abs(transform.localToWorldMatrix.lossyScale))
    };
}
