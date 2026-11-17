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
        NightOverlay(); NextSliceAfterEmptyTail(); MultipleLightsAndSlots(); MissingLoadAttachment(); BorrowedDrawCleanup();
        Console.WriteLine($"PASS {checks} HDR light lifecycle checks (recording boundary; no GPU/game).");
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
        var other = new RenderTexture(8, 8, VkFormat.B8G8R8A8UNorm);
        target.RecordLights(ctx, other, light, 2000, 2500);
        var changed = ctx.CommandBuffer.CompositeFramebuffers.Last();
        Check(!changed.Equals(first) && ctx.Device.Destroyed.Count == 0, "changed color view gets a new live framebuffer");
        target.RecordLights(ctx, f.Scenes.Current(0), light, 2000, 2500);
        Check(ctx.CommandBuffer.CompositeFramebuffers.Last().Equals(first), "returning to a view reuses its live framebuffer");
        f.Context.SubmissionFrameIndex = 1;
        target.RecordLights(f.Ctx, f.Scenes.Current(1), light, 2000, 2500);
        Check(ctx.Device.Destroyed.Count == 0, "another frame slot does not retire existing framebuffers");
        int created = ctx.Device.Created.Count;
        ctx.Device.FinishRecording(); target.RebuildTargets(f.Context);
        Check(ctx.Device.Destroyed.Count == created, "rebuild releases every cached framebuffer once");
        target.RecordLights(f.Ctx, f.Scenes.Current(1), light, 2000, 2500);
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
