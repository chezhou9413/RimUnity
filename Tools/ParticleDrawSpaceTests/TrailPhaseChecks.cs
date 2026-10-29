using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RimUnity;

internal static partial class Program
{
    private readonly record struct TrailQuad(Vector3 A, Vector3 B, Vector3 C, Vector3 D, float U0, float U1, Color32 C0, Color32 C1);
    private static void MeasureTrailPhases(string destination)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        Check(typeof(ParticleSystem).Assembly.GetType("RimUnity.Diagnostics.LabParticleDetail") == null,
            "trail phase benchmark requires normal Release");
        var rows = new List<object>();
        foreach (string[] layout in new[] { Array.Empty<string>(), new[] { "UV", "VertexID" } })
        foreach (bool ribbon in new[] { false, true })
        foreach (int points in new[] { 2, 17, 33 })
        {
            using var full = new Rig(typeof(ParticleSystem).Assembly);
            using var writes = new Rig(typeof(ParticleSystem).Assembly);
            foreach (var rig in new[] { full, writes })
            { rig.Configure("Local", "Hierarchy", "Billboard", "View", 1, 1); rig.Streams(layout); }
            var trail = full.Trail(points, "Stretch", ribbon, 0, 1);
            var channels = full.TrailChannels(trail, ribbon);
            var quads = new TrailQuad[points - 1];
            for (int i = 0; i < quads.Length; i++)
            {
                int v = i * 4;
                quads[i] = new((Vector3)channels["vertices"][v], (Vector3)channels["vertices"][v + 1],
                    (Vector3)channels["vertices"][v + 2], (Vector3)channels["vertices"][v + 3],
                    ((Vector2)channels["uv"][v]).x, ((Vector2)channels["uv"][v + 2]).x,
                    (Color32)channels["colors"][v], (Color32)channels["colors"][v + 2]);
            }
            var type = writes.Renderer.GetType();
            var clear = type.GetMethod("ClearMesh", Flags).CreateDelegate<Action>(writes.Renderer);
            var write = type.GetMethod("AppendTrailQuad", Flags).CreateDelegate<Action<Vector3, Vector3, Vector3, Vector3, float, float, Color32, Color32>>(writes.Renderer);
            var indices = type.GetMethod("Quad", Flags).CreateDelegate<Action<int>>(writes.Renderer);
            Action replay = () =>
            {
                clear(); int vertex = 0;
                for (int copy = 0; copy < 256; copy++) foreach (var q in quads)
                { write(q.A, q.B, q.C, q.D, q.U0, q.U1, q.C0, q.C1); indices(vertex); vertex += 4; }
            };
            var total = full.TrailBatch(trail, ribbon);
            total(); replay();
            foreach (string name in channels.Keys)
            {
                var expected = ((System.Collections.IEnumerable)full.Renderer.GetType().GetField(name, Flags).GetValue(full.Renderer)).Cast<object>().ToArray();
                var actual = ((System.Collections.IEnumerable)type.GetField(name, Flags).GetValue(writes.Renderer)).Cast<object>().ToArray();
                Check(expected.SequenceEqual(actual), "trail write replay differs in " + name);
            }
            for (int i = 0; i < 100; i++) { total(); replay(); }
            var totals = new List<double>(); var copies = new List<double>();
            for (int round = 0; round < 6; round++)
                foreach (bool onlyWrites in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
                {
                    Action action = onlyWrites ? replay : total;
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 100; i++) action();
                    double ms = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / 100;
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    Check(allocated == 0, $"trail benchmark allocated {allocated} B after warmup; layout={string.Join('+', layout)}, ribbon={ribbon}, points={points}, replay={onlyWrites}");
                    (onlyWrites ? copies : totals).Add(ms);
                }
            bool sameWritePath = type.GetMethod("AppendTrailIndices", Flags) == null
                && (layout.Length != 0 || type.GetMethod("ReserveTrail", Flags) == null);
            rows.Add(new { Layout = layout, Ribbon = ribbon, Points = points, TrailsPerBatch = 256,
                FullMs = totals.Average(), WriteReplayMs = copies.Average(), FullRunsMs = totals, WriteRunsMs = copies,
                SameWritePath = sameWritePath,
                EstimatedOtherMs = sameWritePath ? (double?)(totals.Average() - copies.Average()) : null });
        }
        const string caveats = "Normal Release, CPU only, no per-segment timers. Full is real AppendTrail including coordinates, curves, segment construction and writes. Replay uses precomputed quad inputs with legacy AppendTrailQuad/Quad, checked against all nine output channels. Full may use whole-trail reservation while replay still uses per-quad writes; when paths differ EstimatedOtherMs is unavailable. Otherwise subtraction includes computation/loop/call-shape differences and is not a directly measured pure-computation stage or an optimization performance baseline. Does not measure trail simulation/aging, GPU, or whole game.";
        File.WriteAllText(destination, JsonSerializer.Serialize(new { Rows = rows, Caveats = caveats }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS {rows.Count} trail phase configurations: exact write replay, alternating warmed measurements, zero steady allocation. {destination}");
    }
}
