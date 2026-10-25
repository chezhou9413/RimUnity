using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using RimUnity;

internal static partial class Program
{
    private static void CheckBillboardMath(string baseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("billboard math baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(baseline));
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        Check(old.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId != typeof(ParticleSystem).Module.ModuleVersionId, "billboard math baseline must differ");
        var random = new Random(9413);
        int projections = 0, axes = 0;
        var edge = new[] { 0f, -0f, 1e-8f, -1e-7f, 1e-7f, float.Epsilon, float.PositiveInfinity,
            float.NegativeInfinity, BitConverter.Int32BitsToSingle(unchecked((int)0xffc00123)) };
        for (int m = 0; m < 48; m++)
        {
            var matrix = Matrix4x4.identity;
            for (int i = 0; i < 16; i++) matrix[i] = (float)(random.NextDouble() * 4 - 2);
            if (m < edge.Length) matrix.m33 = edge[m];
            if (m % 3 == 0) matrix.m30 = matrix.m31 = matrix.m32 = 0;
            if (m % 4 == 0) matrix.m20 = matrix.m21 = matrix.m22 = matrix.m23 = float.NaN;
            var viewport = (m % 5) switch {
                0 => new Rect(0, 0, 1600, 900), 1 => new Rect(-12.5f, 39.25f, 801.5f, 599.75f),
                2 => new Rect(10, -20, 0, 0), 3 => new Rect(-0f, 0, -.5f, .5f), _ => new Rect(2, 3, -800, 400) };
            object left = old.ProjectionState(matrix, viewport), right = current.ProjectionState(matrix, viewport);
            foreach (var point in edge.Select(v => new Vector3(v, -v, .7f)).Concat(Enumerable.Range(0, 100).Select(_ =>
                new Vector3((float)(random.NextDouble() * 100 - 50), (float)(random.NextDouble() * 100 - 50), (float)(random.NextDouble() * 100 - 50)))))
            {
                var a = old.Project(left, point); var b = current.Project(right, point);
                Check(ChannelBytes("uv", new object[] { a }).SequenceEqual(ChannelBytes("uv", new object[] { b })), "projection bits differ: " + projections);
                projections++;
            }
            foreach (var limits in new[] { (0f, .5f), (.01f, .2f), (.4f, .1f), (-1f, 0f), (float.NaN, float.PositiveInfinity) })
            foreach (float value in edge)
            {
                var center = new Vector3(.5f, -.3f, value); var x = new Vector3(value, .15f, -.03f); var y = new Vector3(.1f, -value, .05f);
                SameVectors(old.Clamp(left, center, x, y, limits.Item1, limits.Item2), current.Clamp(right, center, x, y, limits.Item1, limits.Item2), "clamp");
            }
        }
        foreach (string mode in new[] { "Billboard", "Stretch", "HorizontalBillboard", "VerticalBillboard" })
        foreach (string alignment in new[] { "View", "World", "Local", "Facing", "Velocity" })
        foreach (bool threeD in new[] { false, true })
        foreach (bool aligned in new[] { false, true })
        foreach (int camera in new[] { 0, 1, 2 })
        foreach (float value in edge)
        {
            foreach (var rig in new[] { old, current }) rig.Configure("Local", "Hierarchy", mode, alignment, camera, axes);
            SameVectors(old.AxesEdges(camera, threeD, aligned, value), current.AxesEdges(camera, threeD, aligned, value), "axes " + axes);
            axes++;
        }
        Console.WriteLine($"PASS {projections} exact projection cases, 2160 clamp boundary cases, {axes} axis cases: missing/ortho/perspective cameras, arbitrary matrices/near-zero w, viewport rounding, 2D/3D/emission rotation, velocity, zero/negative/nonfinite values.");
        foreach (var path in new[] { ("Billboard", "View", false), ("Billboard", "View", true),
            ("HorizontalBillboard", "View", false), ("VerticalBillboard", "View", false), ("Stretch", "Velocity", false) })
        {
            foreach (var rig in new[] { old, current }) { rig.Configure("World", "Hierarchy", path.Item1, path.Item2, 2, 1); rig.Streams("UV"); }
            foreach (string phase in new[] { "Clamp", "Axes" })
            {
                var slow = old.BillboardPhase(phase, 2944, path.Item3); var fast = current.BillboardPhase(phase, 2944, path.Item3);
                Measure(slow, fast, phase + "/" + path);
            }
            var slowGeometry = old.BuildGeometryBatch(2944, 52, packed: true);
            var fastGeometry = current.BuildGeometryBatch(2944, 52, packed: true);
            Measure(slowGeometry, fastGeometry, "Geometry/" + path);
        }
        static void SameVectors(Vector3[] a, Vector3[] b, string label)
            => Check(ChannelBytes("vertices", a.Cast<object>().ToArray()).SequenceEqual(ChannelBytes("vertices", b.Cast<object>().ToArray())), label + " bits differ");
        static void Measure(Action slow, Action fast, string label)
        {
            for (int i = 0; i < 100; i++) { slow(); fast(); }
            for (int run = 0; run < 3; run++) foreach (bool optimized in new[] { run % 2 == 0, run % 2 != 0 })
            {
                var action = optimized ? fast : slow;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 150; i++) action();
                long ticks = Stopwatch.GetTimestamp() - start;
                Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "math benchmark allocates after warmup");
                Console.WriteLine($"Math CPU run {run + 1}, {label}, optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 150:F6} ms per 2944 particles; 0 B; geometry includes attributes/52 draw snapshots, excludes sorting/upload/GPU.");
            }
        }
    }

    private static void MeasureBillboardPhases()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        using var rig = new Rig(typeof(ParticleSystem).Assembly);
        foreach (var path in new[] { ("Billboard", "View", false), ("Billboard", "View", true),
            ("HorizontalBillboard", "View", false), ("Stretch", "Velocity", false) })
        {
            rig.Configure("World", "Hierarchy", path.Item1, path.Item2, 2, 1); rig.Streams();
            foreach (string phase in new[] { "Clamp", "Axes", "Write" })
            {
                var action = rig.BillboardPhase(phase, 2944, path.Item3);
                for (int i = 0; i < 100; i++) action();
                for (int run = 0; run < 3; run++)
                {
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 200; i++) action();
                    long ticks = Stopwatch.GetTimestamp() - start;
                    Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "phase benchmark allocates after warmup");
                    Console.WriteLine($"Phase {phase} run {run + 1}, {path.Item1}/{path.Item2}/3D={path.Item3}: {ticks * 1000d / Stopwatch.Frequency / 200:F6} ms per 2944 particles; 0 B; synthetic CPU only. Axes includes Clamp; do not sum these phases.");
                }
            }
        }
    }

    private sealed partial class Rig
    {
        internal object ProjectionState(Matrix4x4 matrix, Rect viewport)
        {
            Camera.GetType().GetProperty("projectionMatrix").SetValue(Camera, matrix);
            Camera.GetType().GetProperty("worldToCameraMatrix").SetValue(Camera, Matrix4x4.identity);
            Set(Camera, "pixelRect", viewport);
            return Activator.CreateInstance(cameraState, Flags, null, new object[] { Camera, null, true }, null);
        }
        internal Vector2 Project(object state, Vector3 point)
            => (Vector2)cameraState.GetMethod("ViewportPoint", Flags).Invoke(state, new object[] { point });
        internal Vector3[] Clamp(object state, Vector3 center, Vector3 x, Vector3 y, float min, float max)
        {
            Set(Renderer, "minParticleSize", min); Set(Renderer, "maxParticleSize", max);
            var args = new object[] { center, state, x, y };
            renderer.GetMethod("ClampBillboardSize", Flags).Invoke(Renderer, args);
            return new[] { (Vector3)args[2], (Vector3)args[3] };
        }
        internal Vector3[] AxesEdges(int camera, bool threeD, bool aligned, float value)
        {
            // Restore real camera projections after the arbitrary-matrix tests.
            Camera.GetType().GetMethod("ResetProjectionMatrix").Invoke(Camera, null);
            Camera.GetType().GetMethod("ResetWorldToCameraMatrix").Invoke(Camera, null);
            Set(main, "startRotation3D", threeD);
            object p = Particle(2, true); Set(p, "alignedToDirection", aligned);
            Set(p, "rotation3D", new Vector3(value, -.3f, value));
            object state = Activator.CreateInstance(cameraState, Flags, null, new object[] { camera == 0 ? null : Camera, null, true }, null);
            var args = new object[] { System, p, state, new Vector3(value, .3f, -.2f), new Vector3(.3f, -.5f, value), new Vector3(value, -value, .2f), Vector3.zero, Vector3.zero };
            renderer.GetMethod("BillboardAxesPrepared", Flags).Invoke(Renderer, args);
            return new[] { (Vector3)args[6], (Vector3)args[7] };
        }

        internal Action BillboardPhase(string phase, int count, bool threeD)
        {
            Set(main, "startRotation3D", threeD);
            var data = Array.CreateInstance(particle, count);
            var centers = new Vector3[count]; var sizes = new Vector3[count]; var velocities = new Vector3[count];
            for (int k = 0; k < count; k++)
            {
                object p = Particle(k % 64, false); Set(p, "alignedToDirection", false); data.SetValue(p, k);
                centers[k] = new Vector3(k % 64 * .15f, k % 17 * .2f, .3f);
                sizes[k] = new Vector3(.3f, .4f, .2f); velocities[k] = new Vector3(.5f, -.1f, .7f);
            }
            var self = Expression.Constant(Renderer, renderer); var sys = Expression.Constant(System, ps);
            var i = Expression.Variable(typeof(int)); var cam = Expression.Variable(cameraState);
            var x = Expression.Variable(typeof(Vector3)); var y = Expression.Variable(typeof(Vector3));
            var done = Expression.Label();
            var center = Expression.ArrayIndex(Expression.Constant(centers), i);
            var size = Expression.ArrayIndex(Expression.Constant(sizes), i);
            Expression call = phase switch {
                "Clamp" => Expression.Call(self, renderer.GetMethod("ClampBillboardSize", Flags), center, cam, x, y),
                "Axes" => Expression.Call(self, renderer.GetMethod("BillboardAxesPrepared", Flags), sys,
                    Expression.ArrayIndex(Expression.Constant(data, particle.MakeArrayType()), i), cam, center, size,
                    Expression.ArrayIndex(Expression.Constant(velocities), i), x, y),
                "Write" => Expression.Call(self, renderer.GetMethod("AppendSimpleBillboard", Flags), center, x, y,
                    Expression.Constant(new Rect(.1f, .2f, .3f, .4f)), Expression.Constant(new Color32(120, 80, 240, 180)),
                    Expression.Constant(new Vector4(1, 2, 3, 4)), Expression.Constant(new Vector4(5, 6, 7, 8))),
                _ => throw new ArgumentException(phase) };
            return Expression.Lambda<Action>(Expression.Block(new[] { i, cam, x, y },
                Expression.Call(self, renderer.GetMethod("ClearMesh", Flags)),
                phase == "Write" ? Expression.Assign(Expression.Field(self, renderer.GetField("packedBillboards", Flags)), Expression.Constant(true)) : Expression.Empty(),
                Expression.Assign(cam, Expression.New(cameraState.GetConstructors(Flags).Single(), Expression.Constant(Camera, Camera.GetType()),
                    Expression.Constant(null, cameraState.GetConstructors(Flags).Single().GetParameters()[1].ParameterType), Expression.Constant(true))),
                Expression.Assign(i, Expression.Constant(0)),
                Expression.Loop(Expression.IfThenElse(Expression.LessThan(i, Expression.Constant(count)), Expression.Block(
                    Expression.Assign(x, Expression.Constant(new Vector3(.15f, 0, .03f))),
                    Expression.Assign(y, Expression.Constant(new Vector3(.01f, .2f, 0))), call,
                    Expression.PostIncrementAssign(i), Expression.Empty()), Expression.Break(done)), done),
                Expression.Assign(Expression.Field(null, typeof(BenchmarkState).GetField("Sink", BindingFlags.Static | BindingFlags.NonPublic)), x),
                Expression.Empty())).Compile();
        }
    }
}
