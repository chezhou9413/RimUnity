using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using RimUnity;

internal static partial class Program
{
    private static void CheckMeshDirections(string baseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("mesh direction baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(baseline));
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        Check(old.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId != typeof(ParticleSystem).Module.ModuleVersionId, "mesh baseline must differ");
        int cases = 0;
        foreach (int count in new[] { 0, 1, 2, 3, 4, 32, 129 })
        foreach (int pattern in Enumerable.Range(0, 10))
        foreach (var layout in new[] { Array.Empty<string>(), new[] { "UV" }, new[] { "Position", "Normal", "Color", "Tangent" },
            new[] { "UV", "UV2", "UV3", "UV4", "VertexID" }, new[] { "UV", "Velocity", "Center", "SizeXYZ", "Speed" } })
        {
            foreach (var rig in new[] { old, current })
            {
                rig.Configure(new[] { "Local", "World", "Custom" }[cases % 3], new[] { "Hierarchy", "Local", "Shape" }[cases / 3 % 3],
                    "Mesh", new[] { "View", "World", "Local", "Facing", "Velocity" }[cases % 5], cases % 3, cases);
                rig.Streams(layout); rig.EnableCustomChannels(); rig.AnimatedStreams(); rig.MeshDirections(count, pattern);
            }
            for (int draw = 0; draw < 3; draw++)
            {
                if (draw > 0) foreach (var rig in new[] { old, current }) { rig.MoveSortRig(draw); rig.MeshDirections(count, (pattern + draw) % 10); }
                var expected = old.Geometry(cases % 3, cases); var actual = current.Geometry(cases % 3, cases);
                foreach (string channel in expected.Keys)
                    Check(ChannelBytes(channel, expected[channel]).SequenceEqual(ChannelBytes(channel, actual[channel])), $"mesh direction bits differ: {cases}/{draw}/{count}/{pattern}/{channel}");
            }
            cases++;
        }
        Console.WriteLine($"PASS {cases} mesh direction cases x3 draws x9 bytewise channels; missing/partial/uniform/varying directions, tangent handedness, signed zero/NaN, zero/negative scale, animation, events, mesh replacement and changed transforms. No GPU.");
        var oldChoices = old.MeshAlternatingChannels(); var newChoices = current.MeshAlternatingChannels();
        for (int i = 0; i < oldChoices.Count; i++) foreach (string channel in oldChoices[i].Keys)
            Check(ChannelBytes(channel, oldChoices[i][channel]).SequenceEqual(ChannelBytes(channel, newChoices[i][channel])), $"cached mesh switch differs: {i}/{channel}");
        Console.WriteLine("PASS multiple cached mesh choices refresh direction metadata and preserve tangent handedness.");
        foreach (var layout in new[] { Array.Empty<string>(), new[] { "UV" }, new[] { "Position", "Color" }, new[] { "UV", "VertexID" } })
        foreach (int count in new[] { 4, 32, 128 })
        foreach (int pattern in new[] { 0, 1, 2, 5, 9 })
        {
            foreach (var rig in new[] { old, current }) { rig.Configure("Local", "Hierarchy", "Mesh", "View", 1, 1); rig.MeshDirections(count, pattern); rig.Streams(layout); }
            var slow = old.BuildGeometryBatch(256, 4); var fast = current.BuildGeometryBatch(256, 4);
            for (int i = 0; i < 1000; i++) { slow(); fast(); }
            for (int run = 0; run < 3; run++) foreach (bool optimized in new[] { run % 2 == 0, run % 2 != 0 })
            {
                var action = optimized ? fast : slow;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100; i++) action();
                long ticks = Stopwatch.GetTimestamp() - start;
                Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "mesh directions allocate after warmup");
                Console.WriteLine($"Mesh CPU run {run + 1}, vertices={count}, pattern={pattern}, layout=[{string.Join('+', layout)}], optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 100:F6} ms per 256 particles/4 chunks; 0 B, CPU geometry only, no upload/GPU.");
            }
        }
    }
    private static byte[] ChannelBytes(string channel, object[] values) => channel switch {
        "vertices" or "normals" => Bytes<Vector3>(values), "uv" => Bytes<Vector2>(values),
        "indices" => Bytes<int>(values), "colors" => Bytes<Color32>(values), _ => Bytes<Vector4>(values)
    };
    private static byte[] Bytes<T>(object[] values) where T : unmanaged => MemoryMarshal.AsBytes(values.Cast<T>().ToArray().AsSpan()).ToArray();

    private sealed partial class Rig
    {
        internal List<Dictionary<string, object[]>> MeshAlternatingChannels()
        {
            Configure("Local", "Hierarchy", "Mesh", "View", 1, 1); MeshDirections(4, 1);
            var alternate = new Mesh { vertices = mesh.vertices, normals = new[] { Vector3.right },
                tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(0, 1, 0, 1) }, uv = mesh.uv, triangles = mesh.triangles };
            try
            {
                renderer.GetMethod("SetMeshes", new[] { typeof(Mesh[]) }).Invoke(Renderer, new object[] { new[] { mesh, alternate } });
                var output = new List<Dictionary<string, object[]>>();
                var cam = Activator.CreateInstance(cameraState, Flags, null, new[] { Camera, null, (object)true }, null);
                var space = Activator.CreateInstance(drawSpace, Flags, null, new[] { System }, null);
                foreach (int choice in new[] { 0, 1, 0, 1 })
                {
                    var p = Particle(1, false); Set(p, "meshIndexOverride", choice);
                    renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
                    renderer.GetMethod("AppendParticle", Flags).Invoke(Renderer, new[] { System, p, cam, space });
                    output.Add(Channels());
                }
                return output;
            }
            finally { Set(Renderer, "mesh", mesh); RimUnity.Object.DestroyImmediate(alternate); }
        }

        internal void MeshDirections(int count, int pattern)
        {
            mesh.vertices = Enumerable.Range(0, count).Select(i => new Vector3(i * .1f, i % 3 - 1, i % 5 * .2f)).ToArray();
            int length = pattern == 0 ? 0 : pattern == 3 ? count / 2 : pattern == 4 ? Math.Min(1, count) : pattern == 8 ? count + 2 : count;
            mesh.normals = Enumerable.Range(0, length).Select(i => pattern switch {
                2 => new Vector3(i * .1f, 1, .2f),
                6 => new Vector3(BitConverter.Int32BitsToSingle(i % 2 == 0 ? int.MinValue : 0), 1, 0),
                7 => new Vector3(BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00123) + i % 2), 1, 0),
                8 when i >= count => Vector3.right, _ => Vector3.up
            }).ToArray();
            mesh.tangents = Enumerable.Range(0, pattern == 9 ? 0 : length).Select(i => pattern switch {
                2 => new Vector4(1, i * .03f, .1f, i % 2 == 0 ? 1 : -1),
                5 => new Vector4(1, 0, 0, i % 2 == 0 ? 1 : -1),
                6 => new Vector4(1, BitConverter.Int32BitsToSingle(i % 2 == 0 ? int.MinValue : 0), 0, 1),
                7 => new Vector4(1, float.NaN, 0, 1),
                8 when i >= count => new Vector4(0, 1, 0, 1), _ => new Vector4(1, 0, 0, 1)
            }).ToArray();
            mesh.uv = Enumerable.Range(0, count).Select(i => new Vector2(i % 2, i / 2f)).ToArray();
            mesh.triangles = Enumerable.Range(0, count / 3 * 3).ToArray();
            // Exercise the existing decoded-geometry invalidation contract.
            Set(Renderer, "mesh", mesh);
        }
    }
}
