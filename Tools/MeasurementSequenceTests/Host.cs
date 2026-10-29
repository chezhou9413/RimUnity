namespace RimUnity
{
    internal class ParticleSystem
    {
        internal readonly Main main = new(); internal readonly Emission emission = new(); internal readonly Subs subEmitters = new();
        internal class Main { internal int maxParticles; internal Curve startLifetime = new(); }
        internal class Curve { internal float constant = 4; }
        internal class Emission { internal float rateOverTime, rateOverDistance; }
        internal class Subs { internal Entry[] entries = []; }
        internal class Entry { internal ParticleSystem system; }
    }
}
namespace RimWorld
{
    internal static class CameraJumper
    {
        internal enum MovementMode { Cut }
        internal static void TryJump(object a, object b, MovementMode c) { }
    }
}
namespace Verse
{
    internal static class GenFilePaths { internal static readonly string ConfigFolderPath = Path.Combine(Path.GetTempPath(), "RimUnity", "Sequence-" + Guid.NewGuid().ToString("N")); }
    internal static class DebugSettings { internal static bool ShowDevGizmos = true; }
    internal static class Find { internal static readonly Driver CameraDriver = new(); internal class Driver { internal void SetRootSize(float value) { } } }
    internal static class Log { internal static void Error(string value) { } }
}
namespace RimUnity.Diagnostics
{
    internal enum LabCpuMode { Off, BridgeOnly, Stages }
    internal static class ParticleSpikeCapture { internal static bool LabCaptureActive; }
    internal sealed class LabSampler
    {
        internal bool Sampling; internal int Stops; internal double Seconds;
        internal string LastReport, MeasurementStopReason;
        internal void Stop(string reason, bool export) { if (Sampling) { Sampling = false; Stops++; } }
        internal void Complete(string reason = "采样时间结束") { Sampling = false; MeasurementStopReason = reason; LastReport = "fixture.txt"; }
        internal LabMeasurementPlan.Run MeasurementResult(LabMeasurementPlan.Job job) =>
            new(job, LastReport, 10, job.Mode == LabCpuMode.Off ? 0 : 9, 1, .1, 1.1, 1.2, job.Roots, job.Roots * job.ParticlesPerRoot, 100, 400, 600, .1, 0, 0, 0, 0);
    }
    internal sealed partial class PerformanceLab
    {
        internal static double Clock;
        private static double Now => Clock;
        internal bool IsCurrent = true, FixedInstances, StressBloom = true, Playing, FailStart;
        internal int Concurrency = 17, SampleSeconds = 30, Starts, Clears;
        internal float WarmupSeconds = 5, Radius = 8, Scale = 2, RotationX = 15, Rate = 3, Lifetime = 5;
        internal LabCpuMode CpuMode = LabCpuMode.Off;
        internal readonly LabSampler Sampler = new();
        internal string Status;
        internal readonly FakeMap Map = new();
        internal class FakeMap { internal object Center => null; }
        internal void StartPlayback(bool mixedStress) { Starts++; Playing = !FailStart; }
        internal void StopPlayback() { Playing = false; Clears++; }
        internal void UpdateSequence() => UpdateMeasurementSequence();
        internal void ConfigureLoad(ParticleSystem[] systems) => ConfigureMeasurementLoad(systems);
    }
}
