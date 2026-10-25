using System.Diagnostics;
using System.Text.Json;
using RimUnity;
using RimUnity.Diagnostics;
using RimUnity.Rendering;

internal static class Program
{
    private static int checks;
    private static object keep;
    private static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); checks++; }
    private static void Main()
    {
        Families(); SnapshotCoverage(); Nesting(); Lifecycle(); Windows(); Batch(); PackedInputs(); Pages(); PoolLifecycle(); Population(); ObserverCost(); Reports();
        Console.WriteLine($"PASS {checks} CPU capture/batch checks (production collector and batch code; fake mesh/host, no GPU).");
    }
    private static void Families()
    {
        var cpu = new LabCpuMetrics(3); cpu.RegisterSystem(101, 2); cpu.RegisterSystem(102, 7); cpu.Start();
        using (LabCpuMetrics.MeasureSystem(LabCpuStage.ParticleRenderer, 101))
        {
            using (LabCpuMetrics.Measure(LabCpuStage.ParticleAttributes)) { keep = new byte[128]; }
            using (LabCpuMetrics.MeasureSystem(LabCpuStage.SimulationSystemStep, 102)) { }
            using (LabCpuMetrics.MeasureSystem(LabCpuStage.ParticleRenderer, 999))
                using (LabCpuMetrics.Measure(LabCpuStage.ParticleAttributes)) { }
        }
        cpu.CompleteFrame();
        Check(cpu.Family(2, LabCpuStage.ParticleAttributes).Calls == 1, "family inherited by child scopes; unknown system does not inherit unrelated family");
        Check(cpu.Family(7, LabCpuStage.SimulationSystemStep).Calls == 1 && cpu.Family(2, LabCpuStage.SimulationSystemStep).Calls == 0, "nested system attribution uses its own family");
        Check(cpu.Family(2, LabCpuStage.ParticleAttributes).SelfBytes >= 128, "family bytes attributed");
        cpu.CompleteFrame();
        Check(cpu.Family(2, LabCpuStage.ParticleAttributes).Calls == 1, "family pending clears after completed frame");
        using (LabCpuMetrics.MeasureSystem(LabCpuStage.ParticleRenderer, 101)) { }
        cpu.Stop();
        Check(cpu.Family(2, LabCpuStage.ParticleRenderer).Calls == 1, "unfinished tail frame excluded from family totals");
        using (LabCpuMetrics.MeasureSystem(LabCpuStage.ParticleRenderer, 101)) { }
        Check(cpu.Family(2, LabCpuStage.ParticleRenderer).Calls == 1, "disabled capture cannot change family totals");
        var warm = new LabCpuMetrics(1); warm.RegisterSystem(1, 0); warm.Start();
        for (int i = 0; i < 100; i++)
            using (LabCpuMetrics.MeasureSystem(LabCpuStage.ParticleRenderer, 1))
                using (LabCpuMetrics.Measure(LabCpuStage.ParticleAttributes)) { }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            using (LabCpuMetrics.MeasureSystem(LabCpuStage.ParticleRenderer, 1))
                using (LabCpuMetrics.Measure(LabCpuStage.ParticleAttributes)) { }
        warm.CompleteFrame();
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "family scopes and completed-frame aggregation allocate zero bytes after warmup");
        warm.Stop();
    }
    private static void SnapshotCoverage()
    {
        var cpu = new LabCpuMetrics(2); cpu.Start();
        LabCpuMetrics.RecordSnapshot(false, 9, -1); LabCpuMetrics.RecordSnapshot(true, 0, 1); cpu.CompleteFrame();
        var first = cpu.GetSnapshot(0);
        Check(first.HdrCalls == 1 && first.HdrCommands == 9 && first.BloomCalls == 1 && first.BloomCommands == 0, "empty Bloom is not missing Bloom");
        LabCpuMetrics.RecordSnapshot(true, 7, 1); LabCpuMetrics.RecordSnapshot(true, 3, 4); cpu.CompleteFrame();
        var second = cpu.GetSnapshot(1);
        Check(second.HdrCalls == 0 && second.BloomCalls == 2 && second.BloomCommands == 10 && second.BloomMask == 5, "snapshot counts reset per frame and combine multiple captures");
        cpu.Stop(); LabCpuMetrics.RecordSnapshot(true, 100, -1);
        Check(cpu.GetSnapshot(1) == second, "disabled capture cannot change retained coverage");
    }
    private static void Nesting()
    {
        var cpu = new LabCpuMetrics(3); cpu.Start();
        using (LabCpuMetrics.Measure(LabCpuStage.ParticleRenderer))
        {
            keep = new byte[1024];
            using (LabCpuMetrics.Measure(LabCpuStage.ParticleSubmit))
            { keep = new byte[2048]; Thread.SpinWait(1000); }
        }
        cpu.CompleteFrame(); cpu.CompleteFrame(); cpu.Stop();
        var parent = cpu.Get(0, LabCpuStage.ParticleRenderer); var child = cpu.Get(0, LabCpuStage.ParticleSubmit);
        Check(parent.Calls == 1 && child.Calls == 1, "nested calls counted once");
        Check(parent.Bytes == parent.SelfBytes + child.Bytes && child.SelfBytes == child.Bytes, "nested allocation attribution conserves bytes");
        Check(parent.SelfBytes >= 1024 && child.Bytes >= 2048, "real thread allocation measured");
        Check(parent.Ticks == parent.SelfTicks + child.Ticks, "nested CPU attribution conserves time");
        Check(cpu.Get(1, LabCpuStage.ParticleSubmit).Calls == 0, "frame counters reset");
        var stage = LabCpuReport.Stages(cpu)[(int)LabCpuStage.ParticleRenderer];
        Check(stage.CallsPerFrame == .5 && stage.P95InclusiveMs == stage.MaxFrameInclusiveMs, "summary includes zero-call frames");
        Check(JsonSerializer.Serialize(LabCpuReport.Stages(cpu)).Contains("SelfBytesPerFrame"), "JSON retains numeric metrics");
        Check(cpu.InvalidScopes == 0, "balanced nesting valid");
    }
    private static void Lifecycle()
    {
        var old = new LabCpuMetrics(2); old.Start();
        try { using var scope = LabCpuMetrics.Measure(LabCpuStage.MeshFill); throw new Exception(); }
        catch { }
        old.CompleteFrame();
        Check(old.Get(0, LabCpuStage.MeshFill).Calls == 1, "exception scope closes");
        Task.Run(() => { using var scope = LabCpuMetrics.Measure(LabCpuStage.MeshFill); LabCpuMetrics.Batch(LabBatchReason.Accepted); }).GetAwaiter().GetResult();
        Check(old.CrossThreadScopes == 1, "worker scope explicitly skipped");
        var late = LabCpuMetrics.Measure(LabCpuStage.MeshFill); old.Stop();
        var next = new LabCpuMetrics(1); next.Start(); late.Dispose();
        using (LabCpuMetrics.Measure(LabCpuStage.DrawMesh)) { }
        next.CompleteFrame(); next.CompleteFrame(); next.Stop(); next.Stop();
        Check(next.FrameCount == 1 && next.Get(0, LabCpuStage.MeshFill).Calls == 0, "late scope cannot contaminate next session; capacity bounded");
        Check(next.Get(0, LabCpuStage.DrawMesh).Calls == 1 && next.InvalidScopes == 0, "new session isolated");
        using (LabCpuMetrics.Measure(LabCpuStage.DrawMesh)) { }
        Check(next.Get(0, LabCpuStage.DrawMesh).Calls == 1, "stop disables collection");
    }
    private static void Windows()
    {
        var cpu = new LabCpuMetrics(4); cpu.Start(); cpu.CompleteFrame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        keep = new byte[128]; cpu.BeginMainFrame();
        keep = new byte[256];
        using (LabCpuMetrics.BridgeWindow()) { keep = new byte[512]; }
        keep = new byte[1024];
        long after = GC.GetAllocatedBytesForCurrentThread();
        cpu.CompleteFrame(); cpu.CompleteFrame();
        cpu.BeginMainFrame();
        using (LabCpuMetrics.BridgeWindow()) { }
        using (LabCpuMetrics.BridgeWindow()) { }
        cpu.CompleteFrame(); cpu.Stop();
        Check(!cpu.HasWindows(0) && cpu.HasWindows(1) && !cpu.HasWindows(2), "partial/missing boundaries omitted");
        Check(!cpu.HasWindows(3), "duplicate bridge boundaries omitted");
        Check(cpu.GetWindow(1, LabFrameWindow.Bridge).Bytes >= 512, "bridge allocation captured");
        Check(cpu.GetWindow(1, LabFrameWindow.PrePollToBridge).Bytes >= 256, "pre-bridge allocation captured");
        Check(cpu.GetWindow(1, LabFrameWindow.BridgeToEnqueue).Bytes >= 1024, "post-bridge allocation captured");
        var windows = LabCpuReport.Windows(cpu);
        Check(windows.Length == 4 && windows.All(w => w.CompleteFrames == 1), "only complete intervals summarized");
        Check(windows.Sum(w => w.BytesPerFrame) >= after - before, "timeline allocation partition covers inter-frame work");
    }
    private static readonly List<Vector3> v = new() { default, default, default };
    private static readonly List<Vector3> n = new();
    private static readonly List<Vector4> t = new();
    private static readonly List<Color32> c = new();
    private static readonly List<int> indices = new() { 0, 1, 2 };
    private static bool Append(Material m, int layer = 0, MaterialPropertyBlock block = null, List<Vector3> vertices = null, List<int> ix = null)
        => ParticleDrawBatch.TryAppend(m, layer, block, vertices ?? v, n, t, c, t, t, t, ix ?? indices);
    private static void Batch()
    {
        var cpu = new LabCpuMetrics(2); cpu.Start(); Time.frameCount++;
        var a = new Material(); var b = new Material();
        Check(!Append(a), "inactive draw not batched");
        ParticleDrawBatch.Begin();
        Check(Append(a) && Append(a) && Append(b), "compatible adjacent inputs accepted");
        Check(Graphics.Draws.Count == 0, "material change closes a range without publishing an unfinished page");
        Append(b, 1); b.engineMaterial.Version++; Append(b, 1);
        Check(!Append(a, block: new MaterialPropertyBlock { PropertyCount = 1 }), "property block preserves independent path");
        Check(Graphics.Draws[0].Indices.SequenceEqual(new[] { 0, 1, 2, 3, 4, 5 }), "shared page preserves triangle order and offsets");
        Check(Graphics.Draws.Count == 4 && Graphics.Draws.Select(d => d.Mesh).Distinct().Count() == 1, "incompatible input drains all prior ranges in one shared page");
        Check(!Append(a, block: new MaterialPropertyBlock { HasStorageBindings = true }), "storage binding preserves independent path");
        ParticleDebugWindow.Capturing = true; Check(!Append(a), "debug capture rejected"); ParticleDebugWindow.Capturing = false;
        Check(!Append(new Material { passCount = 2 }), "multipass rejected");
        Check(!Append(new Material { Eligibility = ParticleBatchShaders.Eligibility.UnknownShader }), "unknown shader rejected");
        Check(!Append(new Material { Eligibility = ParticleBatchShaders.Eligibility.UnsafeShader }), "unsafe shader rejected");
        Check(!Append(a, vertices: Enumerable.Repeat(default(Vector3), ParticleDrawBatch.VertexLimit + 1).ToList()), "oversize vertices rejected");
        Check(!Append(a, ix: Enumerable.Repeat(0, ParticleDrawBatch.IndexLimit + 1).ToList()), "oversize indices rejected");
        Append(a, vertices: Enumerable.Repeat(default(Vector3), ParticleDrawBatch.VertexLimit).ToList()); Append(a);
        Append(a, ix: Enumerable.Repeat(0, ParticleDrawBatch.IndexLimit).ToList()); Append(a);
        ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        cpu.CompleteFrame();
        foreach (var reason in new[] { LabBatchReason.Inactive, LabBatchReason.MaterialChanged, LabBatchReason.LayerChanged,
            LabBatchReason.MaterialVersionChanged, LabBatchReason.PropertyBlock, LabBatchReason.StorageBindings,
            LabBatchReason.DebugCapture, LabBatchReason.MultiplePasses, LabBatchReason.UnknownShader, LabBatchReason.UnsafeShader,
            LabBatchReason.InputVertexLimit, LabBatchReason.InputIndexLimit, LabBatchReason.BatchVertexLimit, LabBatchReason.BatchIndexLimit })
            Check(cpu.Reason(0, reason) > 0, "reason recorded: " + reason);
        ParticleDrawBatch.Begin(); Mesh.FailAppend = true;
        try { Append(a); } catch (InvalidOperationException) { } finally { Mesh.FailAppend = false; ParticleDrawBatch.End(); }
        ParticleDrawBatch.Begin(); Check(Append(a), "append exception can cleanly restart"); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        cpu.CompleteFrame(); cpu.Stop();
        Check(cpu.InvalidScopes == 0, "batch exception leaves balanced collector stack");
        Check(LabCpuReport.Reasons(cpu)[(int)LabBatchReason.Accepted].Count > 0, "reason report populated");
        ParticleDrawBatch.Clear();
    }
    private static void PackedInputs()
    {
        ParticleDrawBatch.Clear(); Graphics.Draws.Clear(); Time.frameCount++;
        var material = new Material();
        var packed = Enumerable.Repeat(default(ParticleVertex), 6).ToList();
        ParticleDrawBatch.Begin();
        Check(ParticleDrawBatch.TryAppend(material, 0, null, new List<Vector3>(), n, t, c, t, t, t, indices, packed),
            "packed input accepted with empty legacy channels");
        Check(Append(material), "legacy input accepted after packed input");
        ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(Graphics.Draws.Count == 1 && Graphics.Draws[0].Mesh.vertexCount == 9,
            "mixed fixture counts packed vertices instead of empty legacy channels");
        Check(Graphics.Draws[0].Indices.SequenceEqual(new[] { 0, 1, 2, 6, 7, 8 }),
            "legacy indices use the preceding packed vertex offset");
        ParticleDrawBatch.Begin();
        Check(!ParticleDrawBatch.TryAppend(material, 0, null, new List<Vector3>(), n, t, c, t, t, t, indices,
            Enumerable.Repeat(default(ParticleVertex), ParticleDrawBatch.VertexLimit + 1).ToList()),
            "packed vertex limit enforced with empty legacy channels");
        ParticleDrawBatch.End(); ParticleDrawBatch.Clear();
    }
    private static void Pages()
    {
        Graphics.Draws.Clear(); ParticleDrawBatch.Sampling = true; Time.frameCount++;
        var a = new Material(); var b = new Material();
        ParticleDrawBatch.Begin(); Append(a); Append(b); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(Graphics.Draws.Select(d => d.Material).SequenceEqual(new[] { a, b, a }), "ABA materials retain order, no global regrouping");
        Check(Graphics.Draws.Select(d => d.Mesh).Distinct().Count() == 1 && ParticleDrawBatch.UploadPages == 1, "multiple material ranges share one upload page");
        Check(Graphics.Draws.SelectMany(d => d.Indices).SequenceEqual(Enumerable.Range(0, 9)), "range index offsets retain all triangles");
        var firstPage = Graphics.Draws[0].Mesh;
        ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(!ReferenceEquals(firstPage, Graphics.Draws[^1].Mesh), "same-frame cameras get independent mesh pages");
        Check(firstPage.vertexCount == 9 && firstPage.Submeshes.Count == 3, "later camera cannot overwrite earlier page geometry");
        Time.frameCount++; Graphics.Draws.Clear();
        ParticleDrawBatch.Begin();
        for (int i = 0; i < ParticleDrawBatch.SegmentLimit + 1; i++) Append(i % 2 == 0 ? a : b);
        ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(Graphics.Draws.Count == ParticleDrawBatch.SegmentLimit + 1 && ParticleDrawBatch.UploadPages == 2, "range limit creates bounded pages without losing draws");
        Time.frameCount++; Graphics.Draws.Clear();
        ParticleDrawBatch.Begin(); Append(a); Append(b); ParticleDrawBatch.End();
        Check(Graphics.Draws.Count == 0, "aborted page publishes no unfinished ranges");
        ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(Graphics.Draws.Count == 1 && Graphics.Draws[0].Submesh == 0, "aborted range references do not leak into the next page");
        ParticleDrawBatch.Clear(); ParticleDrawBatch.Sampling = false;
    }
    private static void PoolLifecycle()
    {
        ParticleDrawBatch.Clear(); Graphics.Draws.Clear(); Time.frameCount++;
        var a = new Material(); ParticleDrawBatch.Sampling = true;
        ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        var original = Graphics.Draws[0].Mesh;
        Time.frameCount++;
        Check(ParticleDrawBatch.ReadPoolUsage().UsedThisFrame == 0, "unused new frame reports no stale leases");
        ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(ReferenceEquals(original, Graphics.Draws[^1].Mesh) && original.vertexCount == 3,
            "next frame reuses cleared page without accumulating old geometry");
        Check(ParticleDrawBatch.ReadPoolUsage().Pages == 1, "stable frame reuses one page");
        for (int i = 0; i < 10000; i++) _ = ParticleDrawBatch.ReadPoolUsage();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) _ = ParticleDrawBatch.ReadPoolUsage();
        long poolBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(poolBytes == 0, "pool diagnostics allocate zero bytes after warmup: " + poolBytes);
        ParticleDrawBatch.Clear(); Graphics.Draws.Clear(); Time.frameCount++;
        var fullVertices = Enumerable.Repeat(default(Vector3), ParticleDrawBatch.VertexLimit).ToList();
        ParticleDrawBatch.Begin(); Append(a, vertices: fullVertices); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(Graphics.Draws.Count == 2 && !ReferenceEquals(Graphics.Draws[0].Mesh, Graphics.Draws[1].Mesh)
            && Graphics.Draws[0].Mesh.vertexCount == ParticleDrawBatch.VertexLimit,
            "exact vertex boundary splits before overflow without overwriting published page");
        ParticleDrawBatch.Clear(); Graphics.Draws.Clear(); Time.frameCount++;
        var fullIndices = Enumerable.Repeat(0, ParticleDrawBatch.IndexLimit).ToList();
        ParticleDrawBatch.Begin(); Append(a, ix: fullIndices); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(Graphics.Draws.Count == 2 && Graphics.Draws[0].Indices.Length == ParticleDrawBatch.IndexLimit,
            "exact index boundary splits before overflow and preserves complete range");
        ParticleDrawBatch.Clear(); Graphics.Draws.Clear(); Time.frameCount++;
        for (int i = 0; i < 130; i++)
        { ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End(); }
        var burst = Graphics.Draws.Select(d => d.Mesh).ToArray();
        var peak = ParticleDrawBatch.ReadPoolUsage();
        Check(peak.Pages == 130 && peak.UsedThisFrame == 130 && peak.IdlePages == 0,
            "same-frame burst reports all camera/page leases");
        Check(peak.VertexCapacity >= 390 && peak.ManagedBufferBytes > 0, "retained pool capacities include burst pages");
        Time.frameCount++; ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(ParticleDrawBatch.ReadPoolUsage().Pages == 130 && !burst.Any(m => m.Destroyed),
            "previous-frame pages survive immediate burst recovery");
        Time.frameCount++; ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        var reduced = ParticleDrawBatch.ReadPoolUsage();
        Check(reduced.Pages == 128 && reduced.IdlePages == 127 && burst.Count(m => m.Destroyed) == 2,
            "excess idle tail retires after one intervening frame");
        Time.frameCount += 120; ParticleDrawBatch.Begin();
        Check(ParticleDrawBatch.ReadPoolUsage().Pages == 0 && burst.All(m => m.Destroyed),
            "all pages idle for 120 frames release their meshes");
        ParticleDrawBatch.End();
        ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        var final = Graphics.Draws[^1].Mesh;
        ParticleDrawBatch.Clear(); ParticleDrawBatch.Clear();
        Check(final.Destroyed && ParticleDrawBatch.ReadPoolUsage() == default,
            "scene clear is idempotent and drops all pool memory references and counters");
        ParticleDrawBatch.Sampling = false; Graphics.Draws.Clear();
    }
    private static void Population()
    {
        var lab = new PerformanceLab(); var roots = new HashSet<GameObject>();
        var owner = new GameObject { name = "same-name" }; lab.OwnedRoots.Add(owner);
        Camera.main = new Camera { cullingMask = 1 }; Camera.Active.Add(Camera.main);
        Camera.Active.Add(new Camera { targetTexture = new object() });
        ParticleSystem Add(int particles, bool renderer = true, bool owned = false)
        {
            var system = new ParticleSystem { particleCount = particles, Renderer = renderer ? new ParticleSystemRenderer() : null };
            system.gameObject.name = "same-name";
            if (owned) system.transform.parent = owner.transform;
            ParticleSystem.Active.Add(system); return system;
        }
        var ours = Add(10, owned: true); Add(0, owned: true); Add(0);
        var disabledRenderer = Add(7); disabledRenderer.Renderer.isActiveAndEnabled = false;
        Add(0).HasCanvas = true; Add(0).gameObject.layer = 3; Add(0, renderer: false);
        Add(99).isActiveAndEnabled = false;
        var state = LabPopulation.Read(lab, roots);
        Check(state.ActiveSystems == 7 && state.ActiveParticles == 17 && state.OwnedSystems == 2 && state.BackgroundSystems == 5,
            "global/owned/background populations use hierarchy rather than object names");
        Check(state.OwnedParticles == 10 && state.BackgroundParticles == 7, "particle attribution excludes inactive systems");
        Check(state.RendererSystems == 5 && state.MainCameraCandidates == 3 && state.OwnedMainCameraCandidates == 2,
            "renderer/camera candidates distinguish disabled, missing, UI and masked paths");
        Check(state.BackgroundRendererSystems == 3 && state.EmptyRendererSystems == 4 && state.BackgroundEmptyRendererSystems == 3,
            "empty renderer systems remain observable without being counted as submissions");
        Check(roots.Count == 0, "population read clears temporary ownership references");
        for (int i = 0; i < 10000; i++) _ = LabPopulation.Read(lab, roots);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) _ = LabPopulation.Read(lab, roots);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(allocated == 0, "warmed population scans allocate zero: " + allocated);
        ours.transform.parent = null;
        Check(LabPopulation.Read(lab, roots).OwnedSystems == 1, "reparented systems are reclassified on the next sample");
        var capture = LabPopulation.Capture(lab, roots);
        Check(capture.ActiveCameras == 2 && capture.OffscreenCameras == 1 && capture.Background.Length == 6 && !capture.Truncated,
            "boundary inventory includes names, cameras and background systems");
        Check(capture.OwnedEmptyRenderers.Length == 1 && !capture.OwnedEmptyTruncated,
            "owned empty renderers are listed separately from background systems");
        for (int i = 0; i < 150; i++) Add(0).gameObject.name = new string('x', 200) + "\n";
        capture = LabPopulation.Capture(lab, roots);
        Check(capture.Background.Length == LabPopulation.DetailLimit && capture.Truncated && capture.Counts.BackgroundSystems == 156,
            "bounded identity list does not truncate global numeric totals");
        Check(capture.Background.All(e => e.Name.Length <= 80 && !e.Name.Contains('\n')) && roots.Count == 0,
            "inventory text is bounded and retains no ownership roots");
        lab.OwnedRoots.Clear();
        Check(LabPopulation.Read(lab, roots).OwnedSystems == 0, "cleared lab cannot retain earlier ownership");
        ParticleSystem.Active.Clear(); Camera.Active.Clear(); Camera.main = null;
    }
    private static void ObserverCost()
    {
        const int iterations = 100000;
        var cpu = new LabCpuMetrics(1); cpu.Start();
        for (int i = 0; i < 10000; i++) { using var scope = LabCpuMetrics.Measure(LabCpuStage.DrawMesh); }
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        { using var scope = LabCpuMetrics.Measure(LabCpuStage.DrawMesh); LabCpuMetrics.Batch(LabBatchReason.Accepted); }
        long ticks = Stopwatch.GetTimestamp() - start, allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        cpu.CompleteFrame(); cpu.Stop();
        Check(allocated == 0, "warmed active scopes and counters allocate zero bytes");
        bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) { using var scope = LabCpuMetrics.Measure(LabCpuStage.DrawMesh); }
        long idleTicks = Stopwatch.GetTimestamp() - start;
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "inactive scopes allocate zero bytes");
        Console.WriteLine($"Observer microbenchmark: active scope + counter {ticks * 1e9 / Stopwatch.Frequency / iterations:F1} ns/call; inactive {idleTicks * 1e9 / Stopwatch.Frequency / iterations:F1} ns/call; active allocations {allocated} B. Synthetic, not game frame overhead.");
    }
    private static void Reports()
    {
        var lab = new PerformanceLab { RegisterFixtureFamily = true };
        using var sampler = new LabSampler(lab);
        Time.frameCount++; ParticleDrawBatch.Begin(); Append(new Material()); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        sampler.Start(60);
        Check(sampler.Sampling && Waylith.EngineApplication.Instance.Subscribers == 1, "sampler subscribes once");
        for (int i = 0; i < 2; i++)
        {
            if (i == 1)
            {
                Time.frameCount++;
                for (int page = 0; page < 2; page++)
                { ParticleDrawBatch.Begin(); Append(new Material()); ParticleDrawBatch.Flush(); ParticleDrawBatch.End(); }
            }
            Waylith.EngineApplication.Instance.Poll();
            using (LabCpuMetrics.BridgeWindow())
                using (LabCpuMetrics.Measure(LabCpuStage.BridgeUpdate)) { keep = new byte[256]; }
            LabCpuMetrics.Batch(LabBatchReason.MaterialChanged);
            LabCpuMetrics.RecordSnapshot(false, 12, -1); LabCpuMetrics.RecordSnapshot(true, i == 0 ? 0 : 7, 1 << 30);
            using (LabCpuMetrics.MeasureSystem(LabCpuStage.ParticleRenderer, 123))
                using (LabCpuMetrics.Measure(LabCpuStage.ParticleGeometry)) { }
            Waylith.EngineApplication.Instance.Emit();
        }
        ParticleDrawBatch.Clear(); sampler.Stop("fixture", export: true);
        Check(!sampler.Sampling && Waylith.EngineApplication.Instance.Subscribers == 0 && Waylith.EngineApplication.Instance.PrePollSubscribers == 0 && !ParticleDrawBatch.Sampling, "stop unsubscribes all sampler state");
        string prefix = Path.ChangeExtension(sampler.LastReport, null);
        using var json = JsonDocument.Parse(File.ReadAllText(prefix + ".json"));
        var root = json.RootElement;
        Check(root.GetProperty("Schema").GetInt32() == 8 && root.GetProperty("Cpu").GetProperty("Frames").GetInt32() == 2, "schema and frame count exported");
        Check(root.GetProperty("ParticlePopulation").GetProperty("Start").GetProperty("Counts").GetProperty("ActiveSystems").GetInt32() == 0
            && root.GetProperty("Frames")[0].GetProperty("ParticlePopulation").GetProperty("BackgroundSystems").GetInt32() == 0,
            "population boundaries and per-frame numeric attribution exported");
        Check(File.ReadAllLines(prefix + ".population.csv").Length == 3
            && File.ReadAllLines(prefix + ".population.csv").All(row => row.Split(',').Length == 14), "population CSV rows and schema align");
        Check(root.GetProperty("ParticlePool").GetProperty("Start").GetProperty("Pages").GetInt32() == 1
            && root.GetProperty("ParticlePool").GetProperty("End").GetProperty("Pages").GetInt32() == 0
            && root.GetProperty("ParticlePool").GetProperty("PeakPages").GetInt32() == 2
            && root.GetProperty("Frames")[0].GetProperty("ParticlePool").GetProperty("ManagedBufferBytes").GetInt64() > 0,
            "pool start, sampled peak and cleared endpoint remain distinct in report");
        Check(root.GetProperty("Cpu").GetProperty("Enabled").GetBoolean() && !root.GetProperty("Configuration").GetProperty("RuntimeIsStandalone").GetBoolean(), "source/measurement metadata exported");
        var coverage = root.GetProperty("Cpu").GetProperty("SnapshotCoverage");
        var familyRows = root.GetProperty("Cpu").GetProperty("Families");
        Check(familyRows.GetArrayLength() == 2 && familyRows[0].GetProperty("Calls").GetInt64() == 2
            && familyRows[0].GetProperty("Family").GetInt32() == 0, "completed frame family stages exported to JSON");
        Check(File.ReadAllLines(prefix + ".families.csv").Length == 3
            && File.ReadAllLines(prefix + ".families.csv").All(row => row.Split(',').Length == 8), "aggregate family CSV schema");
        Check(coverage.GetProperty("HdrCommands").GetInt64() == 24 && coverage.GetProperty("BloomCommands").GetInt64() == 7
            && coverage.GetProperty("BloomNonemptyFrames").GetInt32() == 1 && coverage.GetProperty("BloomMask").GetInt32() == (1 << 30), "actual snapshot coverage exported");
        Check(File.ReadAllLines(prefix + ".snapshots.csv").Length == 3
            && File.ReadAllLines(prefix + ".snapshots.csv").All(row => row.Split(',').Length == 6), "snapshot CSV frame alignment");
        var bridge = root.GetProperty("Cpu").GetProperty("Stages")[0];
        Check(bridge.GetProperty("Calls").GetInt64() == 2 && bridge.GetProperty("SelfBytesPerFrame").GetDouble() >= 256, "real sampler exports phase calls/allocations");
        var rows = File.ReadAllLines(prefix + ".frames.csv");
        Check(rows.Length == 3 && rows.All(row => row.Split(',').Length == 30), "frame CSV headers/rows align including pool capacities");
        Check(File.ReadAllLines(prefix + ".stages.csv").Count(row => row.Contains(",BridgeUpdate,")) == 2
            && File.ReadAllLines(prefix + ".batch.csv").Count(row => row.Contains(",MaterialChanged,")) == 2,
            "stage/batch CSV frame rows exported alongside pool activity");
        Check(root.GetProperty("Frames")[0].GetProperty("ObserverAllocatedBytes").GetInt64() >= 0, "observer counters serialized");
        Check(root.GetProperty("ProcessMemory").GetProperty("EndCaptured").GetBoolean(), "separate end memory query recorded");
        Check(root.GetProperty("Cpu").GetProperty("FrameWindows")[0].GetProperty("CompleteFrames").GetInt32() == 1, "complete windows exported");
        Check(File.ReadAllLines(prefix + ".windows.csv").Length == 5, "window CSV skips partial first frame");
        lab.MeasureCpuStages = false;
        Thread.Sleep(2); sampler.Start(60); Waylith.EngineApplication.Instance.Emit(); sampler.Stop("baseline", export: true);
        prefix = Path.ChangeExtension(sampler.LastReport, null);
        using var baseline = JsonDocument.Parse(File.ReadAllText(prefix + ".json"));
        Check(!baseline.RootElement.GetProperty("Cpu").GetProperty("Enabled").GetBoolean(), "baseline records CPU capture disabled");
        Check(!File.Exists(prefix + ".stages.csv") && !File.Exists(prefix + ".batch.csv") && !File.Exists(prefix + ".windows.csv") && !File.Exists(prefix + ".families.csv"), "disabled capture exports no fictitious zero stage rows");
        lab.Playing = false; lab.LiveCount = 0; lab.StableReady = false;
        Thread.Sleep(2); sampler.Start(60); Waylith.EngineApplication.Instance.Emit(); sampler.Stop("empty", export: true);
        using var empty = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(sampler.LastReport, ".json")));
        Check(empty.RootElement.GetProperty("Configuration").GetProperty("EmptyBaseline").GetBoolean(), "empty baseline can sample without playback");
        Check(empty.RootElement.GetProperty("Frames")[0].GetProperty("Particles").GetInt32() == 0, "empty baseline reports zero test particles");
        // Force the old periodic refresh branch repeatedly without waiting a minute.
        lab.MeasureCpuStages = true; sampler.Start(60);
        var memoryClock = typeof(LabSampler).GetField("memoryTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        for (int i = 0; i < 60; i++) { memoryClock.SetValue(sampler, 0L); Waylith.EngineApplication.Instance.Emit(); }
        sampler.Stop("refresh-cost", export: true);
        using var refresh = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(sampler.LastReport, ".json")));
        var observer = refresh.RootElement.GetProperty("Frames").EnumerateArray().Select(f => f.GetProperty("ObserverCpuMs").GetDouble()).ToArray();
        Console.WriteLine($"Forced periodic GC/UI refresh (60 callbacks): Observer mean={observer.Average():F6} ms, max={observer.Max():F6} ms; process query moved outside collection.");
        lab.MeasureCpuStages = true; sampler.Start(60); lab.IsCurrent = false; Waylith.EngineApplication.Instance.Emit();
        Check(!sampler.Sampling && Waylith.EngineApplication.Instance.Subscribers == 0, "leaving scene stops collection");
        Console.WriteLine("Sampler schema/CSV/lifecycle report fixtures: " + Verse.GenFilePaths.ConfigFolderPath);
    }
}
