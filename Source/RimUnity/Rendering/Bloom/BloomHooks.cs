using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Brutal.VulkanApi;
using HarmonyLib;
using RimWorld.Planet;
using RimWorldWaylith.Rendering;
using Verse;
using Waylith.Graphics;
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

    //构造和图像绑定的签名必须与编译期一致，启动时检查，避免清理资源时才在渲染线程抛错。
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
            if (pass.GetMethod(nameof(PostEffectPass.SetInput), new[] {
                typeof(int), typeof(VkImageView), typeof(VkSampler), typeof(VkImageLayout) }) == null)
                throw new MissingMethodException(pass.FullName, "SetInput(Int32, VkImageView, VkSampler, VkImageLayout)");
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

    //在宿主捕获完成后读取同一阶段的载荷，保留原有接口调用和逐阶段计时。
    private static IEnumerable<CodeInstruction> PatchCapture(IEnumerable<CodeInstruction> instructions)
    {
        var capture = AccessTools.Method(typeof(IRenderFeature), nameof(IRenderFeature.CaptureFrame));
        var matcher = new CodeMatcher(instructions).MatchStartForward(
            new CodeMatch(code => code.IsLdloc()), new CodeMatch(code => code.IsLdarg(1)),
            new CodeMatch(code => code.Calls(capture)));
        if (matcher.IsInvalid) throw new MissingMethodException("FrameRenderer 缺少渲染阶段捕获调用。");
        var loadFeature = new CodeInstruction(matcher.Opcode, matcher.Operand);
        matcher.Advance(3).Insert(loadFeature, new CodeInstruction(OpCodes.Ldarg_1),
            CodeInstruction.Call(typeof(BloomHooks), nameof(AfterFeatureCapture)));
        return matcher.InstructionEnumeration();
    }

    //在宿主读取通道列表的位置接入后处理，兼容已内联的属性访问。
    private static IEnumerable<CodeInstruction> PatchPasses(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.PropertyGetter(typeof(IRenderFeature), nameof(IRenderFeature.Passes));
        var replacement = AccessTools.Method(typeof(BloomHooks), nameof(GetFeaturePasses));
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

    //地图提供宿主快照，后处理只构建本帧需要的泛光和屏幕效果。
    private static object AfterFeatureCapture(object payload, IRenderFeature feature, in FrameTiming timing)
    {
        if (!interfaceCompatible || payload == null) return payload;
        if (feature is MapRenderFeature && payload is MeshDrawFrame map)
            Handoff.Offer(map.FrameNumber, map);

        if (feature is not PostFxFeature) return payload;
        if (!Handoff.TryTake(timing.FrameNumber, out var captured, out var miss))
        {
            //世界地图和创建星球没有同帧地图快照，保留宿主载荷与原生合成。
            if (Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null
                && WorldRendererUtility.DrawingMap)
                NoteHandshake(miss);
            return payload;
        }
        lastHandshakeMiss = null;
        var bloom = RimUnityMod.Bloom;
        bool diagnostics = Diagnostics.RenderDebugReport.Collecting || Diagnostics.ParticleDebugWindow.Capturing;
        if ((bloom.EffectiveLayerMask == 0 || bloom.intensity <= 0f)
            && !ScreenEfx.EfxPlayer.HasPlaying && !diagnostics) return payload;

        var settings = bloom.Snapshot(timing.FrameNumber);
        try
        {
            if (settings.Enabled && settings.Intensity > 0f)
                settings.Draws = HdrDrawFrame.CaptureBloom(captured, settings.LayerMask);
            if (ScreenEfx.EfxPlayer.HasPlaying) settings.Effects = ScreenEfx.EfxPlayer.Capture();
            if (settings.Draws != null || settings.Effects != null || diagnostics) return settings;
            settings.Dispose();
            return payload;
        }
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
        private static void Prefix(CompositePass ___composite, IRenderTarget __0)
        {
            FailedComposites.Remove(___composite);
            if (__0 is IRenderContext context && Pipelines.TryGetValue(___composite, out var pipeline))
                pipeline.RebuildTargets(context);
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
