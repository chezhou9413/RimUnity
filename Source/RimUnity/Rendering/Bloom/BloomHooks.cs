using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld.Planet;
using RimWorldWaylith.Rendering;
using Verse;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;

namespace RimUnity.Rendering;

//扩展宿主已存在的场景后处理阶段，不替换地图渲染，也不改变其他模组的材质。
internal static class BloomHooks
{
    private static readonly ConditionalWeakTable<CompositePass, BloomPipeline> Pipelines = new();
    private static readonly ConditionalWeakTable<CompositePass, FailedComposite> FailedComposites = new();
    private static readonly BloomMapHandoff<MeshDrawFrame> Handoff = new();
    //签名预检失败时整段后处理停用；单个 CompositePass 构造失败只隔离该实例，重建后可重试。
    private static volatile bool interfaceCompatible = true;
    private static string lastHandshakeMiss;
    private static string lastPipelineError;
    private sealed class FailedComposite { }

    internal static bool Available => interfaceCompatible;
    internal static BloomMapHandoff<MeshDrawFrame> MapHandoff => Handoff;
    internal static string LastHandshakeMiss => lastHandshakeMiss;
    internal static string LastPipelineError => lastPipelineError;

    //宿主升级给构造追加参数后旧绑定会失效，启动时先确认当前引擎仍有编译期签名。
    internal static bool Initialize()
    {
        var harmony = new Harmony("Chezhou.RimUnity.Bloom");
        MethodInfo capture = null, record = null;
        var capturePatch = AccessTools.Method(typeof(BloomHooks), nameof(PatchCapture));
        var passesPatch = AccessTools.Method(typeof(BloomHooks), nameof(PatchPasses));
        try
        {
            var pass = AccessTools.TypeByName("Waylith.Rendering.PostFx.PostEffectPass");
            var constructor = pass?.GetConstructor(new[] {
                typeof(string), typeof(string), typeof(ReadOnlySpan<int>), typeof(BlendState),
                typeof(int), typeof(int), typeof(int), typeof(bool) });
            if (constructor == null)
                throw new MissingMethodException("Waylith.Rendering.PostFx.PostEffectPass", ".ctor");
            capture = AccessTools.Method(typeof(FrameRenderer), nameof(FrameRenderer.CaptureFrame))
                ?? throw new MissingMethodException(typeof(FrameRenderer).FullName, nameof(FrameRenderer.CaptureFrame));
            record = AccessTools.Method(typeof(FrameRenderer), nameof(FrameRenderer.RecordFrame))
                ?? throw new MissingMethodException(typeof(FrameRenderer).FullName, nameof(FrameRenderer.RecordFrame));
            harmony.Patch(capture, transpiler: new HarmonyMethod(capturePatch));
            harmony.Patch(record, transpiler: new HarmonyMethod(passesPatch));
            return true;
        }
        catch (Exception exception)
        {
            if (record != null) harmony.Unpatch(record, passesPatch);
            if (capture != null) harmony.Unpatch(capture, capturePatch);
            DisablePermanently(exception);
            return false;
        }
    }

    private static void DisablePermanently(Exception exception)
    {
        interfaceCompatible = false;
        lastPipelineError = exception.GetType().Name + ": " + exception.Message;
        Log.ErrorOnce("[RimUnity] 当前游戏引擎与 RimUnity 的渲染接口不匹配，场景后处理已禁用。"
            + "请更新游戏到包含当前渲染接口的版本，或更新 RimUnity。异常：" + lastPipelineError, 0x52455652);
    }

    private static void IsolateComposite(CompositePass composite, Exception exception)
    {
        lastPipelineError = exception.GetType().Name + ": " + exception.Message;
        try { FailedComposites.Add(composite, new FailedComposite()); }
        catch (ArgumentException) { }
        Log.ErrorOnce("[RimUnity] Bloom 后处理实例构造失败，已隔离该实例；渲染目标重建后会重试。异常："
            + lastPipelineError, 0x424C4F4D);
    }

    //每个后处理实例拥有自己的 GPU 缓冲区，随宿主实例销毁。
    internal static BloomPipeline Get(CompositePass composite)
    {
        if (!interfaceCompatible || composite == null) return null;
        if (FailedComposites.TryGetValue(composite, out _)) return null;
        try
        {
            return Pipelines.GetValue(composite, value => new BloomPipeline(value));
        }
        catch (Exception exception)
        {
            if (BloomCompatibility.IsPermanentInterfaceFailure(exception))
            {
                DisablePermanently(exception);
                return null;
            }
            IsolateComposite(composite, exception);
            return null;
        }
    }

    internal static string Describe()
        => "compatible=" + interfaceCompatible
            + "; offers=" + Handoff.Offers
            + "; takes=" + Handoff.Takes
            + "; misses=" + Handoff.Misses
            + "; pendingFrame=" + (Handoff.PendingFrame == int.MinValue ? "none" : Handoff.PendingFrame.ToString())
            + "; lastMiss=" + (lastHandshakeMiss ?? "none")
            + "; lastPipelineError=" + (lastPipelineError ?? "none");

    private static void NoteHandshake(string miss)
    {
        if (miss == null || miss == lastHandshakeMiss) return;
        lastHandshakeMiss = miss;
        Log.WarningOnce("[RimUnity] Bloom 地图/后处理帧交接失败：" + miss
            + "。本帧沿用宿主合成。", 0x48414E44);
    }

    //在宿主调用点接线，避免接口转发器和属性 getter 被内联后跳过补丁。
    private static IEnumerable<CodeInstruction> PatchCapture(IEnumerable<CodeInstruction> instructions)
        => ReplaceFeatureCall(instructions, AccessTools.Method(typeof(IRenderFeature), nameof(IRenderFeature.CaptureFrame)),
            nameof(CaptureFeature));

    private static IEnumerable<CodeInstruction> PatchPasses(IEnumerable<CodeInstruction> instructions)
        => ReplaceFeatureCall(instructions, AccessTools.PropertyGetter(typeof(IRenderFeature), nameof(IRenderFeature.Passes)),
            nameof(GetFeaturePasses));

    private static IEnumerable<CodeInstruction> ReplaceFeatureCall(IEnumerable<CodeInstruction> instructions,
        MethodInfo original, string replacementName)
    {
        var replacement = AccessTools.Method(typeof(BloomHooks), replacementName);
        bool found = false;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                found = true;
            }
            yield return instruction;
        }
        if (!found) throw new MissingMethodException("FrameRenderer no longer calls " + original);
    }

    private static object CaptureFeature(IRenderFeature feature, in FrameTiming timing)
    {
        object payload = feature.CaptureFrame(in timing);
        if (!interfaceCompatible) return payload;
        if (feature is MapRenderFeature && payload is MeshDrawFrame map)
            Handoff.Offer(map.FrameNumber, map);

        if (feature is not PostFxFeature || payload == null) return payload;
        if (!Handoff.TryTake(timing.FrameNumber, out var captured, out var miss))
        {
            //世界地图和创建星球没有同帧地图快照，保留宿主载荷与原生合成。
            if (Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null
                && WorldRendererUtility.DrawingMap)
                NoteHandshake(miss);
            return payload;
        }
        lastHandshakeMiss = null;
        var settings = RimUnityMod.Bloom.Snapshot() with { FrameNumber = timing.FrameNumber };
        if (settings.Enabled && settings.Intensity > 0)
            settings = settings with { Draws = new HdrDrawFrame(captured, settings.LayerMask, readOnlyDepth: true) };
        try { return settings with { Effects = ScreenEfx.EfxPlayer.Capture() }; }
        catch { settings.Dispose(); throw; }
    }

    private static IReadOnlyList<IRenderPass> GetFeaturePasses(IRenderFeature feature)
    {
        var passes = feature.Passes;
        if (feature is PostFxFeature && passes.Count == 1 && passes[0] is CompositePass composite)
            return Get(composite)?.Passes ?? passes;
        return passes;
    }

    [HarmonyPatch(typeof(PostFxFeature), nameof(PostFxFeature.OnRenderTargetRebuilt))]
    private static class Rebuild
    {
        private static void Prefix(CompositePass ___composite)
        {
            FailedComposites.Remove(___composite);
            if (Pipelines.TryGetValue(___composite, out var pipeline)) pipeline.ReleaseTargets();
        }
    }

    [HarmonyPatch(typeof(PostFxFeature), nameof(PostFxFeature.Dispose))]
    private static class Dispose
    {
        private static void Prefix(CompositePass ___composite)
        {
            FailedComposites.Remove(___composite);
            if (!Pipelines.TryGetValue(___composite, out var pipeline)) return;
            pipeline.Dispose(); Pipelines.Remove(___composite);
        }
    }
}
