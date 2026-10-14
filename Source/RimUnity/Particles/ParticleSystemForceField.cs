using System;
using System.Collections.Generic;

namespace RimUnity;

public enum ParticleSystemForceFieldShape { Sphere, Hemisphere, Cylinder, Box }

//力场以自身坐标定义方向、引力和涡旋，粒子系统在积分时查询活动集合。
public sealed class ParticleSystemForceField : MonoBehaviour
{
    internal static readonly HashSet<ParticleSystemForceField> Active = new();
    public ParticleSystemForceFieldShape shape;
    public float startRange, endRange = 1, length = 1, gravityFocus;
    public Vector2 rotationRandomness;
    public bool multiplyDragByParticleSize, multiplyDragByParticleVelocity;
    public ParticleSystem.MinMaxCurve directionX = 0, directionY = 0, directionZ = 0, gravity = 0;
    public ParticleSystem.MinMaxCurve rotationSpeed = 0, rotationAttraction = 0, drag = 0;
    public Texture3D vectorField;
    public ParticleSystem.MinMaxCurve vectorFieldSpeed = 1, vectorFieldAttraction = 1;

    //禁用组件后不再影响已有粒子，引用列表保留原对象。
    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
    private void OnDestroy() => Active.Remove(this);

    //范围按场体局部坐标计算，旋转方向和非等比缩放由 Transform 处理。
    internal Vector3 Acceleration(Vector3 position, Vector3 velocity, float size, uint seed)
    {
        Vector3 local = transform.InverseTransformPoint(position);
        float distance = shape switch
        {
            ParticleSystemForceFieldShape.Box => Math.Max(Math.Abs(local.x), Math.Max(Math.Abs(local.y), Math.Abs(local.z))),
            ParticleSystemForceFieldShape.Cylinder => new Vector2(local.x, local.z).magnitude,
            _ => local.magnitude
        };
        if (distance > endRange || endRange <= 0 || shape == ParticleSystemForceFieldShape.Hemisphere && local.y < 0
            || shape == ParticleSystemForceFieldShape.Cylinder && Math.Abs(local.y) > length * .5f) return Vector3.zero;
        float phase = Mathf.InverseLerp(startRange, endRange, distance), random = ParticleSystem.Random(seed, 211);
        Vector3 force = new(directionX.Evaluate(phase, random), directionY.Evaluate(phase, random), directionZ.Evaluate(phase, random));
        Vector3 radial = shape == ParticleSystemForceFieldShape.Cylinder ? new Vector3(local.x, 0, local.z) : local;
        float target = endRange * gravityFocus;
        force -= radial.normalized * (gravity.Evaluate(phase, random) * Mathf.Sign(distance - target));
        Vector3 axis = new(Mathf.Lerp(-rotationRandomness.x, rotationRandomness.x, random), 1,
            Mathf.Lerp(-rotationRandomness.y, rotationRandomness.y, ParticleSystem.Random(seed, 212)));
        Vector3 tangent = Vector3.Cross(axis.normalized, radial).normalized;
        force += tangent * rotationSpeed.Evaluate(phase, random) - radial.normalized * rotationAttraction.Evaluate(phase, random);
        float resistance = Math.Max(0, drag.Evaluate(phase, random));
        if (multiplyDragByParticleSize) resistance *= size;
        if (multiplyDragByParticleVelocity) resistance *= velocity.magnitude;
        Vector3 worldForce = transform.TransformDirection(force) - velocity * resistance;
        if (vectorField != null)
        {
            Vector3 uvw = local / (2 * endRange) + Vector3.one * .5f;
            Vector3 targetVelocity = transform.TransformDirection(ParticleVectorField.Sample(vectorField, uvw)) * vectorFieldSpeed.Evaluate(phase, random);
            worldForce += (targetVelocity - velocity) * vectorFieldAttraction.Evaluate(phase, random);
        }
        return worldForce;
    }
}
