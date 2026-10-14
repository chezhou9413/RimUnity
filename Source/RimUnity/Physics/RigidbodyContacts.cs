using System;

namespace RimUnity;

//顺序冲量求解基本刚体接触，包含法向恢复、静摩擦、动摩擦及穿透位置修正。
internal static class RigidbodyContacts
{
    private static readonly PhysicMaterial defaultMaterial = new();
    private static readonly PhysicMaterial defaultMaterial2D = new PhysicsMaterial2D();
    internal static void Solve(Collider[] colliders)
    {
        for (int iteration = 0; iteration < 6; iteration++)
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider a = colliders[i];
                if (!ParticlePhysicsWorld.Queryable(a) || a.isTrigger) continue;
                Rigidbody bodyA = a.attachedRigidbody;
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    Collider b = colliders[j];
                    if (!ParticlePhysicsWorld.Queryable(b) || b.isTrigger || a.Is2D != b.Is2D) continue;
                    Rigidbody bodyB = b.attachedRigidbody;
                    if (bodyA == bodyB || !Dynamic(bodyA) && !Dynamic(bodyB)) continue;
                    ValidateMesh(a, bodyA); ValidateMesh(b, bodyB);
                    if (!PhysicsContactGeometry.Contact(a.Shape, b.Shape, out Vector3 normal, out Vector3 point, out float depth)) continue;
                    PhysicMaterial fallback = a.Is2D ? defaultMaterial2D : defaultMaterial;
                    Resolve(bodyA, bodyB, a.sharedMaterial ?? fallback, b.sharedMaterial ?? fallback, normal, point, depth, a.Is2D);
                }
            }
    }

    private static void Resolve(Rigidbody a, Rigidbody b, PhysicMaterial ma, PhysicMaterial mb, Vector3 normal, Vector3 point, float depth, bool planar)
    {
        Vector3 inverseA = a?.InverseLinearMass(normal) ?? Vector3.zero, inverseB = b?.InverseLinearMass(normal) ?? Vector3.zero;
        float inverseTranslation = Vector3.Dot(inverseA + inverseB, normal);
        if (inverseTranslation > 0 && depth > .0001f)
        {
            float correction = (depth - .0001f) * .65f / inverseTranslation;
            if (Dynamic(a)) a.position += inverseA * correction;
            if (Dynamic(b)) b.position -= inverseB * correction;
        }
        Vector3 relative = Velocity(a, point) - Velocity(b, point);
        float speed = Vector3.Dot(relative, normal), effective = EffectiveMass(a, b, point, normal);
        if (speed >= 0 || effective <= 1e-12f) return;
        float bounce = Mathf.Clamp01(planar ? Math.Max(ma.bounciness, mb.bounciness) : PhysicMaterial.Combine(ma.bounciness, mb.bounciness, ma.bounceCombine, mb.bounceCombine));
        //低速接触不反复回弹，避免重力下的静止物体抖动。
        if (-speed < .5f) bounce = 0;
        float impulse = -(1 + bounce) * speed / effective;
        Apply(a, b, point, normal * impulse);
        relative = Velocity(a, point) - Velocity(b, point);
        Vector3 tangent = relative - normal * Vector3.Dot(relative, normal);
        if (tangent.sqrMagnitude < 1e-12f) return;
        tangent.Normalize();
        float tangentMass = EffectiveMass(a, b, point, tangent);
        if (tangentMass <= 1e-12f) return;
        float frictionImpulse = -Vector3.Dot(relative, tangent) / tangentMass;
        float staticFriction = Math.Max(0, PhysicMaterial.Combine(ma.staticFriction, mb.staticFriction, ma.frictionCombine, mb.frictionCombine));
        float dynamicFriction = Math.Max(0, PhysicMaterial.Combine(ma.dynamicFriction, mb.dynamicFriction, ma.frictionCombine, mb.frictionCombine));
        if (planar) staticFriction = dynamicFriction = Mathf.Sqrt(Math.Max(0, ma.dynamicFriction * mb.dynamicFriction));
        if (Math.Abs(frictionImpulse) > staticFriction * impulse) frictionImpulse = -Mathf.Sign(Vector3.Dot(relative, tangent)) * dynamicFriction * impulse;
        Apply(a, b, point, tangent * frictionImpulse);
    }

    //冲量的接触点速度变化由平动与转动两部分共同决定。
    private static float EffectiveMass(Rigidbody a, Rigidbody b, Vector3 point, Vector3 axis)
    {
        return Contribution(a) + Contribution(b);
        float Contribution(Rigidbody body)
        {
            if (!Dynamic(body)) return 0;
            Vector3 radius = point - body.worldCenterOfMass;
            return Vector3.Dot(axis, body.InverseLinearMass(axis) + Vector3.Cross(body.InverseAngularMass(Vector3.Cross(radius, axis)), radius));
        }
    }
    private static Vector3 Velocity(Rigidbody body, Vector3 point) => body?.GetPointVelocity(point) ?? Vector3.zero;
    private static bool Dynamic(Rigidbody body) => body != null && !body.isKinematic && body.isActiveAndEnabled && body.physicsFrameDelta > 0;
    private static void Apply(Rigidbody a, Rigidbody b, Vector3 point, Vector3 impulse)
    {
        if (Dynamic(a)) a.ContactImpulse(impulse, point);
        if (Dynamic(b)) b.ContactImpulse(-impulse, point);
    }
    private static void ValidateMesh(Collider collider, Rigidbody body)
    {
        if (Dynamic(body) && collider is MeshCollider { convex: false })
            throw new NotSupportedException("动态非凸 MeshCollider 尚需凸体分解；请提供凸网格或静态网格。");
    }
}
