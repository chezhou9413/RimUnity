
namespace RimUnity;

//模拟位置、发射形状和显示速度分别按 scalingMode 使用对应的缩放。
public sealed partial class ParticleSystem
{
    internal Transform SimulationTransform => main.simulationSpace == ParticleSystemSimulationSpace.Local ? transform
        : main.simulationSpace == ParticleSystemSimulationSpace.Custom ? main.customSimulationSpace : null;

    //Hierarchy 保留完整矩阵；Local 忽略父缩放；Shape 的模拟空间不再缩放移动。
    internal Vector3 WorldPosition(Vector3 position)
    {
        if (main.simulationSpace == ParticleSystemSimulationSpace.Local && main.scalingMode != ParticleSystemScalingMode.Hierarchy)
            return transform.position + transform.rotation * Vector3.Scale(position, EventScale);
        return SimulationTransform != null ? SimulationTransform.TransformPoint(position) : position;
    }

    //出生点按同一空间反算，避免 Shape 模式把形状缩放抵消掉。
    internal Vector3 FromWorld(Vector3 position)
    {
        if (main.simulationSpace == ParticleSystemSimulationSpace.Local && main.scalingMode != ParticleSystemScalingMode.Hierarchy)
            return DivideScale(Quaternion.Inverse(transform.rotation) * (position - transform.position), EventScale);
        return SimulationTransform != null ? SimulationTransform.InverseTransformPoint(position) : position;
    }

    //模块坐标轴只旋转；世界移动速度另按位置变换的线性部分求值。
    internal Vector3 WorldVector(Vector3 vector) => SimulationTransform != null ? SimulationTransform.TransformDirection(vector) : vector;
    internal Vector3 WorldVelocity(Particle particle)
    {
        Vector3 velocity = particle.totalVelocity;
        if (particle.eventSpace is { local: true } space) return space.rotation * Vector3.Scale(velocity, space.scale);
        if (main.simulationSpace == ParticleSystemSimulationSpace.Local && main.scalingMode != ParticleSystemScalingMode.Hierarchy)
            return transform.rotation * Vector3.Scale(velocity, EventScale);
        return SimulationTransform != null ? SimulationTransform.TransformVector(velocity) : velocity;
    }

    //Local 形状使用自身缩放；Hierarchy 和 Shape 的出生范围使用整个层级。
    private Vector3 ShapeWorldPosition(Vector3 position) => main.scalingMode == ParticleSystemScalingMode.Local
        ? transform.position + transform.rotation * Vector3.Scale(position, transform.localScale)
        : transform.TransformPoint(position);

    //事件结束后保留其最后缩放，避免残留粒子的尺寸突然跟回组件。
    internal Vector3 ParticleScale(Particle particle) => particle.eventSpace is { local: true } space ? space.scale : EventScale;

    //零缩放轴没有可逆坐标，将其折叠到局部原点。
    private static Vector3 DivideScale(Vector3 value, Vector3 scale) => new(
        scale.x == 0 ? 0 : value.x / scale.x, scale.y == 0 ? 0 : value.y / scale.y, scale.z == 0 ? 0 : value.z / scale.z);
}
