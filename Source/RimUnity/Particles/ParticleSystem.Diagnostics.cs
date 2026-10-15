namespace RimUnity;

// Read-only inputs for the explicit spike capture; never used to drive simulation.
public sealed partial class ParticleSystem
{
    internal bool DiagnosticPrewarmPending => prewarmPending;
    internal float DiagnosticFrameDelta => frameDelta;
}
