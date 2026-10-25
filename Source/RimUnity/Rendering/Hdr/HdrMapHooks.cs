using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Brutal.Numerics;
using Brutal.VulkanApi;
using HarmonyLib;
using Waylith.Rendering;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering;

//在地图原有队列切片中切换浮点附件，保留水面、抓屏和其他地图阶段的调用顺序。
internal static class HdrMapHooks
{
    private static readonly Type MapPassType = AccessTools.TypeByName("RimWorldWaylith.Rendering.MapRenderFeature+MapMainPass")
        ?? throw new TypeLoadException("宿主缺少地图颜色绘制入口。");
    private static readonly ConditionalWeakTable<MeshDrawFrame, HdrDrawFrame> Frames = new();
    private static readonly ConditionalWeakTable<object, HdrDrawTarget> Targets = new();
    private static readonly ConditionalWeakTable<NativeTarget, Output> Outputs = new();
    private sealed record Output(int Frame, NativeTarget Texture);
    private static readonly Func<object, int, NativeTarget> Current = MakeCurrent();
    internal static NativeTarget WarmupScene(object targets, int frame) => Current(targets, frame);
    internal static HdrDrawTarget WarmupTarget(object owner) => Targets.GetValue(owner, _ => new HdrDrawTarget("RimUnity.HdrMap"));

    //主场景和后处理共用同一个目标身份，帧号防止菜单或空帧取到旧画面。
    internal static NativeTarget LinearScene(NativeTarget scene, int frame)
        => Outputs.TryGetValue(scene, out var output) && output.Frame == frame ? output.Texture : null;

    private static Func<object, int, NativeTarget> MakeCurrent()
    {
        var type = AccessTools.TypeByName("RimWorldWaylith.Rendering.SceneColorTargets");
        var target = Expression.Parameter(typeof(object)); var frame = Expression.Parameter(typeof(int));
        return Expression.Lambda<Func<object, int, NativeTarget>>(
            Expression.Call(Expression.Convert(target, type), AccessTools.Method(type, "Current"), frame), target, frame).Compile();
    }

    [HarmonyPatch]
    private static class Capture
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("RimWorldWaylith.Rendering.MapRenderFeature"), "CaptureFrame");
        private static void Postfix(object __result)
        {
            if (__result is not MeshDrawFrame frame) return;
            HdrDrawFrame snapshot = null;
            // Capture on the main thread: toggles affect new frames only. Existing
            // snapshots retire normally; independent Bloom capture is not gated here.
            if (RimUnityMod.Bloom?.mapHdrEnabled == true && HdrDrawFrame.Needed(frame))
            { snapshot = new HdrDrawFrame(frame); Frames.Add(frame, snapshot); }
            if (Diagnostics.RenderDebugReport.Collecting) Diagnostics.RenderDebugReport.Frame(frame, snapshot != null);
            Diagnostics.ParticleDebugWindow.CaptureFinalFrame(frame, snapshot?.Frame);
        }
    }

    [HarmonyPatch(typeof(MeshDrawFrame), nameof(MeshDrawFrame.Dispose))]
    private static class ReleaseFrame
    {
        private static void Prefix(MeshDrawFrame __instance)
        {
            if (!Frames.TryGetValue(__instance, out var snapshot)) return;
            Frames.Remove(__instance); snapshot.Dispose();
        }
    }

    [HarmonyPatch]
    private static class Slice
    {
        private static MethodBase TargetMethod() => AccessTools.Method(MapPassType, "RecordSlice");
        private static bool Prefix(object __instance, object ___sceneTargets, in FrameContext ctx,
            MeshDrawFrame meshFrame, PassAttachment attachment, bool clear, int queueMin, int queueMax)
        {
            bool hdr = Frames.TryGetValue(meshFrame, out var snapshot);
            if (Diagnostics.RenderDebugReport.Collecting) Diagnostics.RenderDebugReport.Stage("MapSlice", "frame=" + meshFrame.FrameNumber + "; slot=" + ctx.SubmissionFrameIndex + "; format=" + ctx.ColorFormat + "; depth=" + ctx.DepthFormat + "; samples=" + ctx.SampleCount + "; HDR=" + hdr + "; clear=" + clear + "; queue=" + queueMin + ".." + queueMax);
            if (!hdr) return true;
            var scene = Current(___sceneTargets, ctx.SubmissionFrameIndex);
            var target = Targets.GetValue(__instance, _ => new HdrDrawTarget("RimUnity.HdrMap"));
            if (clear)
            {
                ClearScene(in ctx, meshFrame, attachment);
                target.Begin(in ctx, scene, meshFrame.FrameNumber, false);
                target.BindDepth(in ctx, scene);
            }
            target.Record(in ctx, scene, snapshot, queueMin, queueMax);
            target.WriteScene(in ctx, scene, meshFrame.FrameNumber);
            Outputs.AddOrUpdate(scene, new Output(meshFrame.FrameNumber, target.Result));
            return false;
        }
    }

    //清屏使用宿主指定的附件；背景保留模式的 LoadOp 仍由宿主决定。
    private static unsafe void ClearScene(in FrameContext ctx, MeshDrawFrame frame, PassAttachment attachment)
    {
        var target = attachment.MakeFrameContext(in ctx);
        var color = frame.BackgroundColor;
        VkClearValue* clears = stackalloc VkClearValue[2];
        clears[0] = new VkClearColorValue { Float32 = new float4(color.X, color.Y, color.Z, color.W) };
        clears[1] = new VkClearDepthStencilValue { Depth = 0, Stencil = 0 };
        var begin = new VkRenderPassBeginInfo
        {
            RenderPass = target.RenderPass, Framebuffer = target.Framebuffer, RenderArea = new VkRect2D(target.Extent),
            ClearValueCount = 2, ClearValues = clears
        };
        ctx.CommandBuffer.BeginRenderPass(in begin, VkSubpassContents.Inline);
        ctx.CommandBuffer.EndRenderPass();
    }

    [HarmonyPatch]
    private static class Rebuild
    {
        private static MethodBase TargetMethod() => AccessTools.Method(MapPassType, "OnRenderTargetRebuilt");
        private static void Prefix(object __instance) => Release(__instance);
    }
    [HarmonyPatch]
    private static class Dispose
    {
        private static MethodBase TargetMethod() => AccessTools.Method(MapPassType, "Dispose");
        private static void Prefix(object __instance) => Release(__instance);
    }
    //与宿主同步释放附件和渲染器，重建之后按新尺寸分配。
    private static void Release(object owner)
    {
        if (!Targets.TryGetValue(owner, out var target)) return;
        target.Dispose(); Targets.Remove(owner);
    }
}
