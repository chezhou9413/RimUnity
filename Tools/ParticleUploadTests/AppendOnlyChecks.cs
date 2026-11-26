using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RimUnity;
using Mesh = RimUnity.Mesh;

internal static class AppendOnlyChecks
{
    private static int checks;
    private sealed class Data
    {
        internal readonly List<Vector3> Positions = new(), Normals = new();
        internal readonly List<Vector4> Tangents = new(), UV0 = new(), UV1 = new(), UV2 = new();
        internal readonly List<Color32> Colors = new();
        internal readonly List<int> Indices = new();
        internal Data(int quads, int salt)
        {
            for (int p = 0; p < quads; p++)
            {
                for (int v = 0; v < 4; v++)
                {
                    Positions.Add(new(salt + v * .3f, p * .2f - v, (p % 7) * .7f));
                    Normals.Add(new(v * .1f, .2f, 1)); Tangents.Add(new(1, 0, .1f, v % 2 == 0 ? 1 : -1));
                    Colors.Add(new((byte)(salt + v), (byte)(30 + p), 170, 210));
                    UV0.Add(new(v, salt, .3f, .7f)); UV1.Add(new(p, v, 2, 3)); UV2.Add(new(-v, -p, 4, 5));
                }
                int start = p * 4;
                Indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
        }
        internal void Append(Mesh mesh, int range = -1)
        {
            if (range < 0) mesh.AppendParticleGeometry(Positions, Normals, Tangents, Colors, UV0, UV1, UV2, Indices);
            else mesh.AppendParticleGeometry(Positions, Normals, Tangents, Colors, UV0, UV1, UV2, Indices, range);
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        Console.WriteLine($"Core MVID {typeof(Mesh).Module.ModuleVersionId}; tiered compilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var mesh = new Mesh();
        try
        {
            foreach (int mode in new[] { 0, 1, 2 }) foreach (int quads in new[] { 0, 1, 2, 17, 64, 1024 })
            {
                mesh.Clear();
                if (mode == 0) mesh.subMeshCount = 3;
                for (int s = 0; s < 4; s++)
                {
                    // Empty appends do not create a range.
                    var data = new Data(quads, s);
                    data.Append(mesh, mode == 0 ? -1 : quads == 0 ? 0 : mode == 1 ? s : s / 2);
                    digest.AppendData(Snapshot(mesh)); checks++;
                }
                var extra = new Data(1, 10); extra.Append(mesh, mode == 0 ? -1 : mesh.subMeshCount);
                var copied = Snapshot(mesh);
                extra.Positions[0] = new(9999, 9999, 9999); extra.UV0[0] = default; extra.Indices[0] = 3;
                Check(copied.SequenceEqual(Snapshot(mesh)), "source buffers must not alias the mesh");
                digest.AppendData(copied);
            }
            foreach (float edge in new[] { -0f, float.Epsilon, -float.Epsilon, float.PositiveInfinity, float.NegativeInfinity, BitConverter.Int32BitsToSingle(unchecked((int)0xffc00123)) })
            {
                mesh.Clear(); var data = new Data(2, 0); data.Positions[0] = new(edge, 0, -edge);
                data.Append(mesh, 0); digest.AppendData(Snapshot(mesh)); checks++;
            }
            mesh.Clear(); new Data(1, 0).Append(mesh, 0);
            foreach (int failure in Enumerable.Range(0, 6))
            {
                var data = new Data(1, 1);
                if (failure == 0) data.Indices[0] = -1;
                if (failure == 1) data.Indices[0] = data.Positions.Count;
                if (failure == 2) data.Indices.RemoveAt(0);
                if (failure == 3) data.Normals.Clear();
                var before = Snapshot(mesh); bool rejected = false;
                try
                {
                    if (failure == 4) mesh.AppendParticleGeometry(data.Positions, data.Normals, data.Tangents, data.Colors, data.UV0, data.UV1, data.UV2, data.Indices, -1);
                    else data.Append(mesh, failure == 5 ? 3 : 0);
                }
                catch (ArgumentException) { rejected = true; }
                Check(rejected && before.SequenceEqual(Snapshot(mesh)), "invalid input must not mutate mesh data");
            }
            foreach (int invalidIndex in new[] { 0, 101 })
            foreach (int value in new[] { -1, 68 })
            {
                var data = new Data(17, 1);
                data.Indices[invalidIndex] = value;
                var before = Snapshot(mesh);
                bool rejected = false;
                try { data.Append(mesh, 0); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected && before.SequenceEqual(Snapshot(mesh)),
                    "both vector and tail validation must reject out of range indices before mutation");
            }
            Console.WriteLine($"APPEND DIGEST {Convert.ToHexString(digest.GetHashAndReset())}");
            foreach (int quads in new[] { 800, 3000, 5400 }) Benchmark(mesh, quads);
            Console.WriteLine($"PASS {checks} real Core append checks: channels, offsets, ranges/bounds, clear/reuse, numeric edges, invalid input, source independence and warmed allocations. No GPU.");
        }
        finally { RimUnity.Object.DestroyImmediate(mesh); }
    }
    private static void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; }
    private static byte[] Snapshot(Mesh mesh)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Values<T>(T[] values) where T : unmanaged => hash.AppendData(MemoryMarshal.AsBytes(values.AsSpan()));
        Values(new[] { mesh.vertexCount, mesh.subMeshCount });
        Values(mesh.vertices); Values(mesh.normals); Values(mesh.tangents); Values(mesh.colors32);
        var uv = new List<Vector4>();
        for (int i = 0; i < 3; i++) { mesh.GetUVs(i, uv); Values(uv.ToArray()); }
        for (int i = 0; i < mesh.subMeshCount; i++) Values(mesh.GetIndices(i));
        Values(new[] { mesh.bounds });
        Values(((List<Bounds>)typeof(Mesh).GetField("particleSubmeshBounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mesh)).ToArray());
        return hash.GetHashAndReset();
    }
    private static void Benchmark(Mesh mesh, int quads)
    {
        var data = Enumerable.Range(0, 56).Select(s => new Data(quads / 56 + (s < quads % 56 ? 1 : 0), s)).ToArray();
        void Frame() { mesh.Clear(); for (int s = 0; s < data.Length; s++) data[s].Append(mesh, s * 48 / 56); }
        for (int i = 0; i < 100; i++) Frame();
        for (int run = 0; run < 3; run++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 200; i++) Frame();
            long ticks = Stopwatch.GetTimestamp() - start;
            Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "append allocates after warmup");
            Console.WriteLine($"Append CPU run {run + 1}, {quads * 4} vertices / 56 inputs / 48 ranges: {ticks * 1000d / Stopwatch.Frequency / 200:F6} ms, 0 B; real Core CPU data only, no upload/GPU.");
        }
    }
}
