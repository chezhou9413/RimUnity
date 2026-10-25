using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Waylith;
using Waylith.Graphics.Meshes;
using Waylith.Rendering;

internal static class Program
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
    private delegate bool Resolve(Mesh mesh, int submesh, out MeshDrawGeometry geometry, out string failure);
    private static string root;
    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            string runtime = Path.Combine(Environment.GetEnvironmentVariable("RIMUNITY_RUNTIME_DIR")
                ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../1.7/Assemblies")), name.Name + ".dll");
            string file = File.Exists(runtime) ? runtime : Path.Combine(root, name.Name + ".dll");
            return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
        };
        SetDllDirectory(root);
        try { if (args.Length > 2 && args[1] == "--packed-snapshot") PackedParticleChecks.Snapshot(Path.GetFullPath(args[2])); else if (args.Length > 1 && args[1] == "--packed-batch-bench") PackedParticleChecks.Benchmark(); else if (args.Length > 1 && args[1] == "--packed-host") RunPackedHost(); else if (args.Length > 1 && args[1] == "--packed-only") PackedParticleChecks.Run(); else if (args.Length > 1 && args[1] == "--append-only") AppendOnlyChecks.Run(); else Run(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SetDllDirectory(null); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunPackedHost()
    {
        using var app = new EngineApplication(new EngineApplicationOptions {
            Title = "RimUnity packed particle regression", HideWindow = true, ShowCrashDialog = false,
            InitialWindowSize = new Brutal.Numerics.int2(64, 64), EnableVideoDecode = false
        });
        PackedParticleChecks.RunHost(app);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        using var app = new EngineApplication(new EngineApplicationOptions {
            Title = "RimUnity particle upload regression", HideWindow = true, ShowCrashDialog = false,
            InitialWindowSize = new Brutal.Numerics.int2(64, 64), EnableVideoDecode = false
        });
        using var factory = new DrawItemFactory(app.GraphicsDevice);
        var resolve = typeof(DrawItemFactory).GetMethod("TryResolveMesh", BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate<Resolve>(factory);
        CheckProductionMesh();
        foreach (int pages in new[] { 60, 1, 60, 1, 60, 1 }) Measure(app, resolve, pages);
        foreach (int pages in new[] { 60, 1, 60, 1, 60, 1 }) MeasureProductionBuild(app, pages);
        Console.WriteLine("PASS real host upload/geometry benchmark; 800 quads, 60 ordered material ranges, no game/save loaded; upload and resolution CPU calls, not GPU execution time.");
    }
    private static void MeasureProductionBuild(EngineApplication app, int pages)
    {
        var positions = new List<RimUnity.Vector3>[60]; var normals = new List<RimUnity.Vector3>[60];
        var tangents = new List<RimUnity.Vector4>[60]; var colors = new List<RimUnity.Color32>[60];
        var uv = new List<RimUnity.Vector4>[60]; var indices = new List<int>[60];
        for (int s = 0; s < 60; s++)
        {
            positions[s] = new(); normals[s] = new(); tangents[s] = new(); colors[s] = new(); uv[s] = new(); indices[s] = new();
            int particles = 800 / 60 + (s < 800 % 60 ? 1 : 0);
            for (int p = 0; p < particles; p++)
            {
                for (int v = 0; v < 4; v++)
                {
                    positions[s].Add(new(s + v, p, 1)); normals[s].Add(new(0, 0, 1)); tangents[s].Add(new(1, 0, 0, 1));
                    colors[s].Add(new(255, 255, 255, 255)); uv[s].Add(new(v, 0, 0, 0));
                }
                indices[s].AddRange(new[] { p * 4, p * 4 + 1, p * 4 + 2, p * 4, p * 4 + 2, p * 4 + 3 });
            }
        }
        var meshes = new RimUnity.Mesh[pages];
        try
        {
            for (int p = 0; p < pages; p++) { meshes[p] = new() { indexFormat = RimUnity.IndexFormat.UInt32 }; meshes[p].MarkDynamic(); }
            long ticks = 0, bytes = 0;
            for (int f = -30; f < 100; f++)
            {
                app.GraphicsDevice.MeshUploads.BeginRenderFrame(); app.GraphicsDevice.MeshUploads.BeginFrame(0);
                long beforeBytes = GC.GetAllocatedBytesForCurrentThread(), before = Stopwatch.GetTimestamp();
                foreach (var mesh in meshes) mesh.Clear();
                for (int s = 0; s < 60; s++)
                {
                    var mesh = meshes[pages == 1 ? 0 : s];
                    if (pages == 1) mesh.AppendParticleGeometry(positions[s], normals[s], tangents[s], colors[s], uv[s], uv[s], uv[s], indices[s], s);
                    else mesh.AppendParticleGeometry(positions[s], normals[s], tangents[s], colors[s], uv[s], uv[s], uv[s], indices[s]);
                }
                foreach (var mesh in meshes) _ = mesh.EngineMesh;
                if (f >= 0) { ticks += Stopwatch.GetTimestamp() - before; bytes += GC.GetAllocatedBytesForCurrentThread() - beforeBytes; }
                app.GraphicsDevice.MeshUploads.DrainPendingUploads();
            }
            if (bytes != 0) throw new Exception("Warmed production page build allocated " + bytes);
            Console.WriteLine(FormattableString.Invariant($"production CPU build pages={pages}: {ticks * 1000d / Stopwatch.Frequency / 100:F6} ms/frame, {bytes / 100d:F1} B/frame (append + CPU packing; excludes upload)"));
        }
        finally { foreach (var mesh in meshes) if (mesh != null) RimUnity.Object.DestroyImmediate(mesh); }
    }
    private static unsafe void CheckProductionMesh()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var page = new RimUnity.Mesh { indexFormat = RimUnity.IndexFormat.UInt32 };
        var baseline = new RimUnity.Mesh[3];
        var positions = new List<RimUnity.Vector3>(); var normals = new List<RimUnity.Vector3>();
        var tangents = new List<RimUnity.Vector4>(); var colors = new List<RimUnity.Color32>();
        var uv = new List<RimUnity.Vector4>(); var ix = new List<int> { 0, 1, 2, 0, 2, 3 };
        try
        {
            for (int s = 0; s < 3; s++)
            {
                positions.Clear(); normals.Clear(); tangents.Clear(); colors.Clear(); uv.Clear();
                for (int v = 0; v < 4; v++)
                {
                    positions.Add(new(s * 10 + v, s + v, 1)); normals.Add(new(0, 0, 1));
                    tangents.Add(new(1, 0, 0, 1)); colors.Add(new(200, (byte)(30 + s), (byte)v, 255)); uv.Add(new(v, s, .3f, .7f));
                }
                baseline[s] = new RimUnity.Mesh { indexFormat = RimUnity.IndexFormat.UInt32 };
                baseline[s].AppendParticleGeometry(positions, normals, tangents, colors, uv, uv, uv, ix);
                page.AppendParticleGeometry(positions, normals, tangents, colors, uv, uv, uv, ix, s);
            }
            var native = page.EngineMesh.CpuStorage;
            var readUsage = typeof(RimUnity.Mesh).GetMethod("ParticleBufferUsage", BindingFlags.Instance | BindingFlags.NonPublic)
                .CreateDelegate<Func<(long Vertices, long Indices, long ManagedBytes, long NativeCpuBytes)>>(page);
            var usage = readUsage();
            long expectedNative = native.GetIndexBuffer().LengthBytes;
            for (int i = 0; i < native.Layout.StreamCount; i++) expectedNative += native.GetVertexStream(i).LengthBytes;
            if (usage.Vertices < page.vertexCount || usage.Indices < 18 || usage.ManagedBytes < page.vertexCount * 92
                || usage.NativeCpuBytes != expectedNative) throw new Exception("Particle buffer diagnostics mismatch");
            long version = page.EngineMesh.Version;
            for (int i = 0; i < 100; i++) _ = readUsage();
            long probeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) _ = readUsage();
            if (GC.GetAllocatedBytesForCurrentThread() != probeBytes || page.EngineMesh.Version != version)
                throw new Exception("Pool diagnostics allocated or changed mesh upload state");
            Console.WriteLine("PASS production mesh memory counters: logical native bytes, managed capacity, zero allocation and no upload-state mutation.");
            int vertexOffset = 0;
            for (int s = 0; s < 3; s++)
            {
                var previous = baseline[s].EngineMesh.CpuStorage;
                var a = native.SubMeshes[s]; var b = previous.SubMeshes[0];
                if (!a.Bounds.Equals(b.Bounds) || a.IndexCount != b.IndexCount)
                    throw new Exception("Shared range changed bounds/index count");
                var expected = new ReadOnlySpan<byte>((void*)previous.GetVertexStream(0).Pointer, previous.GetVertexStream(0).LengthBytes);
                var actual = new ReadOnlySpan<byte>((byte*)native.GetVertexStream(0).Pointer + vertexOffset * native.Layout.GetStreamStride(0), expected.Length);
                if (!actual.SequenceEqual(expected)) throw new Exception("Shared range changed uploaded vertex attributes");
                uint* actualIx = (uint*)native.GetIndexBuffer().Pointer;
                uint* expectedIx = (uint*)previous.GetIndexBuffer().Pointer;
                for (int i = 0; i < a.IndexCount; i++)
                    if (actualIx[a.IndexStart + i] != expectedIx[i] + vertexOffset) throw new Exception("Shared range changed triangle order");
                vertexOffset += baseline[s].vertexCount;
            }
            page.SetNormals(page.normals);
            var packedUV = new List<RimUnity.Vector4>(); page.GetUVs(0, packedUV); page.SetUVs(0, packedUV);
            for (int s = 0; s < 3; s++)
                if (!page.EngineMesh.CpuStorage.SubMeshes[s].Bounds.Equals(baseline[s].EngineMesh.CpuStorage.SubMeshes[0].Bounds))
                    throw new Exception("Non-position channel edits lost per-range bounds");
            var edge = (RimUnity.Mesh)typeof(RimUnity.Mesh).GetMethod("TriangleEdges", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page, null);
            for (int s = 0; s < 3; s++)
                if (!edge.EngineMesh.CpuStorage.SubMeshes[s].Bounds.Equals(baseline[s].EngineMesh.CpuStorage.SubMeshes[0].Bounds))
                    throw new Exception("Triangle-edge geometry lost per-range bounds");
            page.bounds = new RimUnity.Bounds(new(0, 0, 0), new(100, 100, 100));
            for (int s = 0; s < 3; s++)
                if (!page.EngineMesh.CpuStorage.SubMeshes[s].Bounds.Equals(page.EngineMesh.CpuStorage.Bounds))
                    throw new Exception("Explicit bounds override was ignored");
            bool rejected = false;
            try { page.AppendParticleGeometry(positions, normals, tangents, colors, uv, uv, uv, ix); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Legacy single-range API silently changed multirange geometry");
            page.Clear(); page.AppendParticleGeometry(positions, normals, tangents, colors, uv, uv, uv, ix, 0);
            if (page.subMeshCount != 1 || page.vertexCount != 4) throw new Exception("Page Clear retained old ranges");
            page.Clear(); page.subMeshCount = 3;
            page.AppendParticleGeometry(positions, normals, tangents, colors, uv, uv, uv, ix);
            if (page.subMeshCount != 1) throw new Exception("Legacy API failed to collapse empty predeclared ranges");
            // Cold capacity growth is allowed; steady rebuilds retain all CPU list capacities.
            for (int i = 0; i < 1000; i++) { page.Clear(); page.AppendParticleGeometry(positions, normals, tangents, colors, uv, uv, uv, ix, 0); _ = page.EngineMesh; }
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { page.Clear(); page.AppendParticleGeometry(positions, normals, tangents, colors, uv, uv, uv, ix, 0); _ = page.EngineMesh; }
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            if (bytes != 0) throw new Exception("Warmed page CPU geometry allocated: " + bytes);
            Console.WriteLine("PASS production Core mesh: ordered range bytes/indices/bounds match independent meshes; Clear and legacy API contract; 1000 warmed rebuilds allocate 0 B.");
        }
        finally { foreach (var mesh in baseline) if (mesh != null) RimUnity.Object.DestroyImmediate(mesh); RimUnity.Object.DestroyImmediate(page); }
    }
    private static unsafe void Measure(EngineApplication app, Resolve resolve, int pages)
    {
        const int groups = 60, particles = 800, frames = 100, warmup = 30;
        var meshes = new Mesh[pages];
        try
        {
            for (int p = 0; p < pages; p++)
            {
                var mesh = meshes[p] = new Mesh { DebugName = "RimUnity Upload Regression" }; mesh.MarkDynamic();
                int particleCount = pages == 1 ? particles : particles / groups + (p < particles % groups ? 1 : 0);
                var cpu = mesh.CpuStorage;
                cpu.SetVertexLayout(new VertexLayout(new[] {
                    new VertexAttribute(VertexAttributeSemantic.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttribute(VertexAttributeSemantic.Normal, VertexAttributeFormat.Float32, 3),
                    new VertexAttribute(VertexAttributeSemantic.Tangent, VertexAttributeFormat.Float32, 4),
                    new VertexAttribute(VertexAttributeSemantic.Color, VertexAttributeFormat.UNorm8, 4),
                    new VertexAttribute(VertexAttributeSemantic.TexCoord0, VertexAttributeFormat.Float32, 4),
                    new VertexAttribute(VertexAttributeSemantic.TexCoord1, VertexAttributeFormat.Float32, 4),
                    new VertexAttribute(VertexAttributeSemantic.TexCoord2, VertexAttributeFormat.Float32, 4)
                }), particleCount * 4);
                cpu.SetIndexBufferParams(particleCount * 6, MeshIndexFormat.UInt32);
                cpu.SetSubMeshCount(pages == 1 ? groups : 1);
                cpu.Bounds = new MeshBounds(-1, -1, -1, 1, 1, 1);
                int firstIndex = 0, firstVertex = 0;
                for (int s = 0; s < cpu.SubMeshes.Length; s++)
                {
                    int count = pages == 1 ? particles / groups + (s < particles % groups ? 1 : 0) : particleCount;
                    cpu.SetSubMesh(s, new SubMesh(firstIndex, count * 6, bounds: cpu.Bounds));
                    uint* output = (uint*)cpu.GetIndexBuffer().Pointer;
                    for (int q = 0; q < count; q++)
                    {
                        int i = firstIndex + q * 6; uint v = (uint)(firstVertex + q * 4);
                        output[i] = v; output[i + 1] = v + 1; output[i + 2] = v + 2;
                        output[i + 3] = v; output[i + 4] = v + 2; output[i + 5] = v + 3;
                    }
                    firstIndex += count * 6; firstVertex += count * 4;
                }
            }
            long uploadTicks = 0, uploadBytes = 0, geometryTicks = 0, geometryBytes = 0;
            for (int f = -warmup; f < frames; f++)
            {
                var uploader = app.GraphicsDevice.MeshUploads;
                uploader.BeginRenderFrame(); uploader.BeginFrame(0);
                foreach (var mesh in meshes) { mesh.MarkCpuStorageChanged(); uploader.Enqueue(mesh); }
                long startBytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                uploader.DrainPendingUploads();
                long ticks = Stopwatch.GetTimestamp() - start, bytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
                if (f >= 0) { uploadTicks += ticks; uploadBytes += bytes; }
                startBytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
                for (int s = 0; s < groups; s++)
                    if (!resolve(meshes[pages == 1 ? 0 : s], pages == 1 ? s : 0, out _, out string failure)) throw new Exception(failure);
                ticks = Stopwatch.GetTimestamp() - start; bytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
                if (f >= 0) { geometryTicks += ticks; geometryBytes += bytes; }
            }
            Console.WriteLine(FormattableString.Invariant($"pages={pages}, ranges=60, upload={uploadTicks * 1000d / Stopwatch.Frequency / frames:F6}ms/{uploadBytes / (double)frames:F1}B per frame; resolve={geometryTicks * 1000d / Stopwatch.Frequency / frames:F6}ms/{geometryBytes / (double)frames:F1}B per frame"));
        }
        finally { foreach (var mesh in meshes) mesh?.Dispose(); }
    }
}
