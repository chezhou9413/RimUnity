using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Waylith;
using Waylith.Rendering;

namespace RimUnity.PerformanceLoop;

// The render thread accumulates primitive timings locally and hands completed records to the main thread.
internal static class RenderProbes
{
    private static readonly Harmony Patcher = new("Chezhou.RimUnity.PerformanceLoop.Render");
    private static readonly ConcurrentQueue<Row> Rows = new();
    private static volatile bool recording;
    private static int recorded;
    [ThreadStatic] private static Row current;

    private record struct Row(int Frame, long Start, long End, long Acquire, long Submit, long Pace,
        long BodyEnd, long CompletionStart, long CompletionEnd);

    internal static void Install()
    {
        var engine = typeof(EngineApplication).Assembly;
        Type Type(string name) => engine.GetType(name, throwOnError: true)!;
        var thread = Type("Waylith.Rendering.RenderThread");
        Patch(AccessTools.DeclaredMethod(thread, "ConsumeFrame", [typeof(RenderFrame)]),
            nameof(BeginFrame), nameof(EndFrame));
        Patch(AccessTools.DeclaredMethod(thread, "RenderFrame", [typeof(RenderFrame)]),
            nameof(BeginStage), nameof(EndBody));
        Patch(AccessTools.DeclaredMethod(thread, "MarkFrameComplete", []),
            nameof(BeginStage), nameof(EndCompletion));
        Patch(AccessTools.DeclaredMethod(Type("Waylith.Graphics.RenderContext"), "TryAcquireNextFrame", []),
            nameof(BeginStage), nameof(EndAcquire));
        Patch(AccessTools.DeclaredMethod(Type("Waylith.Graphics.WindowRenderContext"), "TrySubmitFrame"),
            nameof(BeginStage), nameof(EndSubmit));
        Patch(AccessTools.DeclaredMethod(Type("Waylith.PlatformSpecific.FrameSleep"), "SleepUntil", [typeof(long)]),
            nameof(BeginStage), nameof(EndPace));
    }

    private static void Patch(MethodInfo? method, string prefix, string finalizer)
    {
        if (method == null) throw new MissingMethodException("Waylith render probe: " + finalizer);
        Patcher.Patch(method, prefix: new HarmonyMethod(typeof(RenderProbes), prefix),
            finalizer: new HarmonyMethod(typeof(RenderProbes), finalizer));
    }

    internal static void Start() => recording = true;

    internal static void Stop()
    {
        recording = false;
        // Sampling and export have finished before draining in-flight render work.
        EngineApplication.Instance?.WaitForRenderThreadIdle();
        Patcher.UnpatchAll(Patcher.Id);
    }

    private static void BeginFrame(RenderFrame __0)
    {
        if (recording) current = new Row { Frame = __0.Timing.FrameNumber, Start = Stopwatch.GetTimestamp() };
    }

    private static void EndFrame()
    {
        if (current.Start == 0) return;
        current.End = Stopwatch.GetTimestamp();
        if (Interlocked.Increment(ref recorded) <= 36000) Rows.Enqueue(current);
        current = default;
    }

    private static void BeginStage(out long __state) => __state = current.Start == 0 ? 0 : Stopwatch.GetTimestamp();
    private static void EndAcquire(long __state) { if (__state != 0) current.Acquire += Stopwatch.GetTimestamp() - __state; }
    private static void EndSubmit(long __state) { if (__state != 0) current.Submit += Stopwatch.GetTimestamp() - __state; }
    private static void EndPace(long __state) { if (__state != 0) current.Pace += Stopwatch.GetTimestamp() - __state; }
    private static void EndBody(long __state) { if (__state != 0) current.BodyEnd = Stopwatch.GetTimestamp(); }
    private static void EndCompletion(long __state)
    {
        if (__state == 0) return;
        current.CompletionStart = __state;
        current.CompletionEnd = Stopwatch.GetTimestamp();
    }

    internal static void Export(string path)
    {
        using var writer = File.CreateText(path);
        writer.WriteLine("host_frame,start_ticks,end_ticks,body_end_ticks,queue_complete_start_ticks,queue_complete_end_ticks,total_ms,acquire_ms,submit_present_ms,pace_ms,other_ms");
        double ms = 1000.0 / Stopwatch.Frequency;
        while (Rows.TryDequeue(out var row))
        {
            long total = row.End - row.Start;
            long other = Math.Max(0, total - row.Acquire - row.Submit - row.Pace);
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{row.Frame},{row.Start},{row.End},{row.BodyEnd},{row.CompletionStart},{row.CompletionEnd},{total * ms:F6},{row.Acquire * ms:F6},{row.Submit * ms:F6},{row.Pace * ms:F6},{other * ms:F6}"));
        }
    }
}
