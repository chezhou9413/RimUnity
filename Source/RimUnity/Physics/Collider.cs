
namespace RimUnity;

//自有碰撞器复用粒子和简化刚体的几何查询。
public abstract class Collider : MonoBehaviour
{
    public bool isTrigger { get; set; }
    internal abstract ParticlePhysicsShape Shape { get; }
    internal virtual bool Is2D => false;
    public PhysicMaterial sharedMaterial;
    public PhysicMaterial material { get => sharedMaterial ??= new PhysicMaterial(); set => sharedMaterial = value; }
    public Rigidbody attachedRigidbody
    {
        get
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (current.GetComponent<Rigidbody>() is { } body) return body;
            return null;
        }
    }

    //内部点原样返回，表面外的点投影到最近表面。
    public Vector3 ClosestPoint(Vector3 position)
    {
        float distance = Shape.Distance(position, out Vector3 normal);
        return distance > 0 ? position - normal * distance : position;
    }

    //射线距离由单位方向计算，避免调用方方向长度改变结果。
    public virtual bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance)
    {
        hitInfo = default;
        if (!enabled || !gameObject.activeInHierarchy || maxDistance < 0 || ray.direction.sqrMagnitude == 0) return false;
        ParticlePhysicsShape shape = Shape;
        if (float.IsPositiveInfinity(maxDistance))
        {
            if (shape.kind == ParticlePhysicsShape.Kind.Plane)
            {
                float approach = Vector3.Dot(ray.direction.normalized, shape.axis);
                if (approach >= 0) return false;
                maxDistance = Mathf.Max(0, -shape.Distance(ray.origin, out _) / approach) + 1;
            }
            else maxDistance = Vector3.Distance(ray.origin, shape.center) + shape.extents.magnitude + shape.radius + shape.halfSegment + 1;
        }
        Vector3 delta = ray.direction.normalized * maxDistance;
        if (!Shape.Sweep(ray.origin, delta, 0, out float fraction, out Vector3 normal)) return false;
        hitInfo = new RaycastHit { collider = this, point = ray.origin + delta * fraction, normal = normal, distance = fraction * maxDistance };
        return true;
    }
}
