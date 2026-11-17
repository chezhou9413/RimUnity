using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Brutal.Numerics;
using Brutal.VulkanApi;
using HarmonyLib;
using Waylith.Graphics;
using Waylith.Rendering;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering;

//在地图原有队列切片中切换浮点附件，保留水面、抓屏和其他地图阶段的调用顺序。
internal static class HdrMapHooks
{
    private static readonly Type MapPassType = AccessTools.TypeByName("RimWorldWaylith.Rendering.MapRenderFeature+MapMainPass")
        ?? throw new TypeLoadException("宿主缺少地图颜色绘制入口。");
    private static readonly ConditionalWeakTable<MeshDrawFrame, HdrDrawFrame> Frames = new();
    private static readonly ConditionalWeakTable<MeshDrawFrame, HdrLightFrame> LightFrames = new();
    private static readonly ConditionalWeakTable<object, HdrDrawTarget> Targets = new();
    private static readonly ConditionalWeakTable<object, HdrLightTarget> LightTargets = new();
    private static readonly ConditionalWeakTable<NativeTarget, Output> Outputs = new();
    private sealed record Output(int Frame, NativeTarget Texture);
    private static readonly FieldInfo MapRendererField = AccessTools.Field(MapPassType, "renderer")
        ?? throw new MissingFieldException(MapPassType.FullName, "renderer");
    private sealed class SliceState
    {
        internal HdrLightFrame Light;
        internal NativeTarget Scene;
        internal List<HdrLightFrame.DrawRun> Runs;
        internal int NextRun;
        internal MeshDrawFrame Under;
        internal PassAttachment LoadAttachment;
    }
    private static readonly Func<object, int, NativeTarget> Current = MakeCurrent();
    private static readonly Func<object, PassAttachment> LoadAttachment = MakeLoadAttachment();
    internal static NativeTarget WarmupScene(object targets, int frame) => Current(targets, frame);
    internal static HdrDrawTarget WarmupTarget(object owner) => Targets.GetValue(owner, _ => new HdrDrawTarget("RimUnity.HdrMap"));
    internal static HdrLightTarget WarmupLightTarget(object owner) => LightTargets.GetValue(owner, _ => new HdrLightTarget("RimUnity.HdrLight"));

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

    private static Func<object, PassAttachment> MakeLoadAttachment()
    {
        var type = AccessTools.TypeByName("RimWorldWaylith.Rendering.SceneColorTargets");
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, PassAttachment>>(
            Expression.Property(Expression.Convert(target, type), "LoadAttachment"), target).Compile();
    }

    [HarmonyPatch]
    private static class Capture
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("RimWorldWaylith.Rendering.MapRenderFeature"), "CaptureFrame");
        private static void Postfix(object __result)
        {
            if (__result is not MeshDrawFrame frame) return;
            var mode = RimUnityMod.Bloom?.mapHdrMode ?? MapHdrMode.None;
            // BackdropAttachment 路径未接管；保留宿主原样绘制。
            if (frame.PreserveSceneColor)
            { Diagnostics.RenderDebugReport.HdrFrame(frame, mode, "preserved-native", 0); return; }
            HdrDrawFrame snapshot = null;
            HdrLightFrame light = null;
            // Capture on the main thread: toggles affect new frames only. Existing
            // snapshots retire normally; independent Bloom capture is not gated here.
            if (mode == MapHdrMode.Legacy && HdrDrawFrame.Needed(frame))
            { snapshot = new HdrDrawFrame(frame); Frames.Add(frame, snapshot); }
            else if (mode == MapHdrMode.Experiment && HdrLightFrame.Needed(frame))
            { light = new HdrLightFrame(frame); LightFrames.Add(frame, light); }
            Diagnostics.RenderDebugReport.HdrFrame(frame, mode, snapshot != null ? "Legacy" : light != null ? "Experiment" : "native",
                snapshot?.Frame.Draws.Count ?? light?.Lights.Frame.Draws.Count ?? 0);
            if (Diagnostics.RenderDebugReport.Collecting)
                Diagnostics.RenderDebugReport.Frame(frame, snapshot != null || light != null);
            Diagnostics.ParticleDebugWindow.CaptureFinalFrame(frame, snapshot?.Frame ?? light?.Lights.Frame);
        }
    }

    [HarmonyPatch(typeof(MeshDrawFrame), nameof(MeshDrawFrame.Dispose))]
    private static class ReleaseFrame
    {
        private static void Prefix(MeshDrawFrame __instance)
        {
            if (Frames.TryGetValue(__instance, out var snapshot))
            { Frames.Remove(__instance); snapshot.Dispose(); }
            if (LightFrames.TryGetValue(__instance, out var light))
            { LightFrames.Remove(__instance); light.Dispose(); }
        }
    }

    [HarmonyPatch]
    private static class Slice
    {
        private static MethodBase TargetMethod() => AccessTools.Method(MapPassType, "RecordSlice");
        private static bool Prefix(object __instance, object ___sceneTargets, in FrameContext ctx,
            ref MeshDrawFrame meshFrame, PassAttachment attachment, bool clear, int queueMin, int queueMax,
            ref SliceState __state)
        {
            bool legacy = Frames.TryGetValue(meshFrame, out var snapshot);
            bool experiment = LightFrames.TryGetValue(meshFrame, out var light);
            if (Diagnostics.RenderDebugReport.Collecting) Diagnostics.RenderDebugReport.Stage("MapSlice", "frame=" + meshFrame.FrameNumber + "; slot=" + ctx.SubmissionFrameIndex + "; format=" + ctx.ColorFormat + "; depth=" + ctx.DepthFormat + "; samples=" + ctx.SampleCount + "; HDR=" + (legacy ? "Legacy" : experiment ? "Experiment" : "off") + "; clear=" + clear + "; queue=" + queueMin + ".." + queueMax);
            if (meshFrame.PreserveSceneColor) return true;
            if (legacy)
            {
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
            if (!experiment) return true;
            if (!light.HasLights(queueMin, queueMax))
            {
                if (Diagnostics.RenderDebugReport.HdrTracing) Diagnostics.RenderDebugReport.Stage("HdrFallback", "frame=" + meshFrame.FrameNumber + "; queue=" + queueMin + ".." + queueMax + "; native: no lights in slice");
                return true;
            }
            //补画必须保留颜色和深度；宿主尚未提供 Load 附件时沿用完整原生帧。
            var loadAttachment = LoadAttachment(___sceneTargets);
            if (loadAttachment == null)
            {
                if (Diagnostics.RenderDebugReport.HdrTracing) Diagnostics.RenderDebugReport.Stage("HdrFallback", "frame=" + meshFrame.FrameNumber + "; native: missing LoadAttachment");
                return true;
            }
            var runs = light.Partition(queueMin, queueMax);
            Diagnostics.RenderDebugReport.HdrPlan(meshFrame.FrameNumber, runs);
            bool startsWithShell = !runs[0].Light;
            //宿主先画首段外壳，其余光与外壳由 Postfix 按原顺序交替记录。
            var under = HdrDrawFrame.Shell(light.Shell, MeshDrawFrame.RentList(), MeshDrawFrame.RentList());
            if (startsWithShell) under.PersistentDraws.AddRange(runs[0].Draws);
            __state = new SliceState
            {
                Light = light,
                Scene = Current(___sceneTargets, ctx.SubmissionFrameIndex),
                Runs = runs,
                NextRun = startsWithShell ? 1 : 0,
                Under = under,
                LoadAttachment = loadAttachment
            };
            meshFrame = under;
            //空 under 不能交给宿主 RecordPass：会把 MeshBatchRenderer 标成 sceneReady=false，整帧不再画。
            if (!startsWithShell)
            {
                if (clear) ClearScene(in ctx, light.Shell, attachment);
                return false;
            }
            return true;
        }

        private static void Postfix(object __instance, in FrameContext ctx, SliceState __state)
        {
            if (__state?.Light == null || __state.Scene == null) return;
            try
            {
                var target = LightTargets.GetValue(__instance, _ => new HdrLightTarget("RimUnity.HdrLight"));
                var hostRenderer = (MeshBatchRenderer)MapRendererField.GetValue(__instance);
                byte hostPassId = (byte)AccessTools.Property(MapPassType, "PassId").GetValue(__instance);
                if (Diagnostics.RenderDebugReport.HdrTracing && __state.NextRun > 0) Diagnostics.RenderDebugReport.Stage("HdrRun", "frame=" + __state.Light.Shell.FrameNumber + "; slot=" + ctx.SubmissionFrameIndex + "; run=0; initial shell completed by host");
                for (int i = __state.NextRun; i < __state.Runs.Count; i++)
                {
                    var run = __state.Runs[i];
                    if (Diagnostics.RenderDebugReport.HdrTracing) Diagnostics.RenderDebugReport.Stage("HdrRun", "frame=" + __state.Light.Shell.FrameNumber + "; slot=" + ctx.SubmissionFrameIndex + "; run=" + i + "; light=" + run.Light + "; draws=" + run.Draws.Count + "; begin");
                    if (run.Light)
                    {
                        try { target.RecordLights(in ctx, __state.Scene, __state.Light.Lights.Frame, run.Draws); }
                        //每段合成后都恢复宿主绑定，包括光为最后一段的情况。
                        finally { hostRenderer.InvalidateCommandBufferBindings(); }
                    }
                    else target.DrawShellItems(in ctx, __state.LoadAttachment, hostRenderer, hostPassId, __state.Light.Shell, run.Draws);
                    if (Diagnostics.RenderDebugReport.HdrTracing) Diagnostics.RenderDebugReport.Stage("HdrRun", "frame=" + __state.Light.Shell.FrameNumber + "; slot=" + ctx.SubmissionFrameIndex + "; run=" + i + "; completed; hostBindingsInvalidated=" + run.Light);
                }
            }
            catch (Exception error)
            {
                if (Diagnostics.RenderDebugReport.HdrTracing) Diagnostics.RenderDebugReport.Stage("HdrRunError", "frame=" + __state.Light.Shell.FrameNumber + "; " + error);
                throw;
            }
            finally
            {
                if (__state.Under != null)
                {
                    __state.Under.PersistentDraws.Clear();
                    __state.Under.Dispose();
                    __state.Under = null;
                }
            }
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
        private static void Prefix(object __instance, IRenderTarget __0)
        {
            if (__0 is not IRenderContext context) return;
            if (Targets.TryGetValue(__instance, out var target)) target.RebuildTargets(context);
            if (LightTargets.TryGetValue(__instance, out var light)) light.RebuildTargets(context);
        }
    }
    [HarmonyPatch]
    private static class Dispose
    {
        private static MethodBase TargetMethod() => AccessTools.Method(MapPassType, "Dispose");
        private static void Prefix(object __instance) => Release(__instance);
    }
    //地图绘制通道退出时释放其渲染器和附件。
    private static void Release(object owner)
    {
        if (Targets.TryGetValue(owner, out var target))
        { target.Dispose(); Targets.Remove(owner); }
        if (LightTargets.TryGetValue(owner, out var light))
        { light.Dispose(); LightTargets.Remove(owner); }
    }
}
