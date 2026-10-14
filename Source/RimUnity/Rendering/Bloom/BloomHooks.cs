using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;

namespace RimUnity.Rendering;

//扩展宿主已存在的场景后处理阶段，不替换地图渲染，也不改变其他模组的材质。
internal static class BloomHooks
{
    private static readonly ConditionalWeakTable<CompositePass, BloomPipeline> Pipelines = new();
    private static MeshDrawFrame capturedMap;
    private static readonly Type Feature = AccessTools.TypeByName("RimWorldWaylith.Rendering.PostFxFeature")
        ?? throw new TypeLoadException("宿主缺少场景后处理入口 PostFxFeature。");

    //每个后处理实例拥有自己的 GPU 缓冲区，随宿主实例销毁。
    internal static BloomPipeline Get(CompositePass composite) => Pipelines.GetValue(composite, value => new BloomPipeline(value));
    internal static MethodBase Method(string name) => AccessTools.Method(Feature, name)
        ?? throw new MissingMethodException(Feature.FullName, name);

    [HarmonyPatch]
    private static class MapCapture
    {
        private static MethodBase TargetMethod() => AccessTools.Method(
            AccessTools.TypeByName("RimWorldWaylith.Rendering.MapRenderFeature"), "CaptureFrame");
        //两个 Feature 按地图、后处理顺序在主线程捕获，后续按帧号拒绝旧引用。
        private static void Postfix(object __result) => capturedMap = __result as MeshDrawFrame;
    }

    [HarmonyPatch]
    private static class Capture
    {
        //挂接渲染器实际调用的接口入口，避免内部短方法已被内联而绕过补丁。
        private static MethodBase TargetMethod() => Method("Waylith.Rendering.IRenderFeature.CaptureFrame");
        private static void Postfix(in FrameTiming timing, ref object __result)
        {
            var map = capturedMap; capturedMap = null;
            //世界地图和创建星球没有同帧地图快照，保留宿主载荷与原生合成。
            if (__result == null || map?.FrameNumber != timing.FrameNumber) return;
            var settings = RimUnityMod.Bloom.Snapshot() with { FrameNumber = timing.FrameNumber };
            if (settings.Enabled && settings.Intensity > 0)
                settings = settings with { Draws = new HdrDrawFrame(map, settings.LayerMask, readOnlyDepth: true) };
            __result = settings;
        }
    }

    [HarmonyPatch]
    private static class Passes
    {
        private static MethodBase TargetMethod() => Method("get_Passes");
        //宿主先执行所有 PreBackbuffer Pass，再按顺序执行合成与界面绘制。
        private static void Postfix(CompositePass ___composite, ref IReadOnlyList<IRenderPass> __result)
            => __result = Get(___composite).Passes;
    }

    [HarmonyPatch]
    private static class Rebuild
    {
        private static MethodBase TargetMethod() => Method("OnRenderTargetRebuilt");
        private static void Prefix(CompositePass ___composite)
        {
            if (Pipelines.TryGetValue(___composite, out var pipeline)) pipeline.ReleaseTargets();
        }
    }

    [HarmonyPatch]
    private static class Dispose
    {
        private static MethodBase TargetMethod() => Method("Dispose");
        private static void Prefix(CompositePass ___composite)
        {
            if (!Pipelines.TryGetValue(___composite, out var pipeline)) return;
            pipeline.Dispose(); Pipelines.Remove(___composite);
        }
    }
}
