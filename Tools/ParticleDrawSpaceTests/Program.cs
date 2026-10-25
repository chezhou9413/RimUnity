using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using RimUnity;
using Object = RimUnity.Object;

internal static partial class Program
{
    internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static string game, runtime, harmony;
    private static int checks;
    private static class BenchmarkState { internal static Vector3 Sink; }
    private static int Main(string[] args)
    {
        runtime = Path.GetFullPath(args[0]); game = Path.GetFullPath(args[1]); harmony = Path.GetFullPath(args[2]);
        AssemblyLoadContext.Default.Resolving += (_, name) => Resolve(name);
        try { if (args[3] == "--particle-detail") CheckParticleDetail(); else if (args[3] == "--billboard-math") CheckBillboardMath(Path.GetFullPath(args[4])); else if (args[3] == "--billboard-phases") MeasureBillboardPhases(); else if (args[3] == "--packed-billboards") CheckPackedBillboards(Path.GetFullPath(args[4])); else if (args[3] == "--attribute-evaluation") CheckAttributeEvaluation(Path.GetFullPath(args[4])); else if (args[3] == "--stream-pack") CheckStreamPacking(Path.GetFullPath(args[4])); else if (args[3] == "--mesh-directions") CheckMeshDirections(Path.GetFullPath(args[4])); else if (args[3] == "--geometry-batch") CheckGeometryBatch(Path.GetFullPath(args[4])); else if (args[3] == "--sort-reuse") CheckSortReuse(Path.GetFullPath(args[4])); else if (args[3] == "--noise-reuse") NoiseReuseChecks.Run(Path.GetFullPath(args[4])); else if (args[3] == "--trail-reuse") CheckTrailReuse(Path.GetFullPath(args[4])); else if (args[3] == "--mixed-stress") MixedStressChecks.Run(); else if (args[3] == "--particle-physics-timeline") ParticlePhysicsTimelineChecks.Run(); else if (args[3] == "--particle-turnover") ParticleTurnoverChecks.Run(); else if (args[3] == "--mesh-alignment") CheckMeshAlignment(); else Run(Path.GetFullPath(args[3]), args.Length > 4 && !string.IsNullOrWhiteSpace(args[4]) ? Path.GetFullPath(args[4]) : null); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void CheckTrailReuse(string baseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("trail endpoint baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(baseline));
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        Check(old.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId != typeof(ParticleSystem).Module.ModuleVersionId,
            "trail baseline must differ from the current runtime");
        int scenario = 0;
        foreach (string simulation in new[] { "Local", "World", "Custom" })
        foreach (string scaling in new[] { "Hierarchy", "Local", "Shape" })
        foreach (string texture in new[] { "Stretch", "Tile", "DistributePerSegment", "RepeatPerSegment", "Static" })
        foreach (bool ribbon in new[] { false, true })
        foreach (int count in new[] { 0, 1, 2, 3, 17, 64 })
        foreach (int space in new[] { 0, 1, 2 })
        {
            old.Configure(simulation, scaling, "Billboard", "View", 1, scenario);
            current.Configure(simulation, scaling, "Billboard", "View", 1, scenario);
            var a = old.Trail(count, texture, ribbon, space, scenario);
            var b = current.Trail(count, texture, ribbon, space, scenario);
            var expected = old.TrailChannels(a, ribbon); var actual = current.TrailChannels(b, ribbon);
            foreach (string channel in expected.Keys)
                Check(expected[channel].SequenceEqual(actual[channel]), $"trail mismatch {scenario} {texture}/{ribbon}/{count}/{space}: {channel}");
            scenario++;
        }
        Console.WriteLine($"PASS {scenario} trail scenarios / {scenario * 9} exact channel comparisons, including empty, Ribbon, five texture modes, event/world/local/custom spaces and edited curves. No GPU.");
        CheckTrailChannelWrites(old, current);
        BenchmarkTrailSpaces(old, current);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void CheckSortReuse(string baseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("particle sort baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(baseline));
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        int scenario = 0;
        foreach (string simulation in new[] { "Local", "World", "Custom" })
        foreach (string scaling in new[] { "Hierarchy", "Local", "Shape" })
        foreach (string mode in new[] { "Billboard", "Stretch", "HorizontalBillboard", "VerticalBillboard", "Mesh" })
        foreach (string alignment in new[] { "View", "World", "Local", "Facing", "Velocity" })
        for (int camera = 0; camera < 3; camera++)
        {
            old.Configure(simulation, scaling, mode, alignment, camera, scenario);
            current.Configure(simulation, scaling, mode, alignment, camera, scenario);
            var expected = old.Geometry(camera, scenario); var actual = current.Geometry(camera, scenario);
            foreach (string field in expected.Keys)
                Check(expected[field].SequenceEqual(actual[field]), $"prepared velocity geometry mismatch {scenario} {mode}/{alignment}/{simulation}/{scaling}/camera{camera}: {field}");
            // Visibility and draw must retain the same axes and size clamp.
            Check(old.Axes(camera, scenario).SequenceEqual(current.Axes(camera, scenario)), "visibility axes changed");
            scenario++;
        }
        Console.WriteLine($"PASS {scenario} geometry + visibility scenarios / {scenario * 9} exact channel comparisons, including Mesh and prepared world velocity. No GPU.");
        int sorts = 0;
        foreach (string simulation in new[] { "Local", "World", "Custom" })
        foreach (string scaling in new[] { "Hierarchy", "Local", "Shape" })
        foreach (string sort in new[] { "None", "Distance", "OldestInFront", "YoungestInFront", "Depth" })
        foreach (int count in new[] { 0, 1, 2, 16, 64, 257 })
        foreach (int distribution in new[] { 0, 1, 2 })
        {
            old.Configure(simulation, scaling, "Billboard", "View", 1, sorts);
            current.Configure(simulation, scaling, "Billboard", "View", 1, sorts);
            var slow = old.SortAction(count, sort, distribution); var fast = current.SortAction(count, sort, distribution);
            for (int draw = 0; draw < 3; draw++)
            {
                slow(); fast();
                Check(old.SortOrder().SequenceEqual(current.SortOrder()), $"sort order differs: {sort}/{count}/{distribution}/{simulation}/{scaling}/draw{draw}");
                Check(current.SortScratchEmpty(), "sort scratch or system reference retained");
                // New draw snapshots must observe moved parents/custom spaces/cameras.
                old.MoveSortRig(draw); current.MoveSortRig(draw);
            }
            sorts++;
        }
        Console.WriteLine($"PASS {sorts} sort configurations x 3 draws: identical order, ties/near ties/events, camera/transform changes and scratch cleanup.");
        current.CheckSortFailure();
        foreach (var path in new[] { ("Billboard", "View"), ("Billboard", "Velocity"), ("Stretch", "Velocity"), ("Mesh", "Velocity") })
        {
            old.Configure("Local", "Hierarchy", path.Item1, path.Item2, 1, 1);
            current.Configure("Local", "Hierarchy", path.Item1, path.Item2, 1, 1);
            var slowGeometry = old.BuildGeometryBatch(); var fastGeometry = current.BuildGeometryBatch();
            for (int i = 0; i < 50; i++) { slowGeometry(); fastGeometry(); }
            for (int run = 0; run < 3; run++) foreach (bool optimized in new[] { false, true })
            {
                var action = optimized ? fastGeometry : slowGeometry;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 50; i++) action();
                long ticks = Stopwatch.GetTimestamp() - start;
                Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "prepared velocity geometry allocates after warmup");
                Console.WriteLine($"Geometry CPU run {run + 1}, {path.Item1}/{path.Item2}, optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 50:F6} ms per 4000 particles / 300 systems, 0 B; synthetic only.");
            }
        }
        foreach (string simulation in new[] { "World", "Local" })
        foreach (string sort in new[] { "Depth", "Distance", "OldestInFront" })
        {
            old.Configure(simulation, "Hierarchy", "Billboard", "View", 1, 1);
            current.Configure(simulation, "Hierarchy", "Billboard", "View", 1, 1);
            var slow = old.SortAction(64, sort, 0); var fast = current.SortAction(64, sort, 0);
            for (int i = 0; i < 1000; i++) { slow(); fast(); }
            for (int run = 0; run < 3; run++) foreach (bool optimized in new[] { false, true })
            {
                var action = optimized ? fast : slow;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 1000; i++) action();
                long ticks = Stopwatch.GetTimestamp() - start;
                Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "sort path allocates after warmup");
                Console.WriteLine($"Sort CPU run {run + 1}, {simulation}/{sort}, optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 1000:F6} ms per 64 particles, includes snapshots/order refill, 0 B; synthetic only.");
            }
        }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void CheckGeometryBatch(string baseline)
    {
        CheckSortReuse(baseline);
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        CheckVertexReuse(baseline, current, true);
        CheckDenseQuads(baseline, current);
        CheckPopulation(current);
        Benchmark(current);
    }
    private static void CheckDenseQuads(string baseline, Rig current)
    {
        var context = new AssemblyLoadContext("dense quad baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(baseline));
        old.EnableCustomChannels(); current.EnableCustomChannels();
        int cases = 0;
        foreach (var layout in new[] { Array.Empty<string>(), new[] { "UV" }, new[] { "Position", "Normal", "Color", "Tangent" },
            new[] { "UV", "UV2", "AnimBlend" }, new[] { "UV", "Custom1XYZW", "Custom2XY", "VertexID" } })
        foreach (int count in new[] { 0, 1, 2, 3, 4, 5, 16, 64, 1024 })
        {
            old.Configure("Local", "Hierarchy", "Billboard", "View", 1, cases);
            current.Configure("Local", "Hierarchy", "Billboard", "View", 1, cases);
            old.Streams(layout); current.Streams(layout);
            var expected = old.DenseGeometry(count); var actual = current.DenseGeometry(count);
            foreach (string name in expected.Keys) Check(expected[name].SequenceEqual(actual[name]), $"dense quad mismatch {count}/{string.Join('+', layout)}: {name}");
            Check(actual["vertices"].Length == count * 4 && actual["indices"].Length == count * 6, "dense quad channel counts diverge");
            cases++;
        }
        Console.WriteLine($"PASS {cases} dense quad growth/layout cases, 0..1024 particles, nonzero custom channels, exact output and repeated ClearMesh/layout changes.");
    }
    private static Assembly Resolve(AssemblyName name)
    {
        foreach (string folder in new[] { runtime, game })
        {
            string path = Path.Combine(folder, name.Name + ".dll");
            if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
        return name.Name == "0Harmony" ? AssemblyLoadContext.Default.LoadFromAssemblyPath(harmony) : null;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void CheckMeshAlignment()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        using var rig = new Rig(typeof(ParticleSystem).Assembly);
        rig.CheckMeshAlignment();
        Console.WriteLine("PASS mesh alignment: View, World, Local, Facing, Velocity and shape orientation; actual triangle vertices under a top-down camera.");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Run(string baseline, string vertexBaseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("frozen particle baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        var oldAssembly = context.LoadFromAssemblyPath(baseline);
        Check(oldAssembly.GetType("RimUnity.ParticleSystem").GetNestedType("DrawSpace", BindingFlags.NonPublic) == null,
            "baseline must predate the DrawSpace optimization");
        using var old = new Rig(oldAssembly);
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        int scenario = 0;
        foreach (string simulation in new[] { "Local", "World", "Custom" })
        foreach (string scaling in new[] { "Hierarchy", "Local", "Shape" })
        // The frozen DLL ignored Mesh alignment; that corrected behavior has its own geometry assertions.
        foreach (string mode in new[] { "Billboard", "Stretch", "HorizontalBillboard", "VerticalBillboard" })
        foreach (string alignment in new[] { "View", "World", "Local", "Facing", "Velocity" })
        for (int camera = 0; camera < 3; camera++)
        {
            old.Configure(simulation, scaling, mode, alignment, camera, scenario);
            current.Configure(simulation, scaling, mode, alignment, camera, scenario);
            var expected = old.Geometry(camera, scenario);
            var actual = current.Geometry(camera, scenario);
            foreach (string field in expected.Keys)
            {
                Check(expected[field].SequenceEqual(actual[field]), $"geometry mismatch {scenario} {simulation}/{scaling}/{mode}/{alignment}/camera{camera}: {field}");
            }
            scenario++;
        }
        Console.WriteLine($"PASS {scenario} old-DLL versus new-DLL geometry scenarios, {scenario * 9} exact channel comparisons (vertices/indices/normals/tangents/colors/UV/custom streams). No GPU/pixel rendering.");
        using (var meshRig = new Rig(typeof(ParticleSystem).Assembly)) meshRig.CheckMeshAlignment();
        if (vertexBaseline != null) CheckVertexReuse(vertexBaseline, current);
        Benchmark(current);
        CheckPopulation(current);
    }
    private static void CheckVertexReuse(string baseline, Rig current, bool customChannels = false)
    {
        var context = new AssemblyLoadContext("frozen vertex baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(baseline));
        Check(old.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId != current.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId, "marginal baseline is the current runtime");
        if (customChannels) { old.EnableCustomChannels(); current.EnableCustomChannels(); }
        var layouts = new List<string[]> { Array.Empty<string>(), new[] { "Position", "Color", "Normal", "Tangent" }, new[] { "Position", "Normal", "Color", "UV" },
            new[] { "Color", "UV", "Normal", "Position", "Tangent", "Color" }, new[] { "UV", "UV" },
            new[] { "UV", "UV2", "UV3", "UV4", "VertexID", "ParticleIndex", "AnimBlend", "AnimFrame" },
            new[] { "Custom2XYZ", "UV", "Custom1XYZW", "Speed", "VertexID", "MeshIndex" },
            new[] { "StableRandomXYZW", "VaryingRandomXYZW", "StableRandomXYZW" },
            new[] { "Speed", "Speed", "UV", "Center", "UV2", "SizeXYZ" } };
        var streamType = current.Renderer.GetType().GetProperty("vertexStreams").PropertyType.GetElementType();
        foreach (string name in Enum.GetNames(streamType))
        {
            try { current.Streams(name); layouts.Add(new[] { name }); }
            catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException) { }
        }
        int cases = 0;
        // Mesh orientation intentionally differs from this frozen baseline.
        foreach (string mode in new[] { "Billboard" })
        foreach (var layout in layouts)
        {
            old.Configure("Local", "Hierarchy", mode, "View", 1, cases);
            current.Configure("Local", "Hierarchy", mode, "View", 1, cases);
            old.Streams(layout); current.Streams(layout);
            old.AnimatedStreams(); current.AnimatedStreams();
            var expected = old.Geometry(1, cases); var actual = current.Geometry(1, cases);
            foreach (string name in expected.Keys) Check(expected[name].SequenceEqual(actual[name]), $"stream geometry mismatch {mode}/{string.Join(',', layout)}: {name}");
            // ClearMesh must invalidate prepared values before the trail-style Vertex path.
            expected = old.UnpreparedVertices(); actual = current.UnpreparedVertices();
            foreach (string name in expected.Keys) Check(expected[name].SequenceEqual(actual[name]), $"unprepared stream mismatch {mode}/{string.Join(',', layout)}: {name}");
            cases++;
        }
        foreach (var rig in new[] { old, current })
        {
            rig.Streams("Speed", "UV");
            try { rig.Streams("Custom1XYZW", "Custom1XYZW", "Custom2XYZW", "Speed"); throw new Exception("over-capacity layout accepted"); }
            catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException) { }
        }
        var left = old.Geometry(1, 0); var right = current.Geometry(1, 0);
        foreach (string name in left.Keys) Check(left[name].SequenceEqual(right[name]), "failed layout setter changed output: " + name);
        Console.WriteLine($"PASS {cases} frozen marginal-baseline DLL stream/layout cases, exact nine-channel geometry and ClearMesh/unprepared fallback, repeated layout changes and rejected-layout preservation.");
        foreach (string mode in new[] { "Billboard", "Mesh" })
        foreach (var layout in new[] { Array.Empty<string>(), new[] { "UV" }, new[] { "Position", "Normal", "Color", "UV" }, new[] { "UV", "Velocity", "Center", "SizeXYZ", "Speed" }, new[] { "UV", "StableRandomXYZW", "VaryingRandomXYZW", "Speed", "VertexID" } })
        {
            foreach (var rig in new[] { old, current }) { rig.Configure("Local", "Hierarchy", mode, "View", 1, 1); rig.Streams(layout); }
            var slow = old.BuildGeometryBatch(); var fast = current.BuildGeometryBatch();
            for (int i = 0; i < 50; i++) { slow(); fast(); }
            for (int round = 0; round < 3; round++) foreach (bool optimized in new[] { round % 2 == 0, round % 2 != 0 })
            {
                var action = optimized ? fast : slow;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100; i++) action();
                long ticks = Stopwatch.GetTimestamp() - start;
                Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "geometry batch allocated");
                Console.WriteLine($"vertex batch {mode}/{(layout.Length == 0 ? "default" : string.Join('+', layout))} optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 100:F6} ms; 4000 particles/300 chunks, setup included, 0 B; CPU geometry only, no GPU.");
            }
        }
    }
    private static void CheckPopulation(Rig rig)
    {
        var assembly = typeof(ParticleSystem).Assembly;
        var labType = assembly.GetType("RimUnity.Diagnostics.PerformanceLab", true);
        var lab = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(labType);
        var liveField = labType.GetField("live", Flags);
        var live = (IList)Activator.CreateInstance(liveField.FieldType); liveField.SetValue(lab, live);
        var entryType = liveField.FieldType.GetGenericArguments()[0];
        live.Add(Activator.CreateInstance(entryType, Flags, null, new object[] { rig.Owner, new[] { (ParticleSystem)rig.System }, double.PositiveInfinity, -1 }, null));
        var active = (HashSet<ParticleSystem>)typeof(ParticleSystem).GetField("Active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        active.Add((ParticleSystem)rig.System);
        using var background = new FixtureObject(new GameObject("background diagnostic fixture"));
        var ps = background.Value.AddComponent<ParticleSystem>(); background.Value.AddComponent<ParticleSystemRenderer>(); active.Add(ps);
        Camera.main = (Camera)rig.Camera;
        var population = assembly.GetType("RimUnity.Diagnostics.LabPopulation", true);
        var roots = new HashSet<GameObject>();
        object state = population.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { lab, roots });
        int Value(string name) => (int)state.GetType().GetProperty(name).GetValue(state);
        Check(Value("OwnedSystems") == 1 && Value("BackgroundSystems") >= 1 && roots.Count == 0, "real population ownership or scratch cleanup mismatch");
        object boundary = population.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { lab, roots });
        Check(((Array)boundary.GetType().GetProperty("Background").GetValue(boundary)).Length >= 1
            && ((Array)boundary.GetType().GetProperty("OwnedEmptyRenderers").GetValue(boundary)).Length == 1 && roots.Count == 0,
            "real boundary inventory or owned-empty classification mismatch");
        var call = Expression.Call(population.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic), Expression.Constant(lab, labType), Expression.Constant(roots));
        var read = Expression.Lambda<Action>(Expression.Block(call, Expression.Empty())).Compile();
        for (int i = 0; i < 10000; i++) read();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) read();
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes && roots.Count == 0, "real population scan allocated or retained roots");
        Console.WriteLine("PASS real runtime population scan: live-root attribution, global/background classification, zero warmed allocation and scratch reference clearing.");
        Camera.main = null;
    }
    private sealed class FixtureObject(GameObject value) : IDisposable
    {
        internal GameObject Value => value;
        public void Dispose() => Object.DestroyImmediate(value);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static void Benchmark(Rig rig)
    {
        var system = (ParticleSystem)rig.System;
        rig.Configure("Local", "Hierarchy", "Billboard", "View", 0, 1);
        var particle = (ParticleSystem.Particle)rig.Particle(1, false);
        var type = typeof(ParticleSystem).GetNestedType("DrawSpace", BindingFlags.NonPublic);
        var ctor = type.GetConstructor(Flags, null, new[] { typeof(ParticleSystem) }, null);
        var position = typeof(ParticleSystem).GetMethod("WorldPosition", Flags, null, new[] { typeof(ParticleSystem.Particle) }, null).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(system);
        var velocity = typeof(ParticleSystem).GetMethod("WorldVelocity", Flags).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(system);
        var scale = typeof(ParticleSystem).GetMethod("ParticleScale", Flags).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(system);
        object space = ctor.Invoke(new object[] { system });
        MethodInfo CoordinateMethod(string name) => type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == 1);
        Func<ParticleSystem.Particle, Vector3> CoordinateCall(object snapshot, string name)
        {
            var value = Expression.Parameter(typeof(ParticleSystem.Particle), "particle");
            return Expression.Lambda<Func<ParticleSystem.Particle, Vector3>>(
                Expression.Call(Expression.Constant(snapshot, type), CoordinateMethod(name), value), value).Compile();
        }
        var fastPosition = CoordinateCall(space, "WorldPosition");
        var fastVelocity = CoordinateCall(space, "WorldVelocity");
        var fastScale = CoordinateCall(space, "ParticleScale");
        void Calls(bool fast, int count)
        {
            var p = fast ? fastPosition : position; var v = fast ? fastVelocity : velocity; var s = fast ? fastScale : scale;
            for (int i = 0; i < count; i++) BenchmarkState.Sink = p(particle) + v(particle) + s(particle);
        }
        Calls(false, 20000); Calls(true, 20000);
        for (int round = 0; round < 3; round++) foreach (bool fast in new[] { false, true })
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            Calls(fast, 400000);
            long elapsed = Stopwatch.GetTimestamp() - start, allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            Check(allocated == 0, "warmed coordinate evaluation allocates: " + allocated);
            Console.WriteLine($"coordinate kernel snapshot={fast}: {elapsed * 1e9 / Stopwatch.Frequency / 400000:F1} ns/particle; {allocated} B (3 transforms; snapshot setup excluded, not whole renderer).");
        }
        Vector3 saved = fastPosition(particle);
        rig.Root.transform.localPosition += new Vector3(3, -2, 5);
        Check(fastPosition(particle).Equals(saved), "snapshot unexpectedly follows later transform changes");
        object next = ctor.Invoke(new object[] { system });
        var nextPosition = CoordinateCall(next, "WorldPosition");
        Check(!nextPosition(particle).Equals(saved) && nextPosition(particle).Equals(position(particle)), "next draw failed to refresh transformed hierarchy");
        Console.WriteLine("PASS zero-allocation coordinate kernels, call-local snapshot isolation and next-call refresh.");
        // Include snapshot construction once per 40 particles, matching 100 systems / 4000 particles.
        Action<ParticleSystem.Particle[]> BuildBatch(bool fast)
        {
            var data = Expression.Parameter(typeof(ParticleSystem.Particle[]), "data");
            var i = Expression.Variable(typeof(int), "i"); var frameSpace = Expression.Variable(type, "space");
            var p = Expression.Variable(typeof(ParticleSystem.Particle), "p"); var done = Expression.Label();
            Expression Eval(string name) => fast ? Expression.Call(frameSpace, CoordinateMethod(name), p)
                : Expression.Call(Expression.Constant(system), typeof(ParticleSystem).GetMethod(name, Flags, null, new[] { typeof(ParticleSystem.Particle) }, null), p);
            var statements = new List<Expression>();
            if (fast) statements.Add(Expression.IfThen(Expression.Equal(Expression.Modulo(i, Expression.Constant(40)), Expression.Constant(0)),
                Expression.Assign(frameSpace, Expression.New(ctor, Expression.Constant(system)))));
            statements.Add(Expression.Assign(p, Expression.ArrayIndex(data, i)));
            statements.Add(Expression.Assign(Expression.Field(null, typeof(BenchmarkState).GetField("Sink", BindingFlags.Static | BindingFlags.NonPublic)),
                Expression.Add(Expression.Add(Eval("WorldPosition"), Eval("WorldVelocity")), Eval("ParticleScale"))));
            statements.Add(Expression.PostIncrementAssign(i)); statements.Add(Expression.Empty());
            return Expression.Lambda<Action<ParticleSystem.Particle[]>>(Expression.Block(new[] { i, frameSpace, p }, Expression.Assign(i, Expression.Constant(0)),
                Expression.Loop(Expression.IfThenElse(Expression.LessThan(i, Expression.ArrayLength(data)), Expression.Block(statements), Expression.Break(done)), done)), data).Compile();
        }
        var slowBatch = BuildBatch(false); var fastBatch = BuildBatch(true);
        var particles = Enumerable.Repeat(particle, 4000).ToArray();
        for (int i = 0; i < 50; i++) { slowBatch(particles); fastBatch(particles); }
        for (int round = 0; round < 3; round++) foreach (bool fast in new[] { false, true })
        {
            var batch = fast ? fastBatch : slowBatch;
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 200; i++) batch(particles);
            long ticks = Stopwatch.GetTimestamp() - start;
            Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "batched coordinates or snapshot setup allocated");
            Console.WriteLine($"4000-particle coordinate batch snapshot={fast}: {ticks * 1000d / Stopwatch.Frequency / 200:F6} ms; setup included (100 snapshots/batch), 0 B; not a whole-renderer or game measurement.");
        }
    }
    private sealed partial class Rig : IDisposable
    {
        internal readonly GameObject Root = new("draw-space root"), Mid = new("draw-space mid"), Owner = new("draw-space system"), Custom = new("custom space"), CameraOwner = new("draw-space camera");
        internal readonly object System, Renderer, Camera;
        private readonly Type ps, renderer, particle, eventSpace, cameraState, drawSpace;
        private readonly object main;
        private readonly Mesh mesh;
        internal Rig(Assembly assembly)
        {
            ps = assembly.GetType("RimUnity.ParticleSystem", true); renderer = assembly.GetType("RimUnity.ParticleSystemRenderer", true);
            particle = ps.GetNestedType("Particle"); eventSpace = ps.GetNestedType("EventSpace", BindingFlags.NonPublic);
            cameraState = renderer.GetNestedType("ParticleCameraState", BindingFlags.NonPublic); drawSpace = ps.GetNestedType("DrawSpace", BindingFlags.NonPublic);
            Mid.transform.SetParent(Root.transform, false); Owner.transform.SetParent(Mid.transform, false);
            System = Owner.AddComponent(ps); Renderer = Owner.AddComponent(renderer);
            Camera = CameraOwner.AddComponent(assembly.GetType("RimUnity.Camera", true)); main = Get(System, "main");
            mesh = new Mesh { vertices = new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, 1) },
                uv = new[] { Vector2.zero, Vector2.one, new Vector2(.5f, 1) },
                uv3 = new[] { new Vector2(.1f, .2f), new Vector2(.3f, .4f), new Vector2(.5f, .6f) },
                uv4 = new[] { new Vector2(.7f, .8f), new Vector2(.9f, 1), new Vector2(1.1f, 1.2f) }, triangles = new[] { 0, 1, 2 } };
            Set(Renderer, "mesh", mesh);
            Set(Camera, "pixelRect", new Rect(0, 0, 1600, 900));
        }
        internal void Configure(string simulation, string scaling, string mode, string alignment, int camera, int scenario)
        {
            Root.transform.localPosition = new Vector3(1, -2, 3); Root.transform.localRotation = Quaternion.Euler(10, 30, -15);
            Root.transform.localScale = scenario % 3 == 0 ? new Vector3(0, -2, 3) : new Vector3(-1.5f, 2, .7f);
            Mid.transform.localRotation = Quaternion.Euler(35, -20, 11); Mid.transform.localScale = new Vector3(.5f, 1.7f, -2);
            Owner.transform.localPosition = new Vector3(2, 1, -3); Owner.transform.localRotation = Quaternion.Euler(-8, 19, 42);
            Owner.transform.localScale = new Vector3(1.3f, -.4f, 2.1f);
            Custom.transform.localPosition = new Vector3(4, -5, 1); Custom.transform.localRotation = Quaternion.Euler(40, 12, 7); Custom.transform.localScale = new Vector3(-2, 0, 3);
            Set(Get(System, "textureSheetAnimation"), "enabled", false);
            SetEnum(main, "simulationSpace", simulation); SetEnum(main, "scalingMode", scaling);
            Set(main, "customSimulationSpace", scenario % 2 == 0 ? null : Custom.transform); Set(main, "startRotation3D", scenario % 2 == 0);
            SetEnum(Renderer, "renderMode", mode); SetEnum(Renderer, "alignment", alignment);
            Set(Renderer, "pivot", new Vector3(.1f, -.2f, .3f)); Set(Renderer, "flip", new Vector3(.4f, .7f, .5f));
            Set(Renderer, "allowRoll", scenario % 2 == 0); Set(Renderer, "velocityScale", .3f);
            Set(Renderer, "minParticleSize", .01f); Set(Renderer, "maxParticleSize", .2f);
            CameraOwner.transform.localPosition = camera == 1 ? new Vector3(8, 3, -12) : new Vector3(-6, 14, 4);
            CameraOwner.transform.localRotation = Quaternion.Euler(30, 15, 7); Set(Camera, "orthographic", camera == 2);
            // Explicit streams exercise values that consume the transformed position/size/velocity.
            Streams(scenario % 2 == 0 ? new[] { "UV", "Velocity", "Center", "SizeXYZ", "Speed" } : Array.Empty<string>());
        }
        internal void CheckMeshAlignment()
        {
            CameraOwner.transform.rotation = Quaternion.Euler(90, 0, 0);
            CameraOwner.transform.position = new Vector3(0, 10, 0);
            Owner.transform.rotation = Quaternion.Euler(0, 35, 0);
            SetEnum(main, "simulationSpace", "World");
            SetEnum(Renderer, "renderMode", "Mesh");
            object p = Activator.CreateInstance(particle);
            Set(p, "startSize3D", Vector3.one); Set(p, "startLifetime", 1f);
            Set(p, "remainingLifetime", 1f); Set(p, "startColor", new Color32(255, 255, 255, 255));
            Set(p, "velocity", new Vector3(1, -1, 0));
            foreach (string mode in new[] { "View", "World", "Local", "Facing", "Velocity" })
            {
                SetEnum(Renderer, "alignment", mode);
                renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
                object cam = Activator.CreateInstance(cameraState, Flags, null, new object[] { Camera, null, true }, null);
                object space = Activator.CreateInstance(drawSpace, Flags, null, new[] { System }, null);
                renderer.GetMethod("AppendParticle", Flags).Invoke(Renderer, new[] { System, p, cam, space });
                var points = ((IEnumerable)Get(Renderer, "vertices")).Cast<Vector3>().ToArray();
                Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
                Vector3 expected = mode switch {
                    "View" or "Facing" => Vector3.down,
                    "Local" => Owner.transform.rotation * Vector3.forward,
                    "Velocity" => new Vector3(1, -1, 0).normalized,
                    _ => Vector3.forward };
                Check(Vector3.Dot(normal, expected) > .999f, "mesh normal follows " + mode);
            }
            Set(p, "alignedToDirection", true);
            SetEnum(Renderer, "alignment", "View");
            renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            object alignedCam = Activator.CreateInstance(cameraState, Flags, null, new object[] { Camera, null, true }, null);
            object alignedSpace = Activator.CreateInstance(drawSpace, Flags, null, new[] { System }, null);
            renderer.GetMethod("AppendParticle", Flags).Invoke(Renderer, new[] { System, p, alignedCam, alignedSpace });
            var alignedPoints = ((IEnumerable)Get(Renderer, "vertices")).Cast<Vector3>().ToArray();
            Check(Vector3.Dot(Vector3.Cross(alignedPoints[1] - alignedPoints[0], alignedPoints[2] - alignedPoints[0]).normalized, Vector3.forward) > .999f,
                "shape-aligned mesh retains emission orientation");
        }
        internal void EnableCustomChannels()
        {
            var module = Get(System, "customData"); Set(module, "enabled", true);
            var channels = (Array)Get(module, "streams");
            for (int channel = 0; channel < 2; channel++)
            {
                var item = channels.GetValue(channel); SetEnum(item, "mode", "Vector");
                var curves = (Array)Get(item, "curves");
                for (int i = 0; i < 4; i++) curves.SetValue(Activator.CreateInstance(curves.GetType().GetElementType(),
                    new object[] { (channel == 0 ? 1 : -1) * (.1f + i * .2f) }), i);
            }
        }
        internal void Streams(params string[] names)
        {
            var streamType = renderer.GetProperty("vertexStreams").PropertyType.GetElementType();
            var data = Array.CreateInstance(streamType, names.Length);
            for (int i = 0; i < names.Length; i++) data.SetValue(Enum.Parse(streamType, names[i]), i);
            Set(Renderer, "vertexStreams", data);
        }
        internal void AnimatedStreams()
        {
            var module = Get(System, "textureSheetAnimation");
            Set(module, "enabled", true); Set(module, "numTilesX", 3); Set(module, "numTilesY", 2);
            SetEnum(module, "timeMode", "FPS"); Set(module, "fps", 7f);
        }
        internal Dictionary<string, object[]> UnpreparedVertices()
        {
            renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            for (int i = 0; i < 3; i++) renderer.GetMethod("Vertex", Flags).Invoke(Renderer, new object[] {
                new Vector3(i, 2, 3), new Vector2(.2f * i, .3f), new Color32(10, 20, 30, 40), new Vector4(i, 2, 3, 4), new Vector4(5, 6, 7, i) });
            return Channels();
        }
        internal Action BuildGeometryBatch(int particleCount = 4000, int chunks = 300, bool packed = false, Mesh page = null)
        {
            var i = Expression.Variable(typeof(int), "i"); var j = Expression.Variable(typeof(int), "j");
            var cam = Expression.Variable(cameraState, "camera"); var space = Expression.Variable(drawSpace, "space");
            if (particleCount < chunks || chunks < 1) throw new ArgumentOutOfRangeException(nameof(particleCount));
            int perChunk = particleCount / chunks, extra = particleCount % chunks;
            var data = Array.CreateInstance(particle, perChunk + (extra > 0 ? 1 : 0));
            for (int k = 0; k < data.Length; k++) data.SetValue(Particle(k, k % 3 == 0), k);
            var outerDone = Expression.Label(); var innerDone = Expression.Label();
            var self = Expression.Constant(Renderer, renderer); var sys = Expression.Constant(System, ps);
            Expression selectPacked = packed
                ? Expression.Assign(Expression.Field(self, renderer.GetField("packedBillboards", Flags)), Expression.Constant(true)) : Expression.Empty();
            Expression appendPage = Expression.Empty();
            if (page != null)
            {
                var core = typeof(Mesh);
                appendPage = packed
                    ? Expression.Call(Expression.Constant(page), core.GetMethods(Flags).Single(m => m.Name == "AppendParticleGeometry" && m.GetParameters().Length == 10),
                        new[] { "vertices", "normals", "tangents", "colors", "packedUV", "custom1", "custom2", "indices" }
                            .Select(n => (Expression)Expression.Field(self, n)).Append(Expression.Constant(0)).Append(Expression.Field(self, "billboardVertices")))
                    : Expression.Call(Expression.Constant(page), core.GetMethods().Single(m => m.Name == "AppendParticleGeometry" && m.GetParameters().Length == 9),
                        new[] { "vertices", "normals", "tangents", "colors", "packedUV", "custom1", "custom2", "indices" }
                            .Select(n => (Expression)Expression.Field(self, n)).Append(Expression.Constant(0)));
            }
            return Expression.Lambda<Action>(Expression.Block(new[] { i, j, cam, space },
                page != null ? Expression.Call(Expression.Constant(page), typeof(Mesh).GetMethod("Clear"), Expression.Constant(true)) : Expression.Empty(),
                Expression.Assign(i, Expression.Constant(0)),
                Expression.Loop(Expression.IfThenElse(Expression.LessThan(i, Expression.Constant(chunks)), Expression.Block(
                    Expression.Call(self, renderer.GetMethod("ClearMesh", Flags)),
                    selectPacked,
                    Expression.Assign(cam, Expression.New(cameraState.GetConstructors(Flags).Single(), Expression.Constant(Camera, Camera.GetType()), Expression.Constant(null, cameraState.GetConstructors(Flags).Single().GetParameters()[1].ParameterType), Expression.Constant(true))),
                    Expression.Assign(space, Expression.New(drawSpace.GetConstructors(Flags).Single(), sys)),
                    Expression.Assign(j, Expression.Constant(0)),
                    Expression.Loop(Expression.IfThenElse(Expression.LessThan(j, Expression.Condition(Expression.LessThan(i, Expression.Constant(extra)), Expression.Constant(perChunk + 1), Expression.Constant(perChunk))), Expression.Block(
                        Expression.Call(self, renderer.GetMethod("AppendParticle", Flags), sys, Expression.ArrayIndex(Expression.Constant(data, particle.MakeArrayType()), j), cam, space),
                        Expression.PostIncrementAssign(j), Expression.Empty()), Expression.Break(innerDone)), innerDone),
                    appendPage,
                    Expression.PostIncrementAssign(i), Expression.Empty()), Expression.Break(outerDone)), outerDone),
                page != null ? Expression.Property(Expression.Constant(page), "EngineMesh") : Expression.Empty(),
                Expression.Empty())).Compile();
        }
        internal object Particle(int id, bool withEvent)
        {
            object p = Activator.CreateInstance(particle);
            Set(p, "position", new Vector3(id - 2, id * .2f, 1 - id)); Set(p, "velocity", new Vector3(id * .3f, -2, .4f));
            Set(p, "moduleVelocity", new Vector3(.1f, .4f, -.2f)); Set(p, "startSize3D", new Vector3(1, .7f, 1.2f));
            Set(p, "startColor", new Color32(150, 80, 200, 170)); Set(p, "rotation3D", new Vector3(id * 7, -12, 34));
            Set(p, "angularVelocity3D", new Vector3(id + 3, -4, 7));
            Set(p, "startLifetime", id == 0 ? 0f : 5f + id); Set(p, "remainingLifetime", 3f); Set(p, "elapsedAge", 2f + id * .1f); Set(p, "randomSeed", (uint)(123 + id));
            Set(p, "alignedToDirection", id == 3);
            if (withEvent)
            {
                object space = Activator.CreateInstance(eventSpace, true);
                Set(space, "local", id % 2 == 0); Set(space, "position", new Vector3(10 + id, -3, 4));
                Set(space, "rotation", Quaternion.Euler(id * 20, 45, 8)); Set(space, "scale", new Vector3(-2, .5f, id == 2 ? 0 : 3));
                Set(p, "eventSpace", space);
            }
            return p;
        }
        internal Dictionary<string, object[]> Geometry(int camera, int scenario, bool packed = false)
        {
            renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            if (packed) Set(Renderer, "packedBillboards", true);
            object cameraData = Activator.CreateInstance(cameraState, Flags, null, new[] { camera == 0 ? null : Camera, null, (object)(scenario % 2 == 0) }, null);
            object space = drawSpace == null ? null : Activator.CreateInstance(drawSpace, Flags, null, new[] { System }, null);
            var append = renderer.GetMethod("AppendParticle", Flags);
            for (int i = 0; i < 4; i++)
            {
                object p = Particle(i, i % 2 == 0 || i == 3);
                append.Invoke(Renderer, drawSpace == null ? new[] { System, p, cameraData } : new[] { System, p, cameraData, space });
            }
            return Channels();
        }
        internal object Trail(int count, string texture, bool ribbon, int spaceMode, int scenario)
        {
            var module = Get(System, "trails");
            SetEnum(module, "textureMode", texture); Set(module, "worldSpace", spaceMode == 1);
            var curve = ps.GetNestedType("MinMaxCurve");
            var animation = ps.Assembly.GetType("RimUnity.AnimationCurve");
            var line = animation.GetMethod("Linear").Invoke(null, new object[] { 0f, .15f, 1f, 1.3f });
            Set(module, "widthOverTrail", scenario % 2 == 0 ? Activator.CreateInstance(curve, new object[] { .7f })
                : Activator.CreateInstance(curve, new object[] { 1.2f, line }));
            var gradient = new Gradient { mode = (GradientMode)(scenario % 3) };
            gradient.SetKeys(new[] { new GradientColorKey(new Color(.1f, .5f, 1, 1), 0), new GradientColorKey(new Color(1, .2f, .4f, .3f), 1) },
                new[] { new GradientAlphaKey(.2f, 0), new GradientAlphaKey(.9f, 1) });
            var gradientType = ps.GetNestedType("MinMaxGradient");
            Set(module, "colorOverTrail", Activator.CreateInstance(gradientType, new object[] { gradient }));
            Set(module, "colorOverLifetime", Activator.CreateInstance(gradientType, new object[] { gradient }));
            var trailType = ps.GetNestedType("TrailState", BindingFlags.NonPublic);
            var pointType = ps.GetNestedType("TrailPoint", BindingFlags.NonPublic);
            var trail = Activator.CreateInstance(trailType, true);
            Set(trail, "width", .45f); Set(trail, "color", new Color(.3f, .5f, .7f, .8f));
            if (spaceMode == 2)
            {
                var eventData = Activator.CreateInstance(eventSpace, true);
                Set(eventData, "local", true); Set(eventData, "position", new Vector3(3, -4, 5));
                Set(eventData, "rotation", Quaternion.Euler(13, 44, -7)); Set(eventData, "scale", new Vector3(-2, .7f, 0));
                Set(trail, "space", eventData);
            }
            var points = (IList)Get(trail, "points");
            for (int i = 0; i < count; i++)
            {
                var point = Activator.CreateInstance(pointType);
                Set(point, "position", new Vector3(i / 3f, i % 3 == 0 ? .2f : .5f, i * .13f));
                Set(point, "width", .1f + i * .01f); Set(point, "color", new Color(.3f, .2f + i * .01f, .7f, .6f));
                Set(point, "lifetime", i % 4 == 0 ? float.PositiveInfinity : 2f);
                Set(point, "remaining", 1f + i * .02f); Set(point, "distance", i * .21f);
                points.Add(point);
            }
            return trail;
        }
        internal Dictionary<string, object[]> TrailChannels(object trail, bool ribbon, Vector3? forward = null, bool clear = true)
        {
            if (clear) renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            var append = renderer.GetMethod("AppendTrail", Flags);
            object[] args = { System, trail, ribbon, forward ?? Vector3.forward };
            if (append.GetParameters().Length == 5)
                args = args.Append(Activator.CreateInstance(drawSpace, Flags, null, new[] { System }, null)).ToArray();
            append.Invoke(Renderer, args);
            return Channels();
        }
        internal Action TrailBatch(object trail, bool ribbon)
        {
            var i = Expression.Variable(typeof(int), "i"); var done = Expression.Label();
            var self = Expression.Constant(Renderer, renderer);
            var space = Expression.Variable(drawSpace, "space");
            var append = renderer.GetMethod("AppendTrail", Flags);
            var args = new List<Expression> { Expression.Constant(System, ps), Expression.Constant(trail, trail.GetType()), Expression.Constant(ribbon), Expression.Constant(Vector3.forward) };
            if (append.GetParameters().Length == 5) args.Add(space);
            return Expression.Lambda<Action>(Expression.Block(new[] { i, space },
                Expression.Call(self, renderer.GetMethod("ClearMesh", Flags)),
                append.GetParameters().Length == 5
                    ? Expression.Assign(space, Expression.New(drawSpace.GetConstructors(Flags).Single(), Expression.Constant(System, ps))) : Expression.Empty(),
                Expression.Assign(i, Expression.Constant(0)),
                Expression.Loop(Expression.IfThenElse(Expression.LessThan(i, Expression.Constant(256)), Expression.Block(
                    Expression.Call(self, append, args),
                    Expression.PostIncrementAssign(i), Expression.Empty()), Expression.Break(done)), done))).Compile();
        }
        internal Vector3[] Axes(int camera, int scenario)
        {
            object cameraData = Activator.CreateInstance(cameraState, Flags, null, new[] { camera == 0 ? null : Camera, null, (object)(scenario % 2 == 0) }, null);
            var args = new[] { System, Particle(2, true), cameraData, (object)new Vector3(1, 2, 3), new Vector3(.7f, .3f, 1), null, null };
            renderer.GetMethod("BillboardAxes", Flags).Invoke(Renderer, args);
            return new[] { (Vector3)args[5], (Vector3)args[6] };
        }
        internal Action SortAction(int count, string mode, int distribution)
        {
            SetEnum(Renderer, "sortMode", mode);
            var data = Array.CreateInstance(particle, count);
            for (int i = 0; i < count; i++)
            {
                var p = Particle(i, i % 3 == 0);
                float x = distribution == 0 ? (i * 137 % 61) * .03f : distribution == 1 ? 0 : (i % 3) * float.Epsilon;
                Set(p, "position", new Vector3(x, 0, distribution == 0 ? (i * 47 % 71) * .01f : x));
                if (distribution != 0) Set(p, "eventSpace", null);
                Set(p, "elapsedAge", distribution == 0 ? (i * 17 % 39) * .13f : x);
                data.SetValue(p, i);
            }
            Set(System, "particles", data); Set(System, "count", count);
            var order = (List<int>)Get(Renderer, "order");
            var self = Expression.Constant(Renderer, renderer); var sys = Expression.Constant(System, ps);
            var cam = Expression.Variable(cameraState, "camera"); var space = Expression.Variable(drawSpace, "space");
            var steps = new List<Expression> {
                Expression.Assign(cam, Expression.New(cameraState.GetConstructors(Flags).Single(), Expression.Constant(Camera, Camera.GetType()), Expression.Constant(null, cameraState.GetConstructors(Flags).Single().GetParameters()[1].ParameterType), Expression.Constant(true))),
                Expression.Assign(space, Expression.New(drawSpace.GetConstructors(Flags).Single(), sys)) };
            if (mode != "None")
            {
                var method = renderer.GetMethod("SortParticles", Flags);
                if (method != null) steps.Add(Expression.Call(self, method, sys, cam, space));
                else if (renderer.GetMethod("PrepareSorting", Flags) is { } prepare)
                {
                    var comparison = (Comparison<int>)renderer.GetMethod("CompareParticles", Flags).CreateDelegate(typeof(Comparison<int>), Renderer);
                    steps.Add(Expression.TryFinally(Expression.Block(Expression.Call(self, prepare, sys, cam, space),
                        Expression.Call(Expression.Constant(order), typeof(List<int>).GetMethod("Sort", new[] { typeof(Comparison<int>) }), Expression.Constant(comparison))),
                        Expression.Call(self, renderer.GetMethod("ClearSorting", Flags))));
                }
                else
                {
                    steps.Add(Expression.Assign(Expression.Field(self, renderer.GetField("sortingSystem", Flags)), sys));
                    steps.Add(Expression.Assign(Expression.Field(self, renderer.GetField("sortingPosition", Flags)), Expression.Field(cam, cameraState.GetField("Position", Flags))));
                    steps.Add(Expression.Assign(Expression.Field(self, renderer.GetField("sortingForward", Flags)), Expression.Field(cam, cameraState.GetField("Forward", Flags))));
                    var comparison = (Comparison<int>)renderer.GetMethod("CompareParticles", Flags).CreateDelegate(typeof(Comparison<int>), Renderer);
                    steps.Add(Expression.TryFinally(Expression.Call(Expression.Constant(order), typeof(List<int>).GetMethod("Sort", new[] { typeof(Comparison<int>) }), Expression.Constant(comparison)),
                        Expression.Assign(Expression.Field(self, renderer.GetField("sortingSystem", Flags)), Expression.Constant(null, ps))));
                }
            }
            steps.Add(Expression.Empty());
            var execute = Expression.Lambda<Action>(Expression.Block(new[] { cam, space }, steps)).Compile();
            return () => { order.Clear(); for (int i = 0; i < count; i++) order.Add(i); execute(); };
        }
        internal void CheckSortFailure()
        {
            var invalid = SortAction(16, "Depth", 0);
            Set(System, "particles", Array.CreateInstance(particle, 1));
            bool failed = false;
            try { invalid(); } catch (IndexOutOfRangeException) { failed = true; }
            Check(failed && SortScratchEmpty(), "exception during coordinate preparation clears partial scratch and system reference");
            var restored = SortAction(16, "Depth", 0); restored();
            Check(SortOrder().Distinct().Count() == 16 && SortScratchEmpty(), "renderer can sort again after preparation failure");
        }
        internal int[] SortOrder() => ((List<int>)Get(Renderer, "order")).ToArray();
        internal bool SortScratchEmpty() => Get(Renderer, "sortingSystem") == null
            && ((List<Vector3>)Get(Renderer, "sortingWorldPositions")).Count == 0;
        internal void MoveSortRig(int draw)
        {
            Root.transform.localPosition += new Vector3(.2f * draw, -.1f, .3f);
            Custom.transform.localPosition += new Vector3(-1, .2f, .1f);
            CameraOwner.transform.localRotation = Quaternion.Euler(15 * draw, 45, 7);
        }
        internal Dictionary<string, object[]> DenseGeometry(int count)
        {
            renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            var cameraData = Activator.CreateInstance(cameraState, Flags, null, new[] { Camera, null, (object)true }, null);
            var space = Activator.CreateInstance(drawSpace, Flags, null, new[] { System }, null);
            var append = renderer.GetMethod("AppendParticle", Flags);
            for (int i = 0; i < count; i++) append.Invoke(Renderer, new[] { System, Particle(i, i % 3 == 0), cameraData, space });
            if (count > 0) Check((int)Get(Renderer, "streamVertexId") == 3, "last quad vertex identity must stay unchanged");
            return Channels();
        }
        private Dictionary<string, object[]> Channels()
        {
            var output = new Dictionary<string, object[]>();
            foreach (string name in new[] { "vertices", "indices", "normals", "tangents", "colors", "uv", "packedUV", "custom1", "custom2" })
                output.Add(name, ((IEnumerable)Get(Renderer, name)).Cast<object>().ToArray());
            return output;
        }
        public void Dispose()
        {
            Object.DestroyImmediate(Root); Object.DestroyImmediate(Custom); Object.DestroyImmediate(CameraOwner); Object.DestroyImmediate(mesh);
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
        private static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, Flags);
            if (field != null) field.SetValue(target, value); else target.GetType().GetProperty(name, Flags).SetValue(target, value);
        }
        private static void SetEnum(object target, string name, string value) => Set(target, name, Enum.Parse(target.GetType().GetField(name, Flags).FieldType, value));
    }
}
