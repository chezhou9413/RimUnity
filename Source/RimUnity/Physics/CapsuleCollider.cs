using System;

namespace RimUnity;

//胶囊轴由 direction 指定，长度和半径分别使用轴向与横向缩放。
public sealed class CapsuleCollider : Collider
{
    public Vector3 center;
    public float radius = .5f, height = 2;
    public int direction = 1;
    internal override ParticlePhysicsShape Shape
    {
        get
        {
            if (direction < 0 || direction > 2) throw new InvalidOperationException("胶囊 direction 必须为 0、1 或 2。");
            Vector3 scale = ParticlePhysicsShape.Abs(transform.localToWorldMatrix.lossyScale), axis = Vector3.zero;
            axis[direction] = 1;
            float r = Mathf.Max(0, radius) * Mathf.Max(scale[(direction + 1) % 3], scale[(direction + 2) % 3]);
            return new ParticlePhysicsShape
            {
                kind = ParticlePhysicsShape.Kind.Capsule, center = transform.TransformPoint(center),
                axis = transform.rotation * axis, radius = r, halfSegment = Mathf.Max(0, height * scale[direction] * .5f - r)
            };
        }
    }
}
