using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using RimUnity;

internal static class Program
{
    static void Main(string[] args)
    {
        // Resolve the actual runtime/game assemblies; no replacement engine types.
        var directories = args.Select(Path.GetFullPath).ToArray();
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            foreach (string directory in directories) {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        Run();
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var root = new GameObject("emission recovery regression");
        try {
            var system = root.AddComponent<ParticleSystem>();
            system.main.loop = true;
            system.main.startLifetime = 10;
            system.main.startSpeed = 0;
            system.shape.enabled = false;
            system.emission.rateOverTime = 1;
            system.Simulate(.5f, false, true, false);
            Require(system.particleCount == 0, "Half a particle should remain in carry");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity }) {
                system.emission.rateOverTime = invalid;
                system.Simulate(.25f, false, false, false);
                Require(system.particleCount == 0, "Invalid samples must not emit");
            }
            system.emission.rateOverTime = 1;
            system.Simulate(.5f, false, false, false);
            Require(system.particleCount == 1, "Valid emission must resume with preserved carry and no catch-up");
            var particles = new ParticleSystem.Particle[4];
            system.GetParticles(particles);
            float lifetime = particles[0].remainingLifetime;
            system.emission.rateOverTime = float.PositiveInfinity;
            system.emission.rateOverDistance = float.NaN;
            system.Simulate(.25f, false, false, false);
            system.GetParticles(particles);
            Require(system.particleCount == 1 && particles[0].remainingLifetime < lifetime,
                "Existing particles must keep advancing during invalid emissions");
            system.emission.rateOverTime = 0;
            system.emission.rateOverDistance = 1;
            root.transform.position = new Vector3(1, 0, 0);
            system.Simulate(.25f, false, false, false);
            Require(system.particleCount == 2, "Distance emission must also recover");

            // An invalid sample must not poison either fractional accumulator.
            var schedule = typeof(ParticleSystem).GetMethod("ScheduleContinuous", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }) {
                object[] values = { .75f, invalid, 0f, 1f, 0f, 1f };
                schedule.Invoke(system, values);
                Require((float)values[0] == .75f, "Invalid sample changed carry");
            }
            Console.WriteLine("PASS: invalid samples are silent; time/distance emission recovers, carry is preserved, existing particles advance, no catch-up.");
        } finally { RimUnity.Object.DestroyImmediate(root); }
    }
}
