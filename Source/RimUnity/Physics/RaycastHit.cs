namespace RimUnity;

//记录射线命中的碰撞器、位置、法线和距离。
public struct RaycastHit
{
    public Collider collider { get; internal set; }
    public Vector3 point { get; internal set; }
    public Vector3 normal { get; internal set; }
    public float distance { get; internal set; }
    public Transform transform => collider?.transform;
    public Rigidbody rigidbody => collider?.attachedRigidbody;
}
