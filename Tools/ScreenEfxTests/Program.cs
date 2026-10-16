using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using RimUnity.ScreenEfx;
using Waylith.Rendering;

internal static class Program
{
    private static int assertions;
    private static int Main(string[] args)
    {
        string root = args.Length == 0 ? "D:/SteamLibrary/steamapps/common/RimWorld1.7" : args[0];
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(root, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        _ = Verse.UnityData.ThreadId;
        Session();
        var first = new Clip("first") { Enabled = false };
        var handle = EfxPlayer.Play(first);
        EfxPlayer.Update(new(0, .1f, 0, .1f));
        Check(first.Updates == 1 && first.Last.DeltaTime == 0 && first.Last.UnscaledDeltaTime == .1f, "disabled clips update with both clocks");
        EfxPlayer.Capture(); Check(first.Builds == 0, "disabled clips do not draw");
        Throws(() => EfxPlayer.Play(first), "duplicate submit rejected");
        first.Enabled = true; first.Value = .7f;
        EfxPlayer.Capture(); Check(first.CapturedValue == .7f, "mutations are captured next frame");
        handle.Stop(); handle.Stop(); handle.Dispose(); EfxPlayer.Drain();
        Check(!handle.IsPlaying && first.Releases == 1, "stop/dispose release once");
        Throws(() => EfxPlayer.Play(first), "stopped clips cannot be reused");

        Session();
        var order = new List<string>();
        var a = new Clip("a", order) { Order = 2 };
        var b = new Clip("b", order) { Order = 1 };
        var c = new Clip("c", order) { Order = 1 };
        EfxPlayer.Play(a); EfxPlayer.Play(b); EfxPlayer.Play(c);
        EfxPlayer.Capture(); Check(string.Join(",", order) == "b,c,a", "order is stable across clips");
        a.Order = 0; order.Clear(); EfxPlayer.Capture(); Check(string.Join(",", order) == "a,b,c", "changed priority takes effect");
        Verse.LongEventHandler.AnyEventNowOrWaiting = true; order.Clear(); EfxPlayer.Capture(); Check(order.Count == 0, "long events do not capture");
        Verse.LongEventHandler.AnyEventNowOrWaiting = false;

        Session();
        var bad = new Clip("bad") { FailUpdate = true }; var good = new Clip("good");
        var badHandle = EfxPlayer.Play(bad); EfxPlayer.Play(good);
        int errors = Verse.Log.Errors.Count;
        EfxPlayer.Update(default); EfxPlayer.Update(default);
        Check(!badHandle.IsPlaying && bad.Releases == 1 && good.Updates == 2, "update failure isolates clip");
        Check(Verse.Log.Errors.Count == errors + 1, "failure logs once");
        var badBuild = new Clip("badBuild") { FailBuild = true }; EfxPlayer.Play(badBuild);
        EfxPlayer.Capture(); EfxPlayer.Drain(); Check(badBuild.Releases == 1 && good.Builds == 1, "build failure isolates clip");

        Session();
        var delayed = new Clip("delayed"); var delayedHandle = EfxPlayer.Play(delayed);
        var lease = delayedHandle.Lease;
        var frame = new EfxFrame(); lease.Retain(); frame.Leases.Add(lease);
        frame.Retain(); delayedHandle.Stop(); frame.Dispose(); EfxPlayer.Drain();
        Check(delayed.Releases == 0, "GPU slot keeps stopped clip alive after payload disposal");
        frame.Drop(); EfxPlayer.Drain(); Check(delayed.Releases == 1, "slot retirement releases clip");
        Check(lease.Owner == null && lease.Clip == null, "retired handles do not retain game state");

        Session();
        var old = new Clip("old"); var oldHandle = EfxPlayer.Play(old);
        Verse.Current.Game = new();
        var next = new Clip("new"); var nextHandle = EfxPlayer.Play(next);
        EfxPlayer.SynchronizeSession();
        Check(!oldHandle.IsPlaying && old.Releases == 1 && nextHandle.IsPlaying && next.Updates == 0, "session change preserves new submissions without double update");
        bool rejected = Task.Run(() => { try { nextHandle.Stop(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
        Check(rejected && nextHandle.IsPlaying, "worker thread cannot mutate player");
        using (var guarded = EfxFrame.Rent())
        {
            var coreMaterial = (RimUnity.Material)RuntimeHelpers.GetUninitializedObject(typeof(RimUnity.Material));
            var hostMaterial = (UnityEngine.Material)RuntimeHelpers.GetUninitializedObject(typeof(UnityEngine.Material));
            bool RejectBeforeRead(Action action) => Task.Run(() =>
            {
                try { action(); return false; }
                catch (InvalidOperationException error) { return error.Message.Contains("game thread"); }
            }).GetAwaiter().GetResult();
            Check(RejectBeforeRead(() => guarded.Builder.AddPass(coreMaterial)), "RimUnity pass submission rejects worker access before resolving material state");
            Check(RejectBeforeRead(() => guarded.Builder.AddPass(hostMaterial)), "host pass submission rejects worker access before resolving material state");
        }

        Snapshot();
        Pooling();
        Warmup();
        Session();
        Check(EfxPlayer.Capture() == null, "empty capture bypasses processing");
        Benchmark();
        BufferBudgets();
        FullscreenUploadTests.Run(Check);
        IdleCleanup();
        Console.WriteLine($"PASS: {assertions} screen-effect playback/snapshot assertions.");
        return 0;
    }

    private static void IdleCleanup()
    {
        Session();
        var clip = new Clip("warm cache cleanup"); var handle = EfxPlayer.Play(clip);
        using var pipeline = new EfxGpuPipeline();
        var type = typeof(EfxGpuPipeline);
        object Get(string name) => HarmonyLib.AccessTools.Field(type, name).GetValue(pipeline);
        void Set(string name, object value) => HarmonyLib.AccessTools.Field(type, name).SetValue(pipeline, value);
        //No GPU is needed to reproduce the crash: the live warmed cache has no images yet.
        var renderers = new MeshBatchRenderer[2]; Set("renderers", renderers); Set("warmed", true);
        var scene = (Waylith.Rendering.PostFx.PostEffectPass)Get("scene");
        var output = (Waylith.Rendering.PostFx.PostEffectPass)Get("output");
        var allocation = (GpuTextureAllocation)RuntimeHelpers.GetUninitializedObject(typeof(GpuTextureAllocation));
        scene.SetInput(0, allocation, default); scene.SetInput(1, allocation, default); output.SetInput(0, allocation, default);
        pipeline.ReleaseIfIdle(); pipeline.ReleaseIfIdle();
        var inputs = HarmonyLib.AccessTools.Field(typeof(Waylith.Rendering.PostFx.PostEffectPass), "inputTextures");
        Check(((Array)inputs.GetValue(scene)).GetValue(0) == null && ((Array)inputs.GetValue(scene)).GetValue(1) == null
            && ((Array)inputs.GetValue(output)).GetValue(0) == null, "idle cleanup clears native input bindings without dereferencing a null RenderTexture");
        Check(ReferenceEquals(Get("renderers"), renderers), "hidden warmed clips keep pipeline caches after image cleanup");

        var frame = EfxFrame.Rent(); handle.Lease.Retain(); frame.Leases.Add(handle.Lease);
        frame.Retain(); frame.Dispose();
        Set("retained", new[] { frame, null });
        handle.Stop(); EfxPlayer.Drain(); pipeline.ReleaseIfIdle();
        Check(ReferenceEquals(Get("renderers"), renderers) && clip.Releases == 0, "stopping retains caches while a GPU frame is still in flight");
        Set("scratch", new[] { new List<EfxPass>(), new List<EfxPass>() });
        pipeline.BeginSlot(0); EfxPlayer.Drain(); pipeline.ReleaseIfIdle(); pipeline.ReleaseIfIdle();
        Check(Get("renderers") == null && clip.Releases == 1, "last slot retirement releases stopped caches once");
    }

    private static void Warmup()
    {
        Session();
        Check(!EfxPlayer.HasPlaying && EfxPlayer.CaptureWarmup() == null, "empty player has no loading warmup");
        var clip = new Clip("dormant warmup") { Enabled = false, UpdateEnabled = false };
        var handle = EfxPlayer.Play(clip);
        Check(EfxPlayer.HasPlaying, "submission publishes the cache ownership count");
        EfxPlayer.CaptureWarmup(); EfxPlayer.CaptureWarmup(); EfxPlayer.Capture();
        Check(clip.Warmups == 1 && clip.Builds == 0 && clip.Updates == 0,
            "loading warmup runs once for disabled dormant clips without presenting or updating");
        bool rejected = Task.Run(() => { try { EfxPlayer.CaptureWarmup(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
        Check(rejected, "loading material snapshots require the main thread");
        handle.Stop(); handle.Stop();
        Check(!EfxPlayer.HasPlaying, "idempotent stop releases cache ownership once");

        Session();
        var bad = new Clip("bad warmup") { FailWarmup = true }; var good = new Clip("good warmup");
        var badHandle = EfxPlayer.Play(bad); EfxPlayer.Play(good);
        int errors = Verse.Log.Errors.Count;
        EfxPlayer.CaptureWarmup(); EfxPlayer.CaptureWarmup(); EfxPlayer.Drain();
        Check(!badHandle.IsPlaying && bad.Releases == 1 && good.Warmups == 1 && Verse.Log.Errors.Count == errors + 1,
            "warmup failure stops only the failing clip and does not repeat");
        Verse.Current.Game = new(); EfxPlayer.SynchronizeSession();
        Check(!EfxPlayer.HasPlaying && EfxPlayer.CaptureWarmup() == null, "session change retires warm cache ownership");
    }

    private static void Snapshot()
    {
        int strength = 10, screen = RimUnity.Shader.PropertyToID("_ScreenParams"), main = RimUnity.Shader.PropertyToID("_MainTex");
        var scalar = new MaterialUniformSlot(0, MaterialUniformType.Float, "_Strength", strength, default, -1);
        var size = new MaterialUniformSlot(16, MaterialUniformType.Vector4, "_ScreenParams", screen, default, -1);
        var layout = new MaterialLayout(new[] { scalar }, new[] { new MaterialTextureSlot(0, 1, "_MainTex", main) }, 16,
            uniformBlocks: new[] { new MaterialUniformBlockSlot(20, 32, new[] { size }, Array.Empty<MaterialBlockArraySlot>()) });
        var shader = (Waylith.Rendering.Shader)RuntimeHelpers.GetUninitializedObject(typeof(Waylith.Rendering.Shader));
        typeof(Waylith.Rendering.Shader).GetField("sceneGrabResolved", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(shader, true);
        var binding = new MaterialBinding(1, 1, 1, "test", shader, layout, default, default, 2000,
            new ShaderVariantKey(shader, 0, "", true), PipelineState.Opaque, null, false);
        binding.UpdateStorageArrays(new[] { new MaterialStorageArray(20, EfxPass.NextGeneration(), new byte[32]) });
        var geometry = (MeshDrawGeometry)RuntimeHelpers.GetUninitializedObject(typeof(MeshDrawGeometry));
        var pass = new EfxPass(null, geometry, binding);
        pass.Apply(DrawProperty.Float(strength, .4f));
        var input = new FakeTexture();
        var bound = pass.BindInput(input, 1920, 1080);
        pass.Apply(DrawProperty.Float(strength, .9f));
        Check(bound.Binding.Uniforms.ReadFloat(0) == .4f, "captured scalar survives later mutation");
        Check(bound.Binding.Resources[0] == input && pass.Binding.Resources[0] == null, "scene input binding does not mutate capture");
        Check(BitConverter.ToSingle(bound.Binding.StorageArrays[0].Bytes, 16) == 1920 &&
            BitConverter.ToSingle(pass.Binding.StorageArrays[0].Bytes, 16) == 0, "uniform block bytes are isolated and use render dimensions");
        Check(bound.Binding.StorageArrays[0].Generation != pass.Binding.StorageArrays[0].Generation, "per-frame blocks invalidate host storage cache");
        var draw = bound.CreateDraw();
        Check(draw.HasOverride && draw.Resources[0] == input, "draw uses per-frame descriptors instead of persistent material cache");
        FinalDrawItem.Return(draw);
        var resized = pass.BindInput(new FakeTexture(), 1280, 720);
        Check(BitConverter.ToSingle(resized.Binding.StorageArrays[0].Bytes, 20) == 720 &&
            BitConverter.ToSingle(bound.Binding.StorageArrays[0].Bytes, 20) == 1080, "resize does not modify prior frame");
    }

    private static void Session() { EfxPlayer.Clear(); Verse.Current.Game = new(); Verse.Find.CurrentMap = new(); }
    private static void BufferBudgets()
    {
        using var frame = new EfxFrame();
        var small = EfxBufferBudget.ForFrame(frame, 256, 256);
        Check(small.UniformBytes == 65536 && small.StorageBytes == 131072 && small.ReservedBytes == 476 * 1024,
            "small renderer starts with bounded upload/cache allocations");
        int uniformAlignment = 256, descriptors = 2048, uniforms = 4194304, instances = 8388608;
        int storageAlignment = 256, storage = 16777216, indirect = 65536;
        EfxRendererFactory.ConfigureEncoder(ref uniformAlignment, ref descriptors, ref uniforms, ref instances,
            ref storageAlignment, ref storage, ref indirect);
        Check(uniforms == 4194304 && storage == 16777216 && instances == 8388608, "native allocation arguments are unchanged outside a Clip scope");
        using (EfxRendererFactory.Enter(small))
        {
            EfxRendererFactory.ConfigureEncoder(ref uniformAlignment, ref descriptors, ref uniforms, ref instances,
                ref storageAlignment, ref storage, ref indirect);
            Check(uniforms == 65536 && storage == 131072 && instances == 4096 && indirect == 4096 && descriptors == 128,
                "Clip construction receives the reduced capacities");
            int cache = 8388608, alignment = 256;
            EfxRendererFactory.ConfigureCache("PersistentMaterialStorage", ref cache, ref alignment);
            Check(cache == 262144, "persistent storage retains conservative double-buffer headroom");
            cache = 12345; EfxRendererFactory.ConfigureCache("OtherCache", ref cache, ref alignment);
            Check(cache == 12345, "unrelated cache labels retain their capacity");
            bool isolated = Task.Run(() =>
            {
                int a = 256, b = 2048, c = 4194304, d = 8388608, e = 256, f = 16777216, g = 65536;
                EfxRendererFactory.ConfigureEncoder(ref a, ref b, ref c, ref d, ref e, ref f, ref g);
                return c == 4194304 && f == 16777216;
            }).GetAwaiter().GetResult();
            Check(isolated, "construction scope cannot alter allocations on another thread");
            using (EfxRendererFactory.Enter(small with { UniformBytes = 131072 })) { }
            uniforms = 0;
            EfxRendererFactory.ConfigureEncoder(ref uniformAlignment, ref descriptors, ref uniforms, ref instances,
                ref storageAlignment, ref storage, ref indirect);
            Check(uniforms == 65536, "nested scopes restore the preceding budget");
        }
        uniforms = 4194304;
        EfxRendererFactory.ConfigureEncoder(ref uniformAlignment, ref descriptors, ref uniforms, ref instances,
            ref storageAlignment, ref storage, ref indirect);
        Check(uniforms == 4194304, "scope disposal restores native behavior");

        var shader = (Waylith.Rendering.Shader)RuntimeHelpers.GetUninitializedObject(typeof(Waylith.Rendering.Shader));
        var binding = new MaterialBinding(5, 5, 1, "large budget", shader, MaterialLayout.Empty(), default, default, 2000,
            new ShaderVariantKey(shader, 0, "", true), PipelineState.Opaque, null, false);
        binding.UpdateStorageArrays(new[] { new MaterialStorageArray(9, 1, new byte[600000]) });
        frame.Passes.Add(new EfxPass(null, null, binding));
        var large = EfxBufferBudget.ForFrame(frame, 256, 256);
        Check(large.StorageBytes >= 600000 + 16384 && !small.Covers(large) && large.Covers(small),
            "large material payload requests safe growth before any draw");
        var aligned = EfxBufferBudget.ForFrame(frame, 1024, 512);
        Check(aligned.UniformAlignment == 1024 && aligned.StorageAlignment == 512 && !large.Covers(aligned),
            "device alignment changes require a new slot budget");
        var context = new EfxRenderContext();
        Check(context.FramesInFlight == 1 && context.SubmissionFrameIndex == 0, "each private renderer owns one fence slot");
        const string id = "RimUnity.ScreenEfx.CapacityTests";
        var harmony = new HarmonyLib.Harmony(id);
        try
        {
            foreach (var type in typeof(EfxRendererFactory).GetNestedTypes(BindingFlags.NonPublic))
                if (type.GetCustomAttribute<HarmonyLib.HarmonyPatch>() != null) harmony.CreateClassProcessor(type).Patch();
            Check(EfxRendererFactory.HooksInstalled, "allocation hooks patch the actual host constructor signatures");
        }
        finally { harmony.UnpatchAll(id); }
        Check(!EfxRendererFactory.HooksInstalled, "missing allocation hooks are detected instead of silently using large defaults");
    }
    private static void Benchmark()
    {
        Session();
        Measure("empty update/capture", () => { EfxPlayer.Update(default); EfxPlayer.Capture()?.Dispose(); });
        var hidden = new Clip("hidden") { Enabled = false }; var handle = EfxPlayer.Play(hidden);
        Measure("hidden update/capture", () => { EfxPlayer.Update(default); EfxPlayer.Capture()?.Dispose(); });
        hidden.Enabled = true;
        Measure("empty visible capture", () => EfxPlayer.Capture()?.Dispose());
        Session();
        int screen = RimUnity.Shader.PropertyToID("_ScreenParams");
        var layout = new MaterialLayout(Array.Empty<MaterialUniformSlot>(), Array.Empty<MaterialTextureSlot>(), 0,
            uniformBlocks: new[] { new MaterialUniformBlockSlot(20, 32,
                new[] { new MaterialUniformSlot(16, MaterialUniformType.Vector4, "_ScreenParams", screen, default, -1) }, Array.Empty<MaterialBlockArraySlot>()) });
        var shader = (Waylith.Rendering.Shader)RuntimeHelpers.GetUninitializedObject(typeof(Waylith.Rendering.Shader));
        var binding = new MaterialBinding(2, 2, 1, "bench", shader, layout, default, default, 2000,
            new ShaderVariantKey(shader, 0, "", true), PipelineState.Opaque, null, false);
        binding.UpdateStorageArrays(new[] { new MaterialStorageArray(20, EfxPass.NextGeneration(), new byte[32]) });
        var pass = new EfxPass(null, null, binding); var input = new FakeTexture();
        var scratch = new EfxPass(); var second = new EfxPass();
        Measure("one input bind", () => pass.BindInput(input, 1920, 1080, scratch));
        Measure("two input binds", () => { pass.BindInput(input, 1920, 1080, scratch); pass.BindInput(input, 1920, 1080, second); });
    }
    private static void Measure(string name, Action action)
    {
        const int count = 10000;
        for (int i = 0; i < 1000; i++) action();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++) action();
        long end = System.Diagnostics.Stopwatch.GetTimestamp();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(allocated == 0, name + " stays allocation-free after warmup in the fixture");
        Console.WriteLine($"BENCH {name}: {allocated / (double)count:F1} B/op; {(end - start) * 1000000.0 / System.Diagnostics.Stopwatch.Frequency / count:F3} us/op");
    }

    private static void Pooling()
    {
        Session();
        var sleeping = new Clip("sleeping") { Enabled = false, UpdateEnabled = false };
        EfxPlayer.Play(sleeping); EfxPlayer.Update(default);
        Check(sleeping.Updates == 0, "dormant clips skip Update");
        sleeping.UpdateEnabled = true; EfxPlayer.Update(default);
        Check(sleeping.Updates == 1 && sleeping.Builds == 0, "business event can wake a hidden clip without drawing");

        Session();
        var clip = new Clip("pooled"); var handle = EfxPlayer.Play(clip);
        var frame = EfxFrame.Rent(); frame.Retain();
        handle.Lease.Retain(); frame.Leases.Add(handle.Lease);
        var captured = frame.AcquirePass();
        var shader = (Waylith.Rendering.Shader)RuntimeHelpers.GetUninitializedObject(typeof(Waylith.Rendering.Shader));
        int screen = RimUnity.Shader.PropertyToID("_ScreenParams");
        var block = new MaterialUniformBlockSlot(30, 32,
            new[] { new MaterialUniformSlot(16, MaterialUniformType.Vector4, "_ScreenParams", screen, default, -1) }, Array.Empty<MaterialBlockArraySlot>());
        var stable = new MaterialUniformBlockSlot(31, 16, Array.Empty<MaterialUniformSlot>(), Array.Empty<MaterialBlockArraySlot>());
        var layout = new MaterialLayout(Array.Empty<MaterialUniformSlot>(), Array.Empty<MaterialTextureSlot>(), 0, uniformBlocks: new[] { block, stable });
        var binding = new MaterialBinding(3, 3, 1, "pool", shader, layout, default, default, 2000,
            new ShaderVariantKey(shader, 0, "", true), PipelineState.Opaque, null, false);
        var bytes = new byte[32]; bytes[0] = 7;
        binding.UpdateStorageArrays(new[] { new MaterialStorageArray(30, 1, bytes), new MaterialStorageArray(31, 1, new byte[16]) });
        captured.Capture(handle.Lease, null, binding); frame.Passes.Add(captured);
        bytes[0] = 9;
        Check(captured.Binding.StorageArrays[0].Bytes[0] == 7, "capture owns a copy of the host block ring");
        frame.Dispose();
        var another = EfxFrame.Rent();
        Check(!ReferenceEquals(frame, another), "GPU-held frames cannot be rented again");
        another.Dispose(); handle.Stop();
        Check(clip.Releases == 0, "pooled payload waits for GPU ownership");
        frame.Drop(); EfxPlayer.Drain();
        Check(clip.Releases == 1 && captured.Lease == null && captured.Mesh == null, "returned frames clear gameplay references");
        var reused = EfxFrame.Rent();
        Check(ReferenceEquals(frame, reused) && reused.Passes.Count == 0 && reused.Leases.Count == 0, "retired frame lists are reused empty");
        var reusedPass = reused.AcquirePass(); reusedPass.Capture(null, null, binding);
        Check(ReferenceEquals(reusedPass, captured) && reusedPass.Binding.StorageArrays[0].Bytes[0] == 9, "pass buffers are refreshed on reuse");
        var scratch = new EfxPass(); var input = new FakeTexture();
        reusedPass.BindInput(input, 1920, 1080, scratch);
        var buffer = scratch.Binding.StorageArrays[0].Bytes;
        reusedPass.BindInput(input, 1280, 720, scratch);
        Check(ReferenceEquals(buffer, scratch.Binding.StorageArrays[0].Bytes) && BitConverter.ToSingle(buffer, 20) == 720,
            "slot scratch reuses storage while updating dimensions");
        Check(BitConverter.ToSingle(reusedPass.Binding.StorageArrays[0].Bytes, 20) == 0, "scratch updates preserve captured blocks");
        Check(ReferenceEquals(reusedPass.Binding.StorageArrays[1].Bytes, scratch.Binding.StorageArrays[1].Bytes),
            "immutable blocks without dimensions avoid a second copy");
        scratch.ClearReferences();
        Check(scratch.Binding.StorageArrays[1].Bytes == null && reusedPass.Binding.StorageArrays[1].Bytes != null,
            "slot cleanup drops borrowed buffers without clearing the captured frame");
        reused.Dispose();

        EfxFrame.ClearPool();
        var old = EfxFrame.Rent(); EfxFrame.ClearPool(); old.Dispose();
        Check(EfxFrame.CachedFrameCount == 0, "late returns cannot repopulate a previous session's pool");
        var many = Enumerable.Range(0, 12).Select(_ => EfxFrame.Rent()).ToArray();
        foreach (var item in many) item.Dispose();
        Check(EfxFrame.CachedFrameCount == 8, "frame pool has a hard capacity");
        EfxFrame.ClearPool();
        var oversized = EfxFrame.Rent();
        for (int i = 0; i < 65; i++) oversized.AcquirePass();
        oversized.Dispose();
        Check(EfxFrame.CachedFrameCount == 0, "unusually large pass lists are not cached");

        var gpuClip = new Clip("render retirement"); var gpuHandle = EfxPlayer.Play(gpuClip);
        var gpuFrame = EfxFrame.Rent(); gpuFrame.Retain();
        gpuHandle.Lease.Retain(); gpuFrame.Leases.Add(gpuHandle.Lease);
        gpuFrame.Dispose(); gpuHandle.Stop();
        Task.Run(gpuFrame.Drop).GetAwaiter().GetResult();
        Check(gpuClip.Releases == 0, "render retirement only queues the release callback");
        EfxPlayer.Drain(); Check(gpuClip.Releases == 1, "release callback executes when the main thread drains");
    }
    private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    private static void Throws(Action action, string message) { try { action(); } catch (InvalidOperationException) { assertions++; return; } throw new Exception(message); }
    private sealed class FakeTexture : Waylith.Rendering.Texture { public override int width { get; set; } = 1; public override int height { get; set; } = 1; }
    private sealed class Clip(string name, List<string> order = null) : EfxClip
    {
        internal int Updates, Builds, Releases, Warmups;
        internal float Value, CapturedValue;
        internal bool FailUpdate, FailBuild, FailWarmup;
        internal EfxUpdateContext Last;
        public override void Update(EfxUpdateContext context) { Updates++; Last = context; if (FailUpdate) throw new Exception("update"); }
        public override void Warmup(EfxFrameBuilder frame) { Warmups++; if (FailWarmup) throw new Exception("warmup"); }
        public override void BuildFrame(EfxFrameBuilder frame) { Builds++; if (FailBuild) throw new Exception("build"); CapturedValue = Value; order?.Add(name); }
        protected internal override void OnRelease() => Releases++;
    }
}
