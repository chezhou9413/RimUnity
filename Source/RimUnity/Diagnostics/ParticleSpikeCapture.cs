using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using Waylith;

namespace RimUnity.Diagnostics;

// Explicit developer capture. Hooks/subscriptions exist only during a bounded session; completed reports retain text only.
internal static partial class ParticleSpikeCapture
{
    private static volatile SpikeCaptureData current;
    private static SpikeCaptureData last;
    private static EngineApplication application;
    private static long deadline, previousFrame, previousAllocated;
    private static int previous0, previous1, previous2, sceneIdentity, marker;
    private static volatile int observedFrame;
    private static string before, after, reason, startedUtc, reportPath;
    private static readonly List<string> coverage = new();
    private static readonly object FrameGate = new();
    private static SpikeCaptureData.Token mainFrameToken, renderFrameToken;

    [DebugAction("RimUnity", "卡顿诊断 1 · 开始采集（60秒）", allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.Playing)]
    private static void Start60() => Start(60);
    [DebugAction("RimUnity", "卡顿诊断 1 · 开始采集（120秒）", allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.Playing)]
    private static void Start120() => Start(120);
    [DebugAction("RimUnity", "卡顿诊断 2 · 添加触发标记", allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.Playing)]
    private static void Mark()
    {
        if (!Prefs.DevMode || current == null) return;
        current.Mark("用户触发标记 " + ++marker, observedFrame);
        Messages.Message("已记录标记 " + marker + "，现在触发特效。", MessageTypeDefOf.NeutralEvent, false);
    }
    [DebugAction("RimUnity", "卡顿诊断 3 · 停止并导出", allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.Playing)]
    private static void StopMenu()
    {
        if (!Prefs.DevMode) return;
        if (current == null) { Notify("当前没有采集；可用“再次导出上次报告”。"); return; }
        Stop("用户停止");
    }
    [DebugAction("RimUnity", "卡顿诊断 · 再次导出上次报告", allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.Playing)]
    private static void ExportMenu()
    {
        if (!Prefs.DevMode) return;
        if (current != null) { Notify("请先停止采集，避免导出工作影响测量。"); return; }
        if (last == null) { Notify("还没有采集报告。"); return; }
        Export();
    }
    [DebugAction("RimUnity", "卡顿诊断 · 状态", allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.Playing)]
    private static void Status()
    {
        if (!Prefs.DevMode) return;
        Notify(current == null ? "未采集。上次报告：" + (reportPath ?? "无")
            : "正在采集，剩余约 " + Math.Max(0, (deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency) + " 秒；连续触发同一特效两次后停止导出。");
    }
    private static void Notify(string text) => Messages.Message(text, MessageTypeDefOf.NeutralEvent, false);
    private static int SceneIdentity() => Current.Game == null ? 0 : RuntimeHelpers.GetHashCode(Current.Game);
    private static void Start(int seconds)
    {
        if (!Prefs.DevMode || !UnityData.IsInMainThread) return;
        if (current != null) { Notify("已有采集正在运行，请先停止。"); return; }
        application = EngineApplication.Instance;
        if (application?.FrameRenderer == null) { application = null; Notify("宿主渲染循环尚未创建，无法采集。"); return; }
        try
        {
            coverage.Clear(); InstallProbes();
            // Capture setup, metadata enumeration and patch/JIT installation happen before the measurement origin.
            before = Context(); after = ""; reason = ""; reportPath = null; marker = 0;
            sceneIdentity = SceneIdentity(); startedUtc = DateTime.UtcNow.ToString("O");
            previousAllocated = GC.GetTotalAllocatedBytes(false);
            previous0 = GC.CollectionCount(0); previous1 = GC.CollectionCount(1); previous2 = GC.CollectionCount(2);
            observedFrame = Time.frameCount;
            last = new SpikeCaptureData(); previousFrame = last.Origin;
            deadline = previousFrame + seconds * Stopwatch.Frequency;
            current = last;
            application.OnUpdate += Tick;
            application.PrePollEvents += MainFrameStart;
            application.FrameEnqueued += MainFrameEnd;
            application.FrameRenderer.RenderThreadFrameStarting += RenderFrameStart;
            application.FrameRenderer.RenderThreadFrameEnded += RenderFrameEnd;
            application.BeforeGraphicsShutdown += Shutdown;
            Runtime.NativeRuntime.ExceptionRaised += ScriptException;
            current.Mark("开始；探针安装已完成", observedFrame);
            Notify("已开始卡顿采集（" + seconds + "秒）。触发同一特效两次，然后使用“停止并导出”；到时自动保存。");
        }
        catch (Exception error)
        {
            Detach(); current?.Close(); current = null; RemoveProbes();
            Log.Error("[RimUnity] 卡顿诊断启动失败：" + error);
        }
    }
    private static void Tick()
    {
        var data = current;
        if (data == null) return;
        try
        {
            long now = Stopwatch.GetTimestamp(), allocated = GC.GetTotalAllocatedBytes(false);
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            observedFrame = Time.frameCount;
            int particles = 0, checkedSystems = 0;
            foreach (var system in ParticleSystem.Active)
            {
                if (++checkedSystems > 512) break;
                particles += system.particleCount;
            }
            data.AddFrame(new SpikeCaptureData.Frame(observedFrame, SpikeCaptureData.Ms(now - data.Origin),
                SpikeCaptureData.Ms(now - previousFrame), Math.Max(0, allocated - previousAllocated),
                g0 - previous0, g1 - previous1, g2 - previous2, ParticleSystem.Active.Count, particles, LongEventHandler.AnyEventNowOrWaiting));
            previousFrame = now; previousAllocated = allocated; previous0 = g0; previous1 = g1; previous2 = g2;
            if (!Prefs.DevMode) Stop("开发者模式关闭");
            else if (SceneIdentity() != sceneIdentity) Stop("游戏会话变化");
            else if (now >= deadline) Stop("采集时间结束");
        }
        catch (Exception error)
        {
            data.ProbeError(); Stop("采集回调异常：" + error.GetType().Name);
        }
    }
    private static void ScriptException(Exception error, RimUnity.Object owner)
    {
        var data = current;
        if (data == null) return;
        try { data.Mark("脚本异常 " + owner?.name + "：" + Clip(error.ToString(), 2000), observedFrame); }
        catch { data.ProbeError(); }
    }
    private static void Shutdown(EngineApplication app) => Stop("图形设备关闭");
    private static void MainFrameStart(EngineApplication app) => BeginExternalFrame(ref mainFrameToken, "Host.MainFrame");
    private static void MainFrameEnd(EngineApplication app) => EndExternalFrame(ref mainFrameToken);
    private static void RenderFrameStart() => BeginExternalFrame(ref renderFrameToken, "Host.RenderFrame");
    private static void RenderFrameEnd(Waylith.Rendering.RenderFrame frame) => EndExternalFrame(ref renderFrameToken);
    private static void BeginExternalFrame(ref SpikeCaptureData.Token slot, string stage)
    {
        lock (FrameGate)
        {
            var data = current;
            if (data == null) return;
            if (slot != null) slot.Owner.End(slot, "missing end event before next frame");
            slot = data.Begin(stage, "detached frame event", observedFrame, detached: true);
        }
    }
    private static void EndExternalFrame(ref SpikeCaptureData.Token slot, string outcome = "ok")
    {
        lock (FrameGate)
        {
            var token = slot; slot = null;
            if (token != null) token.Owner.End(token, outcome);
        }
    }
    private static void Detach()
    {
        if (application != null)
        {
            application.OnUpdate -= Tick; application.BeforeGraphicsShutdown -= Shutdown;
            application.PrePollEvents -= MainFrameStart; application.FrameEnqueued -= MainFrameEnd;
            application.FrameRenderer.RenderThreadFrameStarting -= RenderFrameStart;
            application.FrameRenderer.RenderThreadFrameEnded -= RenderFrameEnd;
        }
        lock (FrameGate) { mainFrameToken = renderFrameToken = null; }
        Runtime.NativeRuntime.ExceptionRaised -= ScriptException;
        application = null;
    }
    private static void Stop(string why)
    {
        var data = current;
        if (data == null) return;
        EndExternalFrame(ref mainFrameToken, "capture stop: partial main frame");
        data.Mark("停止：" + why, observedFrame);
        current = null; data.Close(); reason = why;
        Detach(); RemoveProbes();
        try { after = Context(); } catch (Exception e) { after = "结束状态读取失败：" + e.Message; }
        Export();
    }
    private static string Clip(string text, int limit = 500)
    {
        if (text == null) return "";
        return text.Length <= limit ? text : text.Substring(0, limit) + " [truncated]";
    }
    private static string Context()
    {
        var b = new StringBuilder();
        b.AppendLine("UTC=" + DateTime.UtcNow.ToString("O") + "; state=" + Current.ProgramState + "; map=" + Find.CurrentMap?.Index
            + "; longEvent=" + LongEventHandler.AnyEventNowOrWaiting + "; bridgeFrame=" + Time.frameCount);
        b.AppendLine("OS=" + System.Runtime.InteropServices.RuntimeInformation.OSDescription + "; runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            + "; CPUs=" + Environment.ProcessorCount + "; mainThread=" + Environment.CurrentManagedThreadId);
        using (var process = Process.GetCurrentProcess()) b.AppendLine("workingSet=" + process.WorkingSet64 + "; privateBytes=" + process.PrivateMemorySize64);
        b.AppendLine("managedBytes=" + GC.GetTotalMemory(false) + "; totalAllocated=" + GC.GetTotalAllocatedBytes(false)
            + "; GC=" + GC.CollectionCount(0) + "/" + GC.CollectionCount(1) + "/" + GC.CollectionCount(2));
        b.AppendLine("Composite=" + RendererDebugCapture.CompositeState);
        b.AppendLine("Other diagnostics: renderReport=" + RenderDebugReport.Collecting + "; particleWindow=" + ParticleDebugWindow.Capturing
            + "; enabled diagnostics and open debug menus contribute overhead.");
        foreach (var assembly in new[] { typeof(ParticleSpikeCapture).Assembly, typeof(RimUnity.Object).Assembly, typeof(EngineApplication).Assembly, typeof(Verse.Game).Assembly })
            b.AppendLine("Assembly=" + assembly.FullName + "; MVID=" + assembly.ManifestModule.ModuleVersionId);
        foreach (var mod in LoadedModManager.RunningModsListForReading) b.AppendLine("MOD=" + mod.PackageIdPlayerFacing + " | " + mod.Name);
        // Inspect existing caches only. Never GetCatalog/Load/GetPaths: those would warm the target before its first trigger.
        if (CatalogsField.GetValue(null) is IDictionary catalogs)
            foreach (DictionaryEntry entry in catalogs)
            {
                var catalog = (Assets.AssetCatalog)entry.Value;
                var resources = (IDictionary)ResourcesField.GetValue(catalog);
                b.AppendLine("CATALOG=" + catalog.Manifest.owner + "; root=" + catalog.Root + "; declared=" + catalog.Manifest.assets.Count + "; cached=" + resources.Count);
                int listed = 0;
                foreach (DictionaryEntry resource in resources)
                {
                    if (++listed > 512) { b.AppendLine("[cached resource listing truncated at 512]"); break; }
                    var value = (RimUnity.Object)resource.Value;
                    string detail = value is Texture2D texture ? "; size=" + texture.width + "x" + texture.height + "; GPU=" + (texture.CoreTexture.GpuResource != null)
                        : value is Material material ? "; shader=" + material.shader.name + "; passes=" + material.passCount : "";
                    b.AppendLine(" CACHED=" + resource.Key + "; type=" + value.GetType().Name + "; name=" + Clip(value.name) + detail);
                }
            }
        b.AppendLine("ACTIVE_SYSTEMS=" + ParticleSystem.Active.Count + " (configuration listing capped at 256)");
        foreach (var system in ParticleSystem.Active.Take(256))
            b.AppendLine(" SYSTEM=" + ObjectPath(system) + "; id=" + system.GetInstanceID() + "; count=" + system.particleCount + "; max=" + system.main.maxParticles
                + "; prewarm=" + system.main.prewarm + "; loop=" + system.main.loop + "; duration=" + system.main.duration
                + "; speed=" + system.main.simulationSpeed + "; collision=" + system.collision.enabled + "; trails=" + system.trails.enabled + "; subEmitters=" + system.subEmitters.enabled);
        return b.ToString();
    }
}
