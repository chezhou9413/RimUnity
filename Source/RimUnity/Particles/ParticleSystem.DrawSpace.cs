namespace RimUnity;

public sealed partial class ParticleSystem
{
    // Owned by one synchronous renderer call, never a frame/global cache. No scene references.
    // Event-local particles keep their own per-particle origin/rotation/scale.
    internal readonly struct DrawSpace
    {
        private readonly bool localUnscaled, transformed;
        private readonly Vector3 position, scale;
        private readonly Quaternion rotation;
        private readonly Matrix4x4 matrix;

        internal DrawSpace(ParticleSystem system)
        {
            this = default;
            scale = system.EventScale;
            localUnscaled = system.main.simulationSpace == ParticleSystemSimulationSpace.Local
                && system.main.scalingMode != ParticleSystemScalingMode.Hierarchy;
            if (localUnscaled)
            {
                position = system.transform.position;
                rotation = system.transform.rotation;
            }
            else
            {
                var source = system.SimulationTransform;
                transformed = source != null;
                if (transformed) matrix = source.localToWorldMatrix;
            }
        }

        internal Vector3 WorldPosition(in Particle particle)
            => WorldPosition(particle.position, particle.eventSpace);

        internal Vector3 WorldPosition(Vector3 point, EventSpace eventSpace)
        {
#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.SpaceConversions, 1);
#endif

            if (eventSpace is { local: true } space)
                return space.position + space.rotation * Vector3.Scale(point, space.scale);
            if (localUnscaled) return position + rotation * Vector3.Scale(point, scale);
            return transformed ? matrix.MultiplyPoint3x4(point) : point;
        }

        internal Vector3 WorldVelocity(in Particle particle)
        {
#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.SpaceConversions, 1);
#endif

            if (particle.eventSpace is { local: true } space)
                return space.rotation * Vector3.Scale(particle.totalVelocity, space.scale);
            if (localUnscaled) return rotation * Vector3.Scale(particle.totalVelocity, scale);
            return transformed ? matrix.MultiplyVector(particle.totalVelocity) : particle.totalVelocity;
        }

        internal Vector3 ParticleScale(in Particle particle)
            => particle.eventSpace is { local: true } space ? space.scale : scale;
    }
}
