using System;

namespace RimUnity;

public enum ForceMode { Force = 0, Impulse = 1, VelocityChange = 2, Acceleration = 5 }
[Flags]
public enum RigidbodyConstraints
{
    None = 0, FreezePositionX = 2, FreezePositionY = 4, FreezePositionZ = 8,
    FreezeRotationX = 16, FreezeRotationY = 32, FreezeRotationZ = 64,
    FreezePosition = 14, FreezeRotation = 112, FreezeAll = 126
}

//刚体积分与粒子碰撞共享速度和惯量。
public class Rigidbody : MonoBehaviour
{
    public float mass = 1, drag, angularDrag = .05f, maxAngularVelocity = 7;
    public bool isKinematic, useGravity = true, detectCollisions = true;
    public Vector3 velocity, angularVelocity, centerOfMass, inertiaTensor = Vector3.one;
    public Quaternion inertiaTensorRotation = Quaternion.identity;
    public RigidbodyConstraints constraints;
    private readonly BridgeTime clock = new();
    private Vector3 accumulatedAcceleration, accumulatedAngularAcceleration, previousPosition;
    private Quaternion previousRotation;
    internal float physicsFrameDelta;
    public Vector3 position { get => transform.position; set => transform.position = value; }
    public Quaternion rotation { get => transform.rotation; set => transform.rotation = value; }
    public Vector3 worldCenterOfMass => transform.TransformPoint(centerOfMass);
    protected virtual Vector3 Gravity => new(0, -9.81f, 0);

    //启用时建立运动基线，不把重启对象的位置差误认为刚体速度。
    protected void OnEnable() { clock.Reset(); previousPosition = position; previousRotation = rotation; }
    protected void Update() => AdvanceFrame();

    //粒子协调器可先调用这一入口，后续原生 Update 不会重复积分。
    internal void AdvanceFrame()
    {
        PhysicsBodySimulation.AdvanceFrame();
    }

    //暂停也消费时钟基线，恢复播放时不会补算暂停期间的位移。
    internal void PreparePhysicsFrame()
    {
        float dt = clock.Step(false);
        physicsFrameDelta = !isActiveAndEnabled ? 0 : dt;
    }

    //显式模拟用于离线推进；每个输入时间只消耗一次累计的力。
    public void Simulate(float seconds) => IntegrateBody(seconds, true);
    internal void IntegrateBody(float seconds, bool clearForces)
    {
        if (seconds < 0 || !float.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds == 0) return;
        ValidateMass();
        if (isKinematic)
        {
            velocity = (position - previousPosition) / seconds;
            Quaternion delta = rotation * Quaternion.Inverse(previousRotation);
            float sign = delta.w < 0 ? -1 : 1;
            Vector3 vector = new Vector3(delta.x, delta.y, delta.z) * sign;
            float sine = vector.magnitude;
            angularVelocity = sine > 1e-7f ? vector / sine * (2 * Mathf.Atan2(sine, Math.Abs(delta.w)) / seconds) : Vector3.zero;
            if (this is Rigidbody2D { bodyType: RigidbodyType2D.Static }) velocity = angularVelocity = Vector3.zero;
        }
        else
        {
            int steps = Math.Max(1, checked((int)Math.Ceiling(seconds * 60)));
            float dt = seconds / steps;
            for (int i = 0; i < steps; i++)
            {
                velocity = Freeze((velocity + (accumulatedAcceleration + (useGravity ? Gravity : Vector3.zero)) * dt) / (1 + Mathf.Max(0, drag) * dt), false);
                angularVelocity = Freeze(Vector3.ClampMagnitude((angularVelocity + accumulatedAngularAcceleration * dt) / (1 + Mathf.Max(0, angularDrag) * dt), Mathf.Max(0, maxAngularVelocity)), true);
                Vector3 center = worldCenterOfMass + velocity * dt;
                float angularSpeed = angularVelocity.magnitude;
                if (angularSpeed > 0) rotation = Quaternion.AngleAxis(angularSpeed * dt * Mathf.Rad2Deg, angularVelocity / angularSpeed) * rotation;
                position += center - worldCenterOfMass;
            }
        }
        if (clearForces) ClearForces();
        previousPosition = position; previousRotation = rotation;
    }
    internal void ClearForces() => accumulatedAcceleration = accumulatedAngularAcceleration = Vector3.zero;

    //接触有效质量包含冻结轴和转动惯量，固定或暂停的物体不接受冲量。
    internal Vector3 InverseLinearMass(Vector3 impulse)
        => isKinematic || !isActiveAndEnabled || physicsFrameDelta <= 0 ? Vector3.zero : Freeze(impulse / mass, false);
    internal Vector3 InverseAngularMass(Vector3 impulse)
        => isKinematic || !isActiveAndEnabled || physicsFrameDelta <= 0 ? Vector3.zero : Freeze(InverseInertia(impulse), true);
    internal void ContactImpulse(Vector3 impulse, Vector3 point)
    {
        velocity += InverseLinearMass(impulse);
        angularVelocity += InverseAngularMass(Vector3.Cross(point - worldCenterOfMass, impulse));
    }

    //Force 与 Acceleration 累计到下一次积分，Impulse 与 VelocityChange 即时改变速度。
    public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
    {
        if (isKinematic) return;
        ValidateMass();
        switch (mode)
        {
            case ForceMode.Force: accumulatedAcceleration += Freeze(force / mass, false); break;
            case ForceMode.Acceleration: accumulatedAcceleration += Freeze(force, false); break;
            case ForceMode.Impulse: velocity += Freeze(force / mass, false); break;
            case ForceMode.VelocityChange: velocity += Freeze(force, false); break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    //转矩经过世界惯量的逆矩阵，冻结旋转轴按刚体局部坐标投影。
    public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
    {
        if (isKinematic) return;
        Vector3 change = Freeze(mode == ForceMode.Acceleration || mode == ForceMode.VelocityChange ? torque : InverseInertia(torque), true);
        switch (mode)
        {
            case ForceMode.Force: case ForceMode.Acceleration: accumulatedAngularAcceleration += change; break;
            case ForceMode.Impulse: case ForceMode.VelocityChange: angularVelocity += change; break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    //偏心作用点同时产生线速度和转矩。
    public void AddForceAtPosition(Vector3 force, Vector3 point, ForceMode mode = ForceMode.Force)
    {
        AddForce(force, mode);
        Vector3 torque = Vector3.Cross(point - worldCenterOfMass, force);
        if (mode == ForceMode.Acceleration) AddTorque(torque * mass, ForceMode.Force);
        else if (mode == ForceMode.VelocityChange) AddTorque(torque * mass, ForceMode.Impulse);
        else AddTorque(torque, mode);
    }
    public void AddRelativeForce(Vector3 force, ForceMode mode = ForceMode.Force) => AddForce(rotation * force, mode);
    public void AddRelativeTorque(Vector3 torque, ForceMode mode = ForceMode.Force) => AddTorque(rotation * torque, mode);
    public Vector3 GetPointVelocity(Vector3 point) => velocity + Vector3.Cross(angularVelocity, point - worldCenterOfMass);
    public void MovePosition(Vector3 point) => position = point;
    public void MoveRotation(Quaternion value) => rotation = value;

    //由 Unity 导出器提供烘焙后的主惯量与惯量旋转，不假装运行完整 PhysX 自动质量求解。
    private Vector3 InverseInertia(Vector3 torque)
    {
        if (inertiaTensor.x <= 0 || inertiaTensor.y <= 0 || inertiaTensor.z <= 0) throw new InvalidOperationException("刚体惯量必须为正数。");
        Quaternion axes = rotation * inertiaTensorRotation;
        Vector3 local = Quaternion.Inverse(axes) * torque;
        return axes * new Vector3(local.x / inertiaTensor.x, local.y / inertiaTensor.y, local.z / inertiaTensor.z);
    }

    //Unity 约束位值分别对应三个位移轴与三个旋转轴。
    private Vector3 Freeze(Vector3 value, bool angular)
    {
        if (angular) value = Quaternion.Inverse(rotation) * value;
        int offset = angular ? 4 : 1;
        for (int i = 0; i < 3; i++) if (((int)constraints & (1 << (offset + i))) != 0) value[i] = 0;
        return angular ? rotation * value : value;
    }
    private void ValidateMass()
    {
        if (mass <= 0 || !float.IsFinite(mass)) throw new InvalidOperationException("刚体质量必须为有限正数。");
    }
}
