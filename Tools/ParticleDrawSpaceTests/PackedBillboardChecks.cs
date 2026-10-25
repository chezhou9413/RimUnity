using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using RimUnity;
using Object = RimUnity.Object;

internal static partial class Program
{
    private static void CheckPackedBillboards(string baseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("packed billboard baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(baseline));
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        Check(old.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId != typeof(ParticleSystem).Module.ModuleVersionId, "baseline must differ");
        int cases = 0;
        foreach (string simulation in new[] { "Local", "World", "Custom" })
        foreach (string scaling in new[] { "Hierarchy", "Local", "Shape" })
        foreach (string mode in new[] { "Billboard", "Stretch", "HorizontalBillboard", "VerticalBillboard" })
        foreach (string alignment in new[] { "View", "World", "Local", "Facing", "Velocity" })
        foreach (var streams in new[] { Array.Empty<string>(), new[] { "UV" }, new[] { "Position", "Color" } })
        for (int camera = 0; camera < 3; camera++)
        {
            foreach (var rig in new[] { old, current })
            {
                rig.Configure(simulation, scaling, mode, alignment, camera, cases);
                rig.EnableCustomChannels(); rig.Streams(streams);
                if (cases % 2 == 0) rig.AnimatedStreams();
            }
            for (int draw = 0; draw < 2; draw++)
            {
                if (draw > 0) foreach (var rig in new[] { old, current }) rig.MoveSortRig(draw);
                var expected = old.Geometry(camera, cases);
                current.Geometry(camera, cases, packed: true);
                var actual = current.PackedChannels();
                foreach (string channel in actual.Keys)
                    Check(ChannelBytes(channel, expected[channel]).SequenceEqual(ChannelBytes(channel, actual[channel])), $"packed geometry differs {cases}/{draw}/{channel}");
                Check(current.EmptyLegacyChannels(), "packed path also wrote legacy channels");
            }
            cases++;
        }
        Console.WriteLine($"PASS {cases} configurations x2 draws, bytewise geometry/streams/index equivalence; legacy channels stay empty.");
        current.CheckPackedEligibility();
        foreach (int particles in new[] { 0, 1, 64, 2944, 8192 })
        foreach (int chunks in new[] { 1, 56 }.Where(n => n <= Math.Max(1, particles)))
        {
            foreach (var rig in new[] { old, current }) { rig.Configure("Local", "Hierarchy", "Billboard", "View", 1, 1); rig.Streams(); }
            var left = new Mesh { indexFormat = IndexFormat.UInt32 };
            var right = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                // Empty input has its own production submission check; this builder requires >=1 particle.
                if (particles == 0) { Check(left.vertexCount == right.vertexCount, "empty meshes differ"); continue; }
                var slow = old.BuildGeometryBatch(particles, chunks, page: left);
                var fast = current.BuildGeometryBatch(particles, chunks, packed: true, page: right);
                slow(); fast();
                CompareMeshes(left, right);
                for (int i = 0; i < 100; i++) { slow(); fast(); }
                for (int run = 0; run < 3; run++) foreach (bool optimized in new[] { run % 2 == 0, run % 2 != 0 })
                {
                    var action = optimized ? fast : slow;
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 150; i++) action();
                    long ticks = Stopwatch.GetTimestamp() - start;
                    Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "packed pipeline allocates after warmup");
                    Console.WriteLine($"Packed CPU run {run + 1}, particles={particles}, chunks={chunks}, optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 150:F6} ms, 0 B; geometry + page append + native CPU packing, excludes GPU/driver.");
                }
            }
            finally { Object.DestroyImmediate(left); Object.DestroyImmediate(right); }
        }
    }

    private static void CompareMeshes(Mesh left, Mesh right)
    {
        Check(ChannelBytes("vertices", left.vertices.Cast<object>().ToArray()).SequenceEqual(ChannelBytes("vertices", right.vertices.Cast<object>().ToArray())), "page positions differ");
        Check(left.GetIndices(0).SequenceEqual(right.GetIndices(0)), "page indices differ");
        Check(left.bounds.Equals(right.bounds), "page bounds differ");
        foreach (int channel in new[] { 0, 1, 2 })
        {
            var a = new List<Vector4>(); var b = new List<Vector4>(); left.GetUVs(channel, a); right.GetUVs(channel, b);
            Check(ChannelBytes("custom1", a.Cast<object>().ToArray()).SequenceEqual(ChannelBytes("custom1", b.Cast<object>().ToArray())), "page uv differs");
        }
    }

    private sealed partial class Rig
    {
        internal bool EmptyLegacyChannels() => new[] { "vertices", "uv", "normals", "tangents", "colors", "packedUV", "custom1", "custom2" }
            .All(name => ((ICollection)Get(Renderer, name)).Count == 0);

        internal Dictionary<string, object[]> PackedChannels()
        {
            var data = ((IEnumerable)Get(Renderer, "billboardVertices")).Cast<object>().ToArray();
            var result = new Dictionary<string, object[]> { ["indices"] = ((IEnumerable)Get(Renderer, "indices")).Cast<object>().ToArray() };
            foreach (var channel in new[] { ("vertices", "Position"), ("normals", "Normal"), ("tangents", "Tangent"),
                ("colors", "Color"), ("packedUV", "UV0"), ("custom1", "UV1"), ("custom2", "UV2") })
                result[channel.Item1] = data.Select(v => v.GetType().GetField(channel.Item2, Flags).GetValue(v)).ToArray();
            return result;
        }

        internal void CheckPackedEligibility()
        {
            bool Eligible() => (bool)renderer.GetMethod("CanPackBillboards", Flags).Invoke(Renderer, new[] { System });
            Configure("World", "Hierarchy", "Billboard", "View", 1, 1); Streams();
            Check(Eligible(), "default billboard not eligible");
            Streams("UV", "UV2"); Check(!Eligible(), "general streams packed");
            Streams(); SetEnum(Renderer, "renderMode", "Mesh"); Check(!Eligible(), "mesh packed");
            SetEnum(Renderer, "renderMode", "Billboard");
            var module = Get(System, "textureSheetAnimation"); Set(module, "enabled", true); SetEnum(module, "mode", "Sprites");
            Check(!Eligible(), "sprite batches packed");
            Set(module, "enabled", false);
            foreach (string name in new[] { "uiEntry", "cameraUiEntry" })
            {
                var field = renderer.GetField(name, Flags);
                field.SetValue(Renderer, global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(field.FieldType));
                try { Check(!Eligible(), "UI path packed: " + name); }
                finally { field.SetValue(Renderer, null); }
            }
            var capture = renderer.Assembly.GetType("RimUnity.Diagnostics.ParticleDebugWindow").GetField("Capturing", BindingFlags.Static | BindingFlags.NonPublic);
            capture.SetValue(null, true);
            try { Check(!Eligible(), "diagnostic capture packed"); }
            finally { capture.SetValue(null, false); }
            renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            Check(!(bool)Get(Renderer, "packedBillboards") && ((ICollection)Get(Renderer, "billboardVertices")).Count == 0, "ClearMesh retained packed state");
            Console.WriteLine("PASS packed eligibility and reset checks.");
        }
    }
}
