using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace RimUnity.Diagnostics;

// Explicit capture only. Bounded text, never retained frames/materials/GPU objects or render-thread I/O.
internal sealed class RenderTraceBuffer(int maxEvents = 4096, int maxChars = 4 * 1024 * 1024, int maxDetail = 16384)
{
    private readonly object gate = new();
    private readonly Queue<string> events = new();
    private readonly Dictionary<string, string> first = new();
    private int chars, dropped, clipped;
    private long start, sequence;
    internal volatile bool Active;

    internal void Start()
    {
        lock (gate)
        {
            Clear(); start = Stopwatch.GetTimestamp(); Active = true;
        }
    }
    internal void Stop() { lock (gate) Active = false; }
    internal void Clear()
    {
        lock (gate)
        {
            Active = false; events.Clear(); first.Clear(); chars = dropped = clipped = 0; sequence = 0;
        }
    }
    internal void Record(string key, string detail)
    {
        if (!Active) return;
        lock (gate)
        {
            if (!Active) return;
            if (key.Length > 80) key = key.Substring(0, 80);
            int limit = Math.Min(maxDetail, Math.Max(0, maxChars - 256));
            if (detail.Length > limit) { detail = detail.Substring(0, limit) + " [DETAIL TRUNCATED]"; clipped++; }
            string entry = "#" + (++sequence) + " +" + ((Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency)
                + "ms thread=" + Environment.CurrentManagedThreadId + " " + key + " | " + detail;
            if (first.Count < 32 && !first.ContainsKey(key)) first.Add(key, entry);
            while (events.Count > 0 && (events.Count >= maxEvents || chars + entry.Length > maxChars))
            { chars -= events.Dequeue().Length; dropped++; }
            events.Enqueue(entry); chars += entry.Length;
        }
    }
    internal void AppendTo(StringBuilder text)
    {
        lock (gate)
        {
            text.AppendLine("HDR LIGHT TRACE v1 / interleaved-light-runs / CPU observations, not GPU pixels");
            text.AppendLine("events=" + sequence + "; retained=" + events.Count + "; droppedOldest=" + dropped + "; clippedDetails=" + clipped);
            text.AppendLine("FIRST EVENT PER STAGE (baseline copies; also present in timeline unless evicted):");
            foreach (var entry in first.Values) text.AppendLine(entry);
            text.AppendLine("TIMELINE (recording order across CPU threads; correlate by frame and slot, not elapsed time alone):");
            foreach (var entry in events) text.AppendLine(entry);
            if (sequence == 0) text.AppendLine("NO TRACE EVENTS: check loaded runtime, active map and diagnostic hooks.");
        }
    }
}
