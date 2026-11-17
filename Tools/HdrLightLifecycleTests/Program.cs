using System.Reflection;
using Brutal.VulkanApi;
using RimUnity.Rendering;
using RimWorldWaylith.Rendering;
using Waylith.Rendering;

// Execute production hooks, partitioning and targets against a recording host/GPU boundary.
// Checks command ordering and object lifetime, not shader math or actual GPU pixels.
internal static class Program
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    private static int checks;
    private static MethodInfo Hook(string type, string method) => typeof(HdrMapHooks).GetNestedType(type, BindingFlags.NonPublic).GetMethod(method, Flags);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }
    private static void Main()
    {
        NightOverlay(); TrailDoesNotDimWall(); InterleavedLayers(); EqualKeysKeepHostOrder(); LightFirst();
        NextSliceAfterEmptyTail(); MultipleLightsAndSlots(); MissingLoadAttachment(); BorrowedDrawCleanup();
        HostDepthLayout();
        TraceChecks();
        Console.WriteLine($"PASS {checks} HDR light lifecycle checks (recording boundary; no GPU/game).");
    }

    private static void HostDepthLayout()
    {
        // Host render passes leave depth/stencil in AttachmentOptimal without updating
        // GpuTextureAllocation.CurrentLayout. Reproduce the stale metadata from the field report.
        foreach (var tracked in new[] { VkImageLayout.Undefined, VkImageLayout.DepthStencilAttachmentOptimal })
        foreach (bool lightFirst in new[] { false, true })
        {
            using var f = new Fixture();
            var scene = f.Scenes.Current(0);
            scene.DepthFormat = VkFormat.D32SFloatS8UInt;
            scene.Depth = new Waylith.Graphics.GpuTextureAllocation(scene)
                { Format = scene.DepthFormat, CurrentLayout = tracked };
            if (!lightFirst) f.Add(1000, .6f);
            f.Add(2000, 0, light: true);
            f.Add(3000, .5f, multiply: true);
            f.Add(4000, .1f, light: true);
            f.Capture(); var ctx = f.Ctx;
            f.Slice(ctx, true, int.MinValue, 3500);
            f.Slice(ctx, false, 3500, int.MaxValue);
            Check(!ctx.CommandBuffer.ImageBarriers.Any(b =>
                b.OldLayout == VkImageLayout.Undefined || b.NewLayout == VkImageLayout.Undefined),
                $"{tracked}, lightFirst={lightFirst}: light runs must neither discard host depth nor restore Undefined");
            Check(scene.Depth.CurrentLayout == VkImageLayout.DepthStencilAttachmentOptimal,
                "depth tracking reflects the host render pass final layout after all light/shell slices");
            Check(ctx.Device.DepthAttachments.Count > 0 && ctx.Device.DepthAttachments.All(a =>
                a.LoadOp == VkAttachmentLoadOp.Load && a.StoreOp == VkAttachmentStoreOp.Store &&
                a.StencilLoadOp == VkAttachmentLoadOp.Load && a.StencilStoreOp == VkAttachmentStoreOp.Store &&
                a.InitialLayout == VkImageLayout.DepthStencilAttachmentOptimal &&
                a.FinalLayout == VkImageLayout.DepthStencilAttachmentOptimal),
                "production light render pass loads/stores both depth and stencil in the host layout");
        }
    }

    private static void TraceChecks()
    {
        string Report(RimUnity.Diagnostics.RenderTraceBuffer buffer)
        { var text = new System.Text.StringBuilder(); buffer.AppendTo(text); return text.ToString(); }
        var bounded = new RimUnity.Diagnostics.RenderTraceBuffer(maxEvents: 3, maxChars: 1024, maxDetail: 64);
        bounded.Record("test", "inactive");
        Check(Report(bounded).Contains("events=0"), "inactive trace records no events");
        bounded.Start(); bounded.Record("test", "baseline");
        for (int i = 0; i < 10; i++) bounded.Record("test", "event-" + i);
        string overflow = Report(bounded);
        Check(overflow.Contains("baseline") && overflow.Contains("event-9") && overflow.Contains("droppedOldest=8"),
            "bounded trace preserves baseline and latest events, reporting evictions");
        bounded.Record("long", new string('x', 1000));
        Check(Report(bounded).Contains("DETAIL TRUNCATED") && Report(bounded).Contains("clippedDetails=1"), "oversized diagnostic text is explicitly truncated");
        bounded.Stop(); string stopped = Report(bounded); bounded.Record("test", "after-stop");
        Check(Report(bounded) == stopped, "stopped trace remains readable and immutable");
        bounded.Start(); Parallel.For(0, 200, i => bounded.Record("parallel", "event-" + i)); bounded.Stop();
        Check(Report(bounded).Contains("events=200; retained=3; droppedOldest=197"), "main/render thread events serialize without exceeding capacity");
        bounded.Clear(); Check(Report(bounded).Contains("events=0") && !Report(bounded).Contains("event-199"), "new capture cannot retain previous scene text");

        long tick = 0;
        var sampled = new RimUnity.Diagnostics.RenderTraceBuffer(sampleBursts: true, timestamp: () => tick);
        sampled.Start();
        for (int frame = 0; frame < 2800; frame++)
        {
            tick = frame * System.Diagnostics.Stopwatch.Frequency / 140;
            string detail = "frame=" + frame + "; " + new string('x', 350);
            for (int n = 0; n < 20; n++) sampled.Record("ParticleSubmit", detail);
            for (int n = 0; n < 16; n++) sampled.Record("HdrRun", detail);
            for (int n = 0; n < 8; n++) sampled.Record("HdrLightPass", detail);
            for (int n = 0; n < 14; n++) sampled.Record("stage" + n, detail);
            if (frame % 70 == 0)
            {
                sampled.Record("HdrMaterials", detail + new string('m', 15000));
                sampled.Record("HdrPlan", detail + new string('p', 15000));
            }
        }
        sampled.Record("HdrRunError", "first-error"); sampled.Record("HdrRunError", "second-error");
        sampled.Record("HdrRunError", "third-error"); sampled.Stop();
        string sustained = Report(sampled);
        Check(sustained.Contains("frame=0;") && sustained.Contains("frame=2730;") && sustained.Contains("droppedOldest=0"),
            "140 fps particle-heavy capture retains observations throughout all 20 seconds");
        Check(sustained.Contains("sampledOut=") && !sustained.Contains("sampledOut=0") && sustained.Contains("third-error") && sustained.Contains("clippedDetails=0"),
            "sampling is explicit, leaves complete detail rows and does not suppress errors");
        sampled.Start(); sampled.Record("ParticleSubmit", "new-capture"); sampled.Stop();
        Check(Report(sampled).Contains("new-capture") && Report(sampled).Contains("sampledOut=0"),
            "sampling budgets and counters reset between captures");

        var trace = RimUnity.Diagnostics.RenderDebugReport.Trace;
        trace.Start(); RimUnity.Diagnostics.RenderDebugReport.Collecting = true;
        try
        {
            using var f = new Fixture();
            f.Add(1000, .6f); f.Add(2000, 0, light: true); f.Add(3000, .5f, multiply: true); f.Add(4000, .1f, light: true);
            f.Capture(); f.Slice(f.Ctx, true, int.MinValue, int.MaxValue);
            string report = Report(trace);
            Check(report.Contains("requested=Experiment; route=Experiment") && report.Contains("captured=2"), "trace identifies actual frame takeover and capture counts");
            Check(report.Contains("classification=light") && report.Contains("run=1 LIGHT") && report.Contains("run=2 SHELL"), "trace includes material classification and interleaved boundaries");
            Check(report.Contains("HdrLightPass") && report.Contains("phase=completed") && report.Contains("/view=0x") && report.Contains("hostBindingsInvalidated=True"), "trace covers target handles, completion and binding recovery");
            Check(Math.Abs(f.Scenes.Current(0).Value - .4f) < .0001f, "enabling diagnostics does not change recorded rendering result");
            trace.Clear(); trace.Start();
            using var fallback = new Fixture(); fallback.Add(1000, .2f, light: true); fallback.Frame.PreserveSceneColor = true;
            fallback.Capture();
            Check(Report(trace).Contains("route=preserved-native"), "preserved/native frames remain visible to diagnostics");
        }
        finally { RimUnity.Diagnostics.RenderDebugReport.Collecting = false; trace.Clear(); }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly TestContext Context = new();
        internal readonly SceneColorTargets Scenes = new();
        internal readonly MapRenderFeature.MapMainPass Pass = new();
        internal readonly MeshDrawFrame Frame = new(1);
        internal FrameContext Ctx => new(Context, new CommandBuffer(Context.Device), Scenes.Current(Context.SubmissionFrameIndex));
        internal FinalDrawItem Add(int queue, float value, bool light = false, bool multiply = false)
        {
            var draw = new FinalDrawItem(queue, value, light, multiply);
            Frame.Draws.Add(draw); return draw;
        }
        internal void Capture() => Hook("Capture", "Postfix").Invoke(null, new object[] { Frame });
        internal void Slice(FrameContext ctx, bool clear, int min, int max)
        {
            var attachment = clear ? Scenes.Attachment : Scenes.LoadAttachment;
            object[] args = { Pass, Scenes, ctx, Frame, attachment, clear, min, max, null };
            bool native = (bool)Hook("Slice", "Prefix").Invoke(null, args);
            if (native) Pass.RecordSlice(ctx, (MeshDrawFrame)args[3], attachment, clear, min, max);
            Hook("Slice", "Postfix").Invoke(null, new[] { (object)Pass, ctx, args[8] });
        }
        public void Dispose()
        {
            Context.Device.FinishRecording();
            Hook("ReleaseFrame", "Prefix").Invoke(null, new object[] { Frame });
            Hook("Dispose", "Prefix").Invoke(null, new object[] { Pass });
        }
    }

    private static void NightOverlay()
    {
        using var f = new Fixture();
        var ground = f.Add(1000, .6f);
        f.Add(2000, .1f, light: true);
        var night = f.Add(3000, .5f, multiply: true);
        f.Capture(); var ctx = f.Ctx;
        f.Slice(ctx, true, int.MinValue, int.MaxValue);
        Check(Math.Abs(f.Scenes.Current(0).Value - .35f) < .0001f, "ground and light survive; night multiplies exactly once");
        Check(ctx.CommandBuffer.Clears == 1, "only the first scene pass clears");
        Check(ground.DrawCount == 1 && night.DrawCount == 1, "shell draws execute exactly once");
        Check(!ground.Disposed && !night.Disposed, "temporary shell frames do not dispose borrowed draws");
        Check(f.Pass.renderer.Invalidations > 0, "host bindings invalidated after external rendering");
    }

    private static void TrailDoesNotDimWall()
    {
        float WallPixel(bool trail)
        {
            using var f = new Fixture();
            f.Add(1000, .6f);
            // The tracer is elsewhere on the map: zero contribution at this wall pixel.
            if (trail) f.Add(2000, 0, light: true);
            var night = f.Add(3000, .5f, multiply: true);
            var wall = f.Add(4000, .1f, light: true);
            f.Capture(); f.Slice(f.Ctx, true, int.MinValue, int.MaxValue);
            Check(night.DrawCount == 1 && wall.DrawCount == 1, "interleaved night and wall light draw once");
            return f.Scenes.Current(0).Value;
        }
        float withoutTrail = WallPixel(false), withTrail = WallPixel(true);
        Check(Math.Abs(withoutTrail - .4f) < .0001f, "wall light follows night without a tracer");
        Check(Math.Abs(withTrail - withoutTrail) < .0001f,
            $"a distant tracer must not dim wall light: without={withoutTrail}, with={withTrail}");
    }

    private static void InterleavedLayers()
    {
        // Compare all three native layer behaviors, including draws in the same render queue.
        foreach (string layer in new[] { "night/fog multiply", "transparent", "opaque" })
        foreach (bool sameQueue in new[] { false, true })
        {
            float Render(bool capture)
            {
                using var f = new Fixture();
                var ground = f.Add(1000, .6f);
                var first = f.Add(2000, .1f, light: true);
                var middle = f.Add(3000, .4f, multiply: layer == "night/fog multiply");
                if (layer == "transparent") middle.Alpha = .3f;
                var second = f.Add(4000, .2f, light: true);
                var fog = f.Add(5000, .7f, multiply: true);
                var third = f.Add(6000, .05f, light: true);
                var tail = f.Add(7000, .9f, multiply: true);
                if (sameQueue) foreach (var draw in f.Frame.Draws) draw.RenderQueue = 3000;
                if (capture) f.Capture();
                var ctx = f.Ctx;
                f.Slice(ctx, true, int.MinValue, int.MaxValue);
                Check(ctx.CommandBuffer.DrawOrder.SequenceEqual(new[] { ground, first, middle, second, fog, third, tail }),
                    $"{layer}, sameQueue={sameQueue}: preserve every draw's native position");
                Check(f.Frame.Draws.All(d => d.DrawCount == 1 && !d.Disposed), "all interleaved draws execute once and remain owned by source");
                if (capture)
                    Check(ctx.CommandBuffer.CompositeFramebuffers.Count == 3 && ctx.CommandBuffer.CompositeFramebuffers.Distinct().Count() == 1,
                        "three separated light runs safely reuse one scene framebuffer");
                return f.Scenes.Current(0).Value;
            }
            Check(Math.Abs(Render(true) - Render(false)) < .0001f, $"{layer}, sameQueue={sameQueue}: HDR preserves native layer ordering");
        }
    }

    private static void EqualKeysKeepHostOrder()
    {
        using var f = new Fixture();
        var ground = f.Add(1000, .6f);
        var first = new FinalDrawItem(2000, .1f, true, false);
        f.Frame.PersistentDraws.Add(first);
        var night = f.Add(2000, .5f, multiply: true);
        var last = f.Add(3000, .2f, light: true);
        f.Capture(); var ctx = f.Ctx;
        f.Slice(ctx, true, int.MinValue, int.MaxValue);
        Check(ctx.CommandBuffer.DrawOrder.SequenceEqual(new[] { ground, first, night, last }), "equal sort keys keep persistent draw before dynamic draw");
        Check(Math.Abs(f.Scenes.Current(0).Value - .55f) < .0001f, "equal-key shell cannot move across a captured light");
    }

    private static void LightFirst()
    {
        using var f = new Fixture();
        var first = f.Add(1000, .2f, light: true);
        var night = f.Add(2000, .5f, multiply: true);
        var last = f.Add(3000, .1f, light: true);
        f.Capture(); var ctx = f.Ctx;
        f.Slice(ctx, true, int.MinValue, int.MaxValue);
        Check(ctx.CommandBuffer.Clears == 1 && Math.Abs(f.Scenes.Current(0).Value - .2f) < .0001f,
            "light-first slice clears once then alternates light and shell");
        Check(ctx.CommandBuffer.DrawOrder.SequenceEqual(new[] { first, night, last }), "empty leading shell does not skip or reorder later draws");
    }

    private static void NextSliceAfterEmptyTail()
    {
        using var f = new Fixture();
        f.Add(1000, .6f); f.Add(2000, .1f, light: true);
        var night = f.Add(3000, .5f, multiply: true);
        f.Capture(); var ctx = f.Ctx;
        f.Slice(ctx, true, int.MinValue, 2500);
        Check(f.Pass.renderer.Invalidations > 0, "empty after-light tail still invalidates host bindings");
        f.Slice(ctx, false, 2500, int.MaxValue);
        Check(Math.Abs(f.Scenes.Current(0).Value - .35f) < .0001f && night.DrawCount == 1,
            "later native-only slice resumes with valid bindings and preserved pixels");
    }

    private static void MultipleLightsAndSlots()
    {
        using var f = new Fixture();
        f.Add(1000, .6f); f.Add(2000, .1f, light: true); f.Add(3000, .1f, light: true); f.Add(4000, .5f, multiply: true);
        f.Capture(); var ctx = f.Ctx;
        f.Slice(ctx, true, int.MinValue, 2500);
        var first = ctx.CommandBuffer.CompositeFramebuffers.Single();
        f.Slice(ctx, false, 2500, int.MaxValue);
        Check(ctx.CommandBuffer.CompositeFramebuffers.Count == 2 && ctx.CommandBuffer.CompositeFramebuffers[1].Equals(first), "two slices reuse the composite framebuffer");
        Check(Math.Abs(f.Scenes.Current(0).Value - .4f) < .0001f, "two light slices accumulate before the night overlay");
        Check(ctx.Device.Destroyed.Count == 0, "no framebuffer is destroyed while recording slices");
        var target = HdrMapHooks.WarmupLightTarget(f.Pass);
        using var light = new HdrLightFrame(f.Frame);
        var lightRun = light.Lights.Frame.Draws.Where(d => d.RenderQueue == 2000).ToList();
        var other = new RenderTexture(8, 8, VkFormat.B8G8R8A8UNorm);
        target.RecordLights(ctx, other, light.Lights.Frame, lightRun);
        var changed = ctx.CommandBuffer.CompositeFramebuffers.Last();
        Check(!changed.Equals(first) && ctx.Device.Destroyed.Count == 0, "changed color view gets a new live framebuffer");
        target.RecordLights(ctx, f.Scenes.Current(0), light.Lights.Frame, lightRun);
        Check(ctx.CommandBuffer.CompositeFramebuffers.Last().Equals(first), "returning to a view reuses its live framebuffer");
        f.Context.SubmissionFrameIndex = 1;
        target.RecordLights(f.Ctx, f.Scenes.Current(1), light.Lights.Frame, lightRun);
        Check(ctx.Device.Destroyed.Count == 0, "another frame slot does not retire existing framebuffers");
        int created = ctx.Device.Created.Count;
        ctx.Device.FinishRecording(); target.RebuildTargets(f.Context);
        Check(ctx.Device.Destroyed.Count == created, "rebuild releases every cached framebuffer once");
        target.RecordLights(f.Ctx, f.Scenes.Current(1), light.Lights.Frame, lightRun);
        Check(ctx.Device.Created.Count > created, "rendering recreates targets after rebuild");
        ctx.Device.FinishRecording();
    }

    private static void MissingLoadAttachment()
    {
        using var f = new Fixture();
        f.Add(1000, .6f); f.Add(2000, .1f, light: true); f.Add(3000, .5f, multiply: true);
        f.Capture(); f.Scenes.LoadAttachment = null;
        f.Slice(f.Ctx, true, int.MinValue, int.MaxValue);
        Check(Math.Abs(f.Scenes.Current(0).Value - .35f) < .0001f, "missing load attachment falls back to complete native drawing");
        Check(f.Context.Device.Created.Count == 0, "fallback does not allocate light targets");
    }

    private static void BorrowedDrawCleanup()
    {
        using var f = new Fixture();
        var draw = f.Add(3000, .5f, multiply: true);
        f.Pass.renderer.ThrowOnDraw = true;
        bool failed = false;
        try { HdrMapHooks.WarmupLightTarget(f.Pass).DrawShellItems(f.Ctx, f.Scenes.LoadAttachment, f.Pass.renderer, 1, f.Frame, new() { draw }); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && !draw.Disposed, "exception cleanup does not dispose borrowed draws");
    }
}
