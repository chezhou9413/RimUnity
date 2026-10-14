
namespace RimUnity;

//无限平面的正侧为可通行区域，局部法线随 Transform 旋转。
public sealed class PlaneCollider : Collider
{
    public Vector3 center, normal = Vector3.up;
    internal override ParticlePhysicsShape Shape => new()
    {
        kind = ParticlePhysicsShape.Kind.Plane, center = transform.TransformPoint(center),
        axis = transform.worldToLocalMatrix.transpose.MultiplyVector(normal).normalized
    };
}
