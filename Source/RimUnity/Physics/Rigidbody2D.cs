using System;

namespace RimUnity;

public enum ForceMode2D { Force, Impulse }
public enum RigidbodyType2D { Dynamic, Kinematic, Static }
[Flags]
public enum RigidbodyConstraints2D { None = 0, FreezePositionX = 1, FreezePositionY = 2, FreezeRotation = 4, FreezePosition = 3, FreezeAll = 7 }

//2D 刚体复用接触积分，但固定 Z 位移与 X/Y 旋转，并保留 2D API 的角度单位。
public sealed class Rigidbody2D : Rigidbody
{
    private RigidbodyType2D kind;
    public float gravityScale = 1;
    public Rigidbody2D() { base.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY; maxAngularVelocity = float.MaxValue; }
    protected override Vector3 Gravity => new(0, -9.81f * gravityScale, 0);
    public RigidbodyType2D bodyType { get => kind; set { kind = value; isKinematic = value != RigidbodyType2D.Dynamic; } }
    public bool simulated { get => enabled; set { enabled = value; detectCollisions = value; } }
    public new Vector2 velocity { get => (Vector2)base.velocity; set => base.velocity = value; }
    public new float angularVelocity { get => base.angularVelocity.z * Mathf.Rad2Deg; set => base.angularVelocity = new Vector3(0, 0, value * Mathf.Deg2Rad); }
    public new Vector2 position { get => (Vector2)base.position; set => base.position = new Vector3(value.x, value.y, base.position.z); }
    public new float rotation { get => base.rotation.eulerAngles.z; set => base.rotation = Quaternion.Euler(0, 0, value); }
    public new Vector2 centerOfMass { get => (Vector2)base.centerOfMass; set => base.centerOfMass = value; }
    public new Vector2 worldCenterOfMass => (Vector2)base.worldCenterOfMass;
    public float inertia { get => inertiaTensor.z; set => inertiaTensor = new Vector3(1, 1, value); }
    public new RigidbodyConstraints2D constraints
    {
        get => (RigidbodyConstraints2D)(((int)base.constraints >> 1 & 3) | ((int)base.constraints >> 4 & 4));
        set => base.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY
            | (RigidbodyConstraints)(((int)value & 3) << 1 | ((int)value & 4) << 4);
    }
    public bool freezeRotation { get => (constraints & RigidbodyConstraints2D.FreezeRotation) != 0; set => constraints = value ? constraints | RigidbodyConstraints2D.FreezeRotation : constraints & ~RigidbodyConstraints2D.FreezeRotation; }
    public void AddForce(Vector2 force, ForceMode2D mode = ForceMode2D.Force) => base.AddForce(force, Convert(mode));
    public void AddRelativeForce(Vector2 force, ForceMode2D mode = ForceMode2D.Force) => base.AddRelativeForce(force, Convert(mode));
    public void AddForceAtPosition(Vector2 force, Vector2 point, ForceMode2D mode = ForceMode2D.Force)
        => base.AddForceAtPosition(force, new Vector3(point.x, point.y, base.position.z), Convert(mode));
    public void AddTorque(float torque, ForceMode2D mode = ForceMode2D.Force) => base.AddTorque(new Vector3(0, 0, torque), Convert(mode));
    public Vector2 GetPointVelocity(Vector2 point) => (Vector2)base.GetPointVelocity(new Vector3(point.x, point.y, base.position.z));
    public void MovePosition(Vector2 point) => position = point;
    public void MoveRotation(float angle) => rotation = angle;
    private static ForceMode Convert(ForceMode2D mode) => mode switch
    {
        ForceMode2D.Force => ForceMode.Force, ForceMode2D.Impulse => ForceMode.Impulse, _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
