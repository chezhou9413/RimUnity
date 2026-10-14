
namespace RimUnity;

//2D 基类复用生命周期与对象查找，但物理查询通过 Is2D 明确隔离两个世界。
public abstract class Collider2D : Collider
{
    public Vector2 offset;
    internal override bool Is2D => true;
    internal abstract ParticlePlanarShape PlanarShape { get; }
    internal override ParticlePhysicsShape Shape
    {
        get
        {
            ParticlePlanarShape planar = PlanarShape;
            Vector3 minimum = planar.center, maximum = planar.center;
            if (planar.kind == ParticlePlanarShape.Kind.Polygon || planar.kind == ParticlePlanarShape.Kind.Edge)
            {
                minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0); maximum = -minimum;
                foreach (Vector3[] path in planar.paths) foreach (Vector3 point in path) { minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point); }
            }
            else
            {
                Vector3 half = ParticlePhysicsShape.Abs(planar.axis) * planar.halfSegment;
                minimum -= half; maximum += half;
            }
            return new ParticlePhysicsShape { kind = ParticlePhysicsShape.Kind.Planar, planar = planar, center = (minimum + maximum) * .5f,
                extents = (maximum - minimum) * .5f + new Vector3(planar.radius, planar.radius, 0) };
        }
    }
    public new Rigidbody2D attachedRigidbody => base.attachedRigidbody as Rigidbody2D;
    public new PhysicsMaterial2D sharedMaterial { get => base.sharedMaterial as PhysicsMaterial2D; set => base.sharedMaterial = value; }
    public Vector2 ClosestPoint(Vector2 point) => (Vector2)base.ClosestPoint(point);
    public bool OverlapPoint(Vector2 point) => PlanarShape.Distance(point, out _) <= 0;
    //宿主 3D Physics.Raycast 不应命中借用同一组件基类的 2D 对象。
    public override bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance) { hitInfo = default; return false; }
    protected Vector3 WorldPoint(Vector2 point) => ParticlePlanarShape.XY(transform.TransformPoint((Vector3)(point + offset)));
    protected Vector2 WorldScale => new(ParticlePlanarShape.XY(transform.TransformVector(Vector3.right)).magnitude,
        ParticlePlanarShape.XY(transform.TransformVector(Vector3.up)).magnitude);
}
