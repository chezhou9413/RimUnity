using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using RimUnity;
using Mesh = RimUnity.Mesh;
using Object = RimUnity.Object;

// Uses the real Core and native CPU storage; no GPU or game/save needed.
internal static class PackedParticleChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type Vertex = typeof(Mesh).Assembly.GetType("RimUnity.ParticleVertex", true);
    private static int checks;
    private delegate bool Resolve(Waylith.Graphics.Meshes.Mesh mesh, int submesh, out Waylith.Rendering.MeshDrawGeometry geometry, out string failure);
    private sealed class Data
    {
        internal readonly List<Vector3> P = new(), N = new();
        internal readonly List<Vector4> T = new(), U = new(), A = new(), B = new();
        internal readonly List<Color32> C = new();
        internal readonly List<int> I = new();
        internal readonly IList Packed;
        internal readonly Action<Mesh, int> AppendPacked;
        internal Data(int quads, float edge = 0)
        {
            Packed = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Vertex));
            for (int q = 0; q < quads; q++)
            {
                for (int v = 0; v < 4; v++)
                {
                    P.Add(new(q * .3f, v == 0 ? edge : v, -v)); N.Add(new(0, v * .2f, 1));
                    T.Add(new(1, 0, .3f, v % 2 == 0 ? -1 : 1)); C.Add(new((byte)q, 120, (byte)v, 200));
                    U.Add(new(v, edge, .3f, .7f)); A.Add(new(v, q, -0f, 1)); B.Add(new(-v, -q, edge, 0));
                    var item = Activator.CreateInstance(Vertex);
                    foreach (var pair in new[] { ("Position", (object)P[^1]), ("Normal", N[^1]), ("Tangent", T[^1]),
                        ("Color", C[^1]), ("UV0", U[^1]), ("UV1", A[^1]), ("UV2", B[^1]) })
                        Vertex.GetField(pair.Item1, Flags).SetValue(item, pair.Item2);
                    Packed.Add(item);
                }
                int i = q * 4; I.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            var mesh = Expression.Parameter(typeof(Mesh)); var range = Expression.Parameter(typeof(int));
            var append = typeof(Mesh).GetMethods(Flags).Single(m => m.Name == "AppendParticleGeometry");
            var args = append.GetParameters().Take(7).Select(p => (Expression)Expression.Constant(null, p.ParameterType))
                .Concat(new Expression[] { Expression.Constant(I), range, Expression.Constant(Packed, Packed.GetType()) });
            AppendPacked = Expression.Lambda<Action<Mesh, int>>(Expression.Call(mesh, append, args), mesh, range).Compile();
        }
        internal void Append(Mesh mesh, int range) => mesh.AppendParticleGeometry(P, N, T, C, U, A, B, I, range);
    }

    internal static void Run()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        Check(Marshal.SizeOf(Vertex) == 92, "particle contract size");
        foreach (int quads in new[] { 0, 1, 17, 1024 })
        foreach (int pattern in new[] { 0, 1, 2, 3 })
        foreach (float edge in new[] { 0f, -0f, float.Epsilon, float.PositiveInfinity, float.NegativeInfinity,
            BitConverter.Int32BitsToSingle(unchecked((int)0xffc00123)) })
        {
            var data = new Data(quads, edge); var expected = new Mesh { indexFormat = IndexFormat.UInt32 }; var actual = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    int range = quads == 0 ? 0 : i / 2;
                    data.Append(expected, range);
                    if ((pattern & 1 << i % 2) != 0) data.Append(actual, range); else data.AppendPacked(actual, range);
                    Equal(expected, actual);
                }
                // Caller scratch can be changed or cleared after synchronous append.
                var before = NativeBytes(actual);
                data.Packed.Clear(); data.P.Clear(); data.I.Clear();
                Check(before.SequenceEqual(NativeBytes(actual)), "caller buffers alias owned mesh");
                expected.Clear(); actual.Clear(); Equal(expected, actual);
            }
            finally { Object.DestroyImmediate(expected); Object.DestroyImmediate(actual); }
        }
        foreach (int operation in Enumerable.Range(0, 18))
        {
            var data = new Data(3); var expected = new Mesh { indexFormat = IndexFormat.UInt32 }; var actual = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                data.Append(expected, 0); data.AppendPacked(actual, 0);
                data.Append(expected, 1); data.AppendPacked(actual, 1);
                Modify(expected, operation); Modify(actual, operation); Equal(expected, actual);
                if (operation == 17)
                {
                    var edges = typeof(Mesh).GetMethod("TriangleEdges", Flags);
                    var a = (Mesh)edges.Invoke(expected, null); var b = (Mesh)edges.Invoke(actual, null); Equal(a, b);
                }
                Check(expected.vertices.SequenceEqual(actual.vertices) && expected.normals.SequenceEqual(actual.normals)
                    && expected.colors32.SequenceEqual(actual.colors32) && expected.tangents.SequenceEqual(actual.tangents), "general channel snapshots differ");
                // Readback may materialize channels, then append must remain correct.
                if (operation == 16) { expected.SetUVs(7, new List<Vector4>()); actual.SetUVs(7, new List<Vector4>()); }
                data.Append(expected, expected.subMeshCount); data.AppendPacked(actual, actual.subMeshCount); Equal(expected, actual);
            }
            finally { Object.DestroyImmediate(expected); Object.DestroyImmediate(actual); }
        }
        CheckInvalidInputs();
        Console.WriteLine($"PASS {checks} packed page checks: exact native vertex/index bytes, ranges/bounds, mixed legacy/packed order, zero/large/nonfinite input, ownership, readback/edit/reappend, mirror, triangle edges, invalid input and Clear.");
    }

    // Run this same harness in separate processes against old/current Core.
    // Include native upload bytes and global/submesh bounds in the digest.
    internal static void Snapshot(string path)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        int samples = 0;
        foreach (int quads in new[] { 0, 1, 17, 1024 })
        foreach (int pattern in new[] { 0, 1, 2, 3 })
        foreach (float edge in new[] { 0f, -0f, float.Epsilon, float.PositiveInfinity, float.NegativeInfinity,
            BitConverter.Int32BitsToSingle(unchecked((int)0xffc00123)) })
        {
            var data = new Data(quads, edge); var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                for (int step = 0; step < 4; step++)
                {
                    int range = quads == 0 ? 0 : step / 2;
                    if ((pattern & 1 << step % 2) != 0) data.Append(mesh, range); else data.AppendPacked(mesh, range);
                    Capture();
                }
                // Public readback changes canonical storage before the next append.
                _ = mesh.vertices;
                data.Append(mesh, quads == 0 ? 0 : mesh.subMeshCount); Capture();
                mesh.Clear(); data.AppendPacked(mesh, 0); data.Append(mesh, 0); Capture();
            }
            finally { Object.DestroyImmediate(mesh); }
            void Capture()
            {
                byte[] bytes = NativeBytes(mesh);
                hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes); samples++;
            }
        }
        File.WriteAllText(path, Convert.ToHexString(hash.GetHashAndReset()));
        Console.WriteLine($"PASS captured {samples} native byte/bounds snapshots for old/current Core comparison.");
    }

    internal static void RunHost(Waylith.EngineApplication app)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        using var factory = new Waylith.Rendering.DrawItemFactory(app.GraphicsDevice);
        var resolve = typeof(Waylith.Rendering.DrawItemFactory).GetMethod("TryResolveMesh", Flags).CreateDelegate<Resolve>(factory);
        var data = Enumerable.Range(0, 56).Select(i => new Data(800 / 56 + (i < 800 % 56 ? 1 : 0))).ToArray();
        var page = new Mesh { indexFormat = IndexFormat.UInt32 };
        try
        {
            for (int frame = 0; frame < 24; frame++)
            {
                var uploads = app.GraphicsDevice.MeshUploads;
                uploads.BeginRenderFrame(); uploads.BeginFrame(0);
                page.Clear();
                for (int i = 0; i < data.Length; i++)
                {
                    if (frame % 3 == 0 || i % 2 == 0) data[i].AppendPacked(page, i * 48 / 56);
                    else data[i].Append(page, i * 48 / 56);
                }
                var native = page.EngineMesh;
                uploads.Enqueue(native); uploads.DrainPendingUploads();
                for (int range = 0; range < 48; range++)
                    Check(resolve(native, range, out _, out string failure), "packed host resolution failed: " + failure);
            }
            Console.WriteLine("PASS packed/mixed real host upload and 48 ordered range resolutions over 24 frames; native CPU data consumed by GPU uploader, no game/save loaded.");
        }
        finally { Object.DestroyImmediate(page); }
    }

    internal static void Benchmark()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var data = Enumerable.Range(0, 56).Select(_ => new Data(52)).ToArray();
        var expected = new Mesh { indexFormat = IndexFormat.UInt32 }; var actual = new Mesh { indexFormat = IndexFormat.UInt32 };
        try
        {
            foreach (int pattern in new[] { 0, 1, 2 })
            {
                void Frame(bool packed)
                {
                    var mesh = packed ? actual : expected; mesh.Clear();
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (packed && (pattern == 0 || pattern == 1 && i % 2 == 0 || pattern == 2 && i == 0)) data[i].AppendPacked(mesh, i * 48 / 56);
                        else data[i].Append(mesh, i * 48 / 56);
                    }
                    _ = mesh.EngineMesh;
                }
                Frame(false); Frame(true); Equal(expected, actual);
                // Allow tiered JIT to finish before timing a newly exercised path.
                for (int i = 0; i < 1000; i++) { Frame(false); Frame(true); }
                for (int run = 0; run < 3; run++) foreach (bool packed in new[] { run % 2 == 0, run % 2 != 0 })
                {
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 300; i++) Frame(packed);
                    long ticks = Stopwatch.GetTimestamp() - start;
                    Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "mixed page benchmark allocates after warmup");
                    Console.WriteLine($"Packed batch run {run + 1}, pattern={pattern}, optimized={packed}: {ticks * 1000d / Stopwatch.Frequency / 300:F6} ms; 2912 quads / 56 inputs / 48 ranges; append + native CPU packing only, 0 B.");
                }
            }
        }
        finally { Object.DestroyImmediate(expected); Object.DestroyImmediate(actual); }
    }

    private static void Modify(Mesh mesh, int operation)
    {
        switch (operation)
        {
            case 0: _ = mesh.vertices; break;
            case 1: mesh.SetVertices(mesh.vertices); break;
            case 2: mesh.SetNormals(mesh.normals); break;
            case 3: mesh.SetTangents(mesh.tangents); break;
            case 4: mesh.SetColors(mesh.colors32.ToList()); break;
            case 5: mesh.colors = mesh.colors; break;
            case 6: mesh.uv = mesh.uv; break;
            case 7: var uv = new List<Vector4>(); mesh.GetUVs(1, uv); mesh.SetUVs(1, uv); break;
            case 8: var uv2 = new List<Vector2>(); mesh.GetUVs(2, uv2); mesh.SetUVs(2, uv2); break;
            case 9: mesh.SetTriangles(mesh.GetIndices(0), 0, false); break;
            case 10: mesh.SetTriangles(mesh.GetIndices(0), 0); break;
            case 11: mesh.RecalculateBounds(); break;
            case 12: mesh.RecalculateNormals(); break;
            case 13: mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100); break;
            case 14: mesh.subMeshCount = 1; break;
            case 15: mesh.indexFormat = IndexFormat.UInt16; break;
            case 16: var list = new List<Vector3>(); mesh.GetVertices(list); mesh.SetUVs(7, Enumerable.Repeat(Vector4.one, list.Count).ToList()); break;
        }
    }

    private static void CheckInvalidInputs()
    {
        var mesh = new Mesh(); var data = new Data(1);
        try
        {
            data.AppendPacked(mesh, 0); var before = NativeBytes(mesh);
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var bad = new Data(1);
                if (mode == 0) bad.I[0] = -1;
                if (mode == 1) bad.I.Add(0);
                bool rejected = false;
                try { bad.AppendPacked(mesh, mode == 2 ? 3 : 0); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected && before.SequenceEqual(NativeBytes(mesh)), "invalid input changed page");
            }
            var field = typeof(Mesh).GetField("particleVertices", Flags);
            Check(((ICollection)field.GetValue(mesh)).Count == 4, "packed representation unexpectedly materialized");
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    private static void Equal(Mesh expected, Mesh actual)
    {
        Check(expected.vertexCount == actual.vertexCount && expected.subMeshCount == actual.subMeshCount, "counts differ");
        Check(NativeBytes(expected).SequenceEqual(NativeBytes(actual)), "native upload bytes or bounds differ");
        // Both derived geometry paths must also observe packed positions correctly.
        var mirror = typeof(Mesh).GetMethod("GetDrawMesh", Flags);
        var a = (Waylith.Graphics.Meshes.Mesh)mirror.Invoke(expected, new object[] { true });
        var b = (Waylith.Graphics.Meshes.Mesh)mirror.Invoke(actual, new object[] { true });
        Check(StorageBytes(a.CpuStorage).SequenceEqual(StorageBytes(b.CpuStorage)), "mirror differs");
    }

    private static byte[] NativeBytes(Mesh mesh) => StorageBytes(mesh.EngineMesh.CpuStorage);
    private static unsafe byte[] StorageBytes(Waylith.Graphics.Meshes.CpuMeshData storage)
    {
        using var stream = new MemoryStream();
        stream.Write(MemoryMarshal.AsBytes(new[] { storage.Bounds }.AsSpan()));
        for (int i = 0; i < storage.Layout.StreamCount; i++)
        {
            var buffer = storage.GetVertexStream(i);
            stream.Write(new ReadOnlySpan<byte>((void*)buffer.Pointer, buffer.LengthBytes));
        }
        var ix = storage.GetIndexBuffer(); stream.Write(new ReadOnlySpan<byte>((void*)ix.Pointer, ix.LengthBytes));
        foreach (var range in storage.SubMeshes) stream.Write(MemoryMarshal.AsBytes(new[] { range.Bounds }.AsSpan()));
        return stream.ToArray();
    }
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
}
