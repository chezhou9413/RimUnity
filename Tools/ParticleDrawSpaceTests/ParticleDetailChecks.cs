using System.Reflection;
using System.Text.Json;
using RimUnity;

internal static partial class Program
{
    private static void CheckParticleDetail()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var assembly = typeof(ParticleSystem).Assembly;
        var type = assembly.GetType("RimUnity.Diagnostics.LabParticleDetail", true);
        var capture = Activator.CreateInstance(type, Flags, null, new object[] { 128, null }, null);
        Action Method(string name) => (Action)type.GetMethod(name, Flags).CreateDelegate(typeof(Action), capture);
        var start = Method("Start"); var begin = Method("BeginFrame"); var end = Method("CompleteFrame"); var stop = Method("Stop");
        using var rig = new Rig(assembly);
        rig.Configure("World", "Hierarchy", "Billboard", "View", 1, 1); rig.Streams("UV"); rig.AnimatedStreams();
        var geometry = rig.BuildGeometryBatch(64, 1, packed: true);
        var root = new GameObject("particle detail integration fixture"); root.SetActive(false);
        var controlRoot = new GameObject("unprofiled control fixture"); controlRoot.SetActive(false);
        try
        {
            assembly.GetType("RimUnity.Diagnostics.LabStressScenario", true).GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { root, 6, null, null });
            assembly.GetType("RimUnity.Diagnostics.LabStressScenario", true).GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { controlRoot, 6, null, null });
            var control = controlRoot.GetComponent<ParticleSystem>(); control.randomSeed = 9413; control.useAutoRandomSeed = false;
            var system = root.GetComponent<ParticleSystem>(); system.randomSeed = 9413; system.useAutoRandomSeed = false;
            geometry(); system.Simulate(.25f, false, true, true); control.Simulate(.25f, false, true, true);
            var left = new ParticleSystem.Particle[64]; var right = new ParticleSystem.Particle[64];
            var fields = typeof(ParticleSystem.Particle).GetFields(Flags).Where(f => f.Name != "identity").ToArray();
            start();
            try
            {
                for (int frame = 0; frame < 64; frame++)
                {
                    control.Simulate(1f / 60, false, false, true);
                    begin(); geometry(); system.Simulate(1f / 60, false, false, true); end();
                    int a = system.GetParticles(left), b = control.GetParticles(right);
                    Check(a == b, "capture preserves particle count");
                    for (int i = 0; i < a; i++) foreach (var field in fields)
                        Check(Equals(field.GetValue(left[i]), field.GetValue(right[i])), "capture preserves particle field/order: " + field.Name);
                    // Identity is globally assigned, so two separate systems intentionally differ there.
                    Check(Equals(system.GetType().GetField("rng", Flags).GetValue(system), control.GetType().GetField("rng", Flags).GetValue(control)),
                        "capture preserves random state");
                }
            }
            finally { stop(); }
            var snapshot = type.GetMethod("Snapshot", Flags).Invoke(capture, null);
            using var report = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
            var domains = report.RootElement.GetProperty("Domains");
            Check(domains[0].GetProperty("SelectedFrames").GetInt32() == 3 && domains[1].GetProperty("SelectedFrames").GetInt32() == 2,
                "real runtime respects domain frame selection");
            foreach (string stage in new[] { "Size", "Color", "Custom", "Atlas", "Axes", "Projection", "VertexWrite" })
                Check(domains[0].GetProperty("Stages").EnumerateArray().Single(s => s.GetProperty("Stage").GetString() == stage).GetProperty("Calls").GetInt64() > 0,
                    "real geometry probe covered: " + stage);
            foreach (string stage in new[] { "SystemStep", "EmissionSchedule", "EmissionTimeline", "AdvanceParticles", "Integrate", "Noise" })
                Check(domains[1].GetProperty("Stages").EnumerateArray().Single(s => s.GetProperty("Stage").GetString() == stage).GetProperty("Calls").GetInt64() > 0,
                    "real simulation probe covered: " + stage);
            Check(report.RootElement.GetProperty("InvalidFramesOrScopes").GetInt64() == 0, "real scopes remain balanced");
            foreach (var domain in domains.EnumerateArray())
                Check(domain.GetProperty("Work").EnumerateArray().Single(s => s.GetProperty("Counter").GetString() == "CurveCalls").GetProperty("Count").GetInt64() > 0,
                    "actual curve evaluations counted");
            Console.WriteLine("PASS real analysis runtime: selected geometry/simulation frames, stage coverage, curve counts and balanced scopes. CPU only, no GPU/pixel claim.");
            Console.WriteLine(JsonSerializer.Serialize(snapshot));
        }
        finally { RimUnity.Object.DestroyImmediate(root); RimUnity.Object.DestroyImmediate(controlRoot); }
    }
}
