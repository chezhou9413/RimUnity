
namespace RimUnity;

//同一子系统的不同触发各有局部原点，粒子和拖尾共享所属事件的坐标框架。
public sealed partial class ParticleSystem
{
    internal sealed class EventSpace
    {
        internal Vector3 position, scale;
        internal Quaternion rotation;
        internal bool local;
    }

    //世界和 Custom 模式仍沿用显式配置，只有 Local 事件使用独立跟随框架。
    internal Vector3 WorldPosition(Particle particle) => WorldPosition(particle.position, particle.eventSpace);
    internal Vector3 WorldPosition(Vector3 position, EventSpace space)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.SpaceConversions);
#endif
        return space is { local: true } ? space.position + space.rotation * Vector3.Scale(position, space.scale) : WorldPosition(position);
    }
    internal Vector3 WorldVector(Vector3 vector, EventSpace space) => space is { local: true } ? space.rotation * vector : WorldVector(vector);
    private Vector3 FromWorld(Vector3 position, EventSpace space)
    {
        if (space is not { local: true }) return FromWorld(position);
        Vector3 local = Quaternion.Inverse(space.rotation) * (position - space.position);
        return DivideScale(local, space.scale);
    }
    private Vector3 FromWorldVector(Vector3 vector, EventSpace space) => space is { local: true } ? Quaternion.Inverse(space.rotation) * vector
        : SimulationTransform != null ? SimulationTransform.InverseTransformDirection(vector) : vector;
    internal Quaternion ParticleRotation(Particle particle) => particle.eventSpace is { local: true } space ? space.rotation
        : SimulationTransform != null ? SimulationTransform.rotation : Quaternion.identity;

    //Shape 缩放不放大粒子的移动与大小，Local 缩放忽略层级上的额外倍率。
    private EventSpace CreateEventSpace(Vector3 position) => new()
    {
        position = position, rotation = transform.rotation, local = main.simulationSpace == ParticleSystemSimulationSpace.Local,
        scale = EventScale
    };
    private Vector3 EventScale => main.scalingMode == ParticleSystemScalingMode.Shape ? Vector3.one
        : main.scalingMode == ParticleSystemScalingMode.Local ? transform.localScale : transform.localToWorldMatrix.lossyScale;
}
