using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using RimUnity;

// Run the same executable in separate processes against baseline/candidate runtimes.
// Fixed seeds and creation order make private identities comparable as well.
internal static class SimulationDispatchChecks
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo Configure = typeof(ParticleSystem).Assembly
        .GetType("RimUnity.Diagnostics.LabStressScenario", true).GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic);

    private static GameObject Create(int kind)
    {
        var root = new GameObject("simulation dispatch fixture"); root.SetActive(false);
        Configure.Invoke(null, new object[] { root, kind, null, null });
        foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
        { system.useAutoRandomSeed = false; system.randomSeed = (uint)(9413 + kind); }
        root.transform.position = new Vector3(2, -1, 3);
        root.transform.localScale = new Vector3(2, .5f, 1.5f);
        root.transform.rotation = Quaternion.Euler(15, 30, 45);
        root.SetActive(true);
        if (!root.GetComponentsInChildren<ParticleSystem>(true).All(p => p.gameObject.activeInHierarchy))
            throw new Exception("Simulation fixture contains an inactive emitter");
        return root;
    }

    internal static void Run(string destination)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        CheckRetainedSubEmission(writer, false);
        CheckRetainedSubEmission(writer, true);
        int frames = 0;
        foreach (bool fixedStep in new[] { false, true })
        for (int kind = 0; kind < 12; kind++)
        {
            var root = Create(kind);
            try
            {
                var system = root.GetComponent<ParticleSystem>();
                var systems = root.GetComponentsInChildren<ParticleSystem>(true);
                for (int frame = 0; frame < 192; frame++)
                {
                    // Changes are made between synchronous advances, including turning
                    // modules off while their already-created trails/events still exist.
                    if (frame == 32) { system.collision.enabled = true; system.collision.type = ParticleSystemCollisionType.World; }
                    if (frame == 48) system.trails.enabled = true;
                    if (frame == 64) system.trigger.enabled = true;
                    if (frame == 80) { system.collision.enabled = false; system.trails.enabled = false; }
                    if (frame == 96)
                    {
                        if (kind == 11 && Events(system).Count == 0)
                            throw new Exception("Subemitter toggle must exercise live retained events");
                        system.trigger.enabled = false; system.subEmitters.enabled = false;
                    }
                    if (frame == 112) system.subEmitters.enabled = kind == 11;
                    if (frame == 128) { system.trails.enabled = true; system.trails.mode = ParticleSystemTrailMode.Ribbon; }
                    if (frame == 144) system.trails.mode = ParticleSystemTrailMode.PerParticle;
                    if (frame == 160) system.emission.enabled = false;
                    system.Simulate(frame % 3 == 0 ? .013f : .021f, true, frame == 0, fixedStep);
                    foreach (var child in systems) Snapshot(writer, child);
                    frames++;
                }
            }
            finally { RimUnity.Object.DestroyImmediate(root); }
        }
        writer.Flush();
        File.WriteAllText(destination, Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))));
        Console.WriteLine($"PASS captured {frames} frames: 12 recipes, both timestep modes, module toggles, pending events/trails, transforms; {buffer.Length} snapshot bytes.");
    }

    private static IList Events(ParticleSystem system) =>
        (IList)typeof(ParticleSystem).GetField("subEmissionEvents", Fields).GetValue(system);

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, Fields).GetValue(owner);

    private static void CheckRetainedSubEmission(BinaryWriter writer, bool fixedStep)
    {
        var root = Create(11);
        try
        {
            var parent = root.GetComponent<ParticleSystem>();
            var child = parent.subEmitters.GetSubEmitterSystem(0);
            parent.emission.enabled = false;
            parent.main.startLifetime = 10; parent.main.maxParticles = 1;
            parent.shape.enabled = false;
            child.main.startLifetime = 10; child.main.maxParticles = 128;
            child.main.startSpeed = 0; child.shape.enabled = false;
            child.emission.rateOverTime = 20;
            child.emission.bursts = Array.Empty<ParticleSystem.Burst>();
            parent.Simulate(0, true, true, fixedStep);
            parent.Emit(new ParticleSystem.EmitParams { velocity = new Vector3(1, 2, 3) }, 1);
            if (Events(parent).Count != 1) throw new Exception("Birth event was not created before disabling its module");
            parent.Simulate(.1f, true, false, fixedStep);
            var retained = Events(parent)[0];
            float elapsed = Field<float>(retained, "elapsed");
            Vector3 position = Field<Vector3>(retained, "position");
            int before = child.particleCount;
            parent.subEmitters.enabled = false;
            // Parent emission is also disabled, so later child births must come from
            // this retained event rather than an accidentally created replacement.
            parent.Simulate(.2f, true, false, fixedStep);
            if (Events(parent).Count != 1 || !ReferenceEquals(Events(parent)[0], retained))
                throw new Exception("Disabling subemitters replaced or retired the retained event");
            if (Field<float>(retained, "elapsed") <= elapsed || child.particleCount <= before)
                throw new Exception("Retained event stopped advancing/emitting after the module was disabled");
            var particles = new ParticleSystem.Particle[1];
            if (parent.GetParticles(particles) != 1) throw new Exception("Moving parent disappeared");
            var world = (Vector3)typeof(ParticleSystem).GetMethod("WorldPosition", Fields, null,
                new[] { typeof(ParticleSystem.Particle) }, null).Invoke(parent, new object[] { particles[0] });
            if (Field<Vector3>(retained, "position") != world || world == position
                || Field<ParticleSystem.Particle>(retained, "parent").position != particles[0].position)
                throw new Exception("Retained event lost its moving parent's final trajectory");
            Snapshot(writer, parent); Snapshot(writer, child);
            Console.WriteLine($"PASS retained Birth event, fixedStep={fixedStep}: module disabled, child {before}->{child.particleCount}, elapsed advanced and final parent motion followed.");
        }
        finally { RimUnity.Object.DestroyImmediate(root); }
    }

    private static void Snapshot(BinaryWriter writer, ParticleSystem system)
    {
        var particles = new ParticleSystem.Particle[system.particleCount];
        system.GetParticles(particles); Value(writer, particles);
        foreach (string name in new[] { "rng", "trailStates", "ribbons", "subEmissionEvents", "subParentMotions" })
            Value(writer, typeof(ParticleSystem).GetField(name, Fields).GetValue(system));
    }

    private static void Value(BinaryWriter writer, object value)
    {
        if (value == null) { writer.Write(false); return; }
        writer.Write(true);
        switch (value)
        {
            case float v: writer.Write(v); return;
            case double v: writer.Write(v); return;
            case bool v: writer.Write(v); return;
            case byte v: writer.Write(v); return;
            case int v: writer.Write(v); return;
            case uint v: writer.Write(v); return;
            case long v: writer.Write(v); return;
            case ulong v: writer.Write(v); return;
            case RimUnity.Object v: writer.Write(v.GetInstanceID()); return;
            case IEnumerable values:
                int count = 0; foreach (object item in values) { Value(writer, item); count++; }
                writer.Write(count); return;
        }
        if (value.GetType().IsEnum) { writer.Write(Convert.ToInt32(value)); return; }
        if (value is DictionaryEntry entry) { Value(writer, entry.Key); Value(writer, entry.Value); return; }
        var type = value.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        { Value(writer, type.GetProperty("Key").GetValue(value)); Value(writer, type.GetProperty("Value").GetValue(value)); return; }
        var fields = type.GetFields(Fields).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        if (fields.Length == 0) throw new Exception("Uncovered snapshot value " + type);
        foreach (var field in fields) Value(writer, field.GetValue(value));
    }

    internal static void Benchmark()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var roots = Enumerable.Range(0, 48).Select(i => Create(i % 12)).ToArray();
        var systems = roots.Select(r => r.GetComponent<ParticleSystem>()).ToArray();
        try
        {
            // Use the ordinary build: no detailed probes. Warm the JIT and reach capacity.
            for (int i = 0; i < 2400; i++) foreach (var system in systems) system.Simulate(1f / 144, true, i == 0, false);
            var times = new double[15]; long allocations = 0;
            for (int batch = 0; batch < times.Length; batch++)
            {
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 240; i++) foreach (var system in systems) system.Simulate(1f / 144, true, false, false);
                times[batch] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / 240;
                allocations += GC.GetAllocatedBytesForCurrentThread() - bytes;
            }
            Array.Sort(times);
            Console.WriteLine(FormattableString.Invariant($"CPU only, 48 mixed roots: mean={times.Average():F6} ms/frame, median={times[7]:F6}, min={times[0]:F6}, max={times[^1]:F6}, allocations={allocations / 3600d:F3} B/frame; no geometry/GPU."));
        }
        finally { foreach (var root in roots) RimUnity.Object.DestroyImmediate(root); }
    }
}
