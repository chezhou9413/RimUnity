using System.Reflection;
using System.Diagnostics;
using RimUnity;

internal static partial class Program
{
    private static void CheckTrailChannelWrites(Rig old, Rig current)
    {
        var layouts = new List<string[]> {
            Array.Empty<string>(), new[] { "Position", "Normal", "Color", "Tangent" },
            new[] { "UV" }, new[] { "Color", "UV", "Normal", "Position", "Color" },
            new[] { "UV", "UV" }, new[] { "UV", "UV2", "UV3", "UV4", "VertexID" },
            new[] { "Custom1XYZW", "Custom2XYZW", "VertexID" },
            new[] { "UV", "Velocity", "Center", "SizeXYZ", "Speed" }
        };
        var streamType = current.Renderer.GetType().GetProperty("vertexStreams").PropertyType.GetElementType();
        foreach (string name in Enum.GetNames(streamType))
        {
            try { current.Streams(name); layouts.Add(new[] { name }); }
            catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException) { }
        }
        int cases = 0;
        foreach (var layout in layouts)
        foreach (bool ribbon in new[] { false, true })
        foreach (string texture in new[] { "Stretch", "Tile", "DistributePerSegment", "RepeatPerSegment", "Static" })
        foreach (int count in new[] { 0, 1, 2, 3, 17, 65, 257 })
        {
            // Draw particle geometry first so stale per-particle stream fields are nonzero.
            // Trail ClearMesh must preserve its established fallback contract for all layouts.
            foreach (var rig in new[] { old, current })
            {
                rig.Configure("Local", "Hierarchy", "Billboard", "View", 1, cases);
                rig.Streams(layout); rig.AnimatedStreams(); rig.Geometry(1, cases);
            }
            var left = old.Trail(count, texture, ribbon, cases % 3, cases);
            var right = current.Trail(count, texture, ribbon, cases % 3, cases);
            var expected = old.TrailChannels(left, ribbon);
            var actual = current.TrailChannels(right, ribbon);
            Compare(expected, actual, "clear");
            // Append behind existing channels, with a second camera direction, then clear/reuse.
            expected = old.TrailChannels(left, ribbon, new Vector3(.3f, -.7f, .2f), false);
            actual = current.TrailChannels(right, ribbon, new Vector3(.3f, -.7f, .2f), false);
            Compare(expected, actual, "nonempty");
            foreach (var rig in new[] { old, current })
            {
                rig.Root.transform.localPosition += new Vector3(3, -2, 1);
                rig.Root.transform.localRotation = Quaternion.Euler(21, 16, -35);
                rig.Custom.transform.localPosition -= Vector3.one;
            }
            expected = old.TrailChannels(left, ribbon, Vector3.zero);
            actual = current.TrailChannels(right, ribbon, Vector3.zero);
            Compare(expected, actual, "degenerate/reuse");
            cases++;

            void Compare(Dictionary<string, object[]> a, Dictionary<string, object[]> b, string step)
            {
                foreach (string channel in a.Keys)
                    Check(ChannelBytes(channel, a[channel]).SequenceEqual(ChannelBytes(channel, b[channel])), $"trail channels {step}/{cases}/{string.Join('+', layout)}/{texture}/{ribbon}/{count}: {channel}");
            }
        }
        Console.WriteLine($"PASS {cases} trail stream/growth cases x 3 draws, all supported streams, clear/nonempty append, changed camera direction and degenerate sides; nine channels match the old DLL byte for byte. No GPU.");
    }

    private static void BenchmarkTrailSpaces(Rig old, Rig current)
    {
        foreach (var layout in new[] { Array.Empty<string>(), new[] { "UV" }, new[] { "Position", "Color" }, new[] { "UV", "VertexID" } })
        foreach (var (simulation, scaling, space, ribbon) in new[] {
            ("Local", "Hierarchy", 0, false), ("Local", "Local", 0, false), ("Local", "Shape", 0, false),
            ("Custom", "Hierarchy", 0, false), ("World", "Hierarchy", 0, false),
            ("Local", "Hierarchy", 1, false), ("Local", "Hierarchy", 2, false), ("Local", "Hierarchy", 0, true) })
        {
            foreach (var rig in new[] { old, current })
            {
                rig.Configure(simulation, scaling, "Billboard", "View", 1, 1);
                rig.Streams(layout);
            }
            var slow = old.TrailBatch(old.Trail(17, "Stretch", ribbon, space, 1), ribbon);
            var fast = current.TrailBatch(current.Trail(17, "Stretch", ribbon, space, 1), ribbon);
            for (int i = 0; i < 60; i++) { slow(); fast(); }
            for (int run = 0; run < 3; run++)
            foreach (bool optimized in new[] { run % 2 == 0, run % 2 != 0 })
            {
                var action = optimized ? fast : slow;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100; i++) action();
                long ticks = Stopwatch.GetTimestamp() - start;
                Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "trail geometry allocates after warmup");
                Console.WriteLine($"Trail CPU run {run + 1}, {simulation}/{scaling}/space{space}/ribbon{ribbon}/layout[{string.Join('+', layout)}], optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 100:F6} ms per 256 trails x 17 points, 0 B; synthetic geometry only, per-draw snapshot setup included.");
            }
        }
    }
}
