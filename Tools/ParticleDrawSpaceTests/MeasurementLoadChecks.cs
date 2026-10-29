using System.Reflection;
using System.Runtime.CompilerServices;
using RimUnity;

internal static partial class Program
{
    private static void CheckMeasurementLoad()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var assembly = typeof(ParticleSystem).Assembly;
        var labType = assembly.GetType("RimUnity.Diagnostics.PerformanceLab", true);
        var lab = RuntimeHelpers.GetUninitializedObject(labType);
        var configure = assembly.GetType("RimUnity.Diagnostics.LabStressScenario", true)
            .GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic);
        var load = labType.GetMethod("ConfigureMeasurementLoad", Flags);
        var pointCount = typeof(ParticleSystem).GetMethod("DiagnosticTrailPointCount", Flags);
        int cases = 0;
        foreach (int target in new[] { 16, 64, 256 })
        for (int family = 0; family < 12; family++)
        {
            var root = new GameObject("measurement load fixture"); root.SetActive(false);
            try
            {
                configure.Invoke(null, new object[] { root, family, null, null });
                var systems = root.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in systems) { ps.useAutoRandomSeed = false; ps.randomSeed = 9413; }
                labType.GetField("StressParticleTarget", Flags).SetValue(lab, target);
                load.Invoke(lab, new object[] { systems });
                root.SetActive(true);
                var parent = root.GetComponent<ParticleSystem>();
                Check(systems.All(p => p.gameObject.activeInHierarchy), "load fixture emitters must be active");
                Check(systems.All(p => p.main.maxParticles == target), "configured capacity does not match target");
                for (int frame = 0; frame < 240; frame++) parent.Simulate(.05f, true, frame == 0, false);
                Check(parent.particleCount >= target - 2 && parent.particleCount <= target,
                    $"actual root occupancy did not reach target: family={family}, target={target}, actual={parent.particleCount}");
                if (family == 11)
                {
                    var child = systems.Single(p => p != parent);
                    Check(child.emission.rateOverTime.constant == 0 && child.emission.bursts.Length == 1,
                        "scale configuration changed child event contract");
                    Check(child.particleCount > 0, "event-driven load is absent");
                }
                if (family == 5) Check((int)pointCount.Invoke(parent, null) > 0, "trail point workload is absent");
                cases++;
            }
            finally { RimUnity.Object.DestroyImmediate(root); }
        }
        Console.WriteLine($"PASS {cases} real-runtime scale configurations: actual 16/64/256 occupancy, active child emission, preserved child contract, nonzero trail-point counts. CPU only, no map/GPU.");
    }
}
