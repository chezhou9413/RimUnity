using System.Diagnostics;
using System.Globalization;
using HarmonyLib;
using Waylith;

namespace RimUnity.PerformanceLoop;

// Installed only in the benchmark process. Children are recorded only inside Throttle.
internal static class WaitProbes
{
    private static readonly Harmony Patcher = new("Chezhou.RimUnity.PerformanceLoop.Waits");
    private static readonly Waits[] Rows = new Waits[36001];
    private static Waits pending;
    private static int mainThread;
    private static bool inThrottle;
    internal record struct Waits(bool Seen, long Throttle, long Backlog, long Sleep, long BacklogStart, long BacklogEnd);

    internal static void Install()
    {
        mainThread = Environment.CurrentManagedThreadId;
        Patch(typeof(EngineApplication), "ThrottleToTargetFrameRate", typeof(bool), nameof(BeginThrottle), nameof(EndThrottle));
        var engine = typeof(EngineApplication).Assembly;
        Patch(engine.GetType("Waylith.Rendering.RenderThread", true)!, "WaitForBacklogAtMost", typeof(int), nameof(BeginChild), nameof(EndBacklog));
        Patch(engine.GetType("Waylith.PlatformSpecific.FrameSleep", true)!, "SleepUntil", typeof(long), nameof(BeginChild), nameof(EndSleep));
    }

    private static void Patch(Type type, string method, Type parameter, string prefix, string finalizer)
    {
        var target = AccessTools.DeclaredMethod(type, method, [parameter])
            ?? throw new MissingMethodException(type.FullName, method);
        Patcher.Patch(target, prefix: new HarmonyMethod(typeof(WaitProbes), prefix),
            finalizer: new HarmonyMethod(typeof(WaitProbes), finalizer));
    }

    private static void BeginThrottle(out long __state)
    {
        __state = Environment.CurrentManagedThreadId == mainThread ? Stopwatch.GetTimestamp() : 0;
        if (__state != 0) inThrottle = true;
    }

    private static void EndThrottle(long __state)
    {
        if (__state == 0) return;
        pending.Throttle += Stopwatch.GetTimestamp() - __state;
        pending.Seen = true;
        inThrottle = false;
    }

    private static void BeginChild(out long __state) => __state =
        Environment.CurrentManagedThreadId == mainThread && inThrottle ? Stopwatch.GetTimestamp() : 0;
    private static void EndBacklog(long __state)
    {
        if (__state == 0) return;
        long end = Stopwatch.GetTimestamp();
        pending.Backlog += end - __state;
        if (pending.BacklogStart == 0) pending.BacklogStart = __state;
        pending.BacklogEnd = end;
    }
    private static void EndSleep(long __state) { if (__state != 0) pending.Sleep += Stopwatch.GetTimestamp() - __state; }

    internal static Waits Take()
    {
        var result = pending;
        pending = default;
        return result;
    }

    internal static void Store(int frame, Waits waits)
    {
        if ((uint)frame < Rows.Length) Rows[frame] = waits;
    }

    internal static void Remove() => Patcher.UnpatchAll(Patcher.Id);

    internal static void Export(string path, int count)
    {
        using var writer = File.CreateText(path);
        writer.WriteLine("frame,throttle_ms,render_backlog_ms,frame_sleep_ms,backlog_start_ticks,backlog_end_ticks");
        double ms = 1000.0 / Stopwatch.Frequency;
        for (int frame = 2; frame <= count && frame < Rows.Length; frame++)
        {
            var row = Rows[frame];
            if (row.Seen)
                writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{frame},{row.Throttle * ms:F6},{row.Backlog * ms:F6},{row.Sleep * ms:F6},{row.BacklogStart},{row.BacklogEnd}"));
        }
    }
}
