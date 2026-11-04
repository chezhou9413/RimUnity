#if RIMUNITY_PARTICLE_ANALYSIS
using System;

namespace RimUnity;

// Analysis builds only. The lab owns the observer from Start to Stop; Core never
// depends on the particle renderer, game state or lab implementation.
internal enum ParticleMeshStage { Validate, Prepare, CopyVertices, CopyIndices, Bounds, Flush, PackVertices, PackIndices }
internal interface IParticleMeshObserver
{
    long Begin(ParticleMeshStage stage);
    void End(long token);
}
internal static class ParticleMeshProbe
{
    internal static IParticleMeshObserver Observer;
    internal static Scope Measure(ParticleMeshStage stage)
    {
        var observer = Observer;
        long token = observer?.Begin(stage) ?? 0;
        return token == 0 ? default : new Scope(observer, token);
    }
    internal readonly struct Scope(IParticleMeshObserver observer, long token) : IDisposable
    {
        public void Dispose() => observer?.End(token);
    }
}
#endif
