using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RimUnity.Diagnostics;

// Bounded, text/numeric-only session data. No game, map, component or GPU references.
internal sealed class SpikeCaptureData
{
    internal const int MaxGroups = 4096, MaxEvents = 25000, MaxFrames = 12000;
    internal sealed record Event(long Id, long Parent, int Thread, int Frame, double StartMs, double DurationMs,
        double SelfMs, long AllocatedBytes, string Stage, string Detail, string Outcome, string Notes = "");
    internal sealed record Frame(int Number, double AtMs, double IntervalMs, long AllocatedBytes,
        int Gen0, int Gen1, int Gen2, int Systems, int Particles, bool LongEvent);
    internal sealed class Group
    {
        public string Stage, Detail;
        public long Calls, Errors, AllocatedBytes;
        public double TotalMs, SelfMs, MaxMs;
        public readonly List<Event> First = new(3);
    }
    internal sealed record Snapshot(Event[] Events, Frame[] Frames, Group[] Groups, long OmittedEvents,
        long OmittedGroups, long OmittedFrames, long InFlight, long ProbeErrors, bool Closed);
    internal sealed class Token
    {
        internal SpikeCaptureData Owner;
        internal Token Parent;
        internal long Id, Start, Bytes, Children;
        internal int Thread, Frame;
        internal string Stage, Detail;
        internal string Notes = "", Outcome = "ok";
        internal bool Ended;
    }
    [ThreadStatic] private static Token top;
    private readonly object gate = new();
    private readonly Dictionary<string, Group> groups = new();
    private readonly List<Event> events = new(2048);
    private readonly List<Frame> frames = new(2048);
    private long sequence, omittedEvents, omittedGroups, omittedFrames, inFlight, probeErrors;
    private bool closed;
    internal readonly long Origin = Stopwatch.GetTimestamp();

    internal Token Begin(string stage, string detail, int frame, bool detached = false)
    {
        lock (gate)
        {
            if (closed) return null;
            var token = new Token { Owner = this, Parent = !detached && top?.Owner == this ? top : null, Id = ++sequence,
                Thread = Environment.CurrentManagedThreadId, Frame = frame, Stage = stage, Detail = detail };
            if (!detached) top = token;
            inFlight++;
            // Metadata construction is outside the measured span; nested probe overhead remains visible in parents.
            token.Bytes = GC.GetAllocatedBytesForCurrentThread(); token.Start = Stopwatch.GetTimestamp();
            return token;
        }
    }
    internal void End(Token token, string outcome = "ok")
    {
        long end = Stopwatch.GetTimestamp(), bytes = GC.GetAllocatedBytesForCurrentThread();
        if (token == null || token.Owner != this || token.Ended) return;
        token.Ended = true;
        if (top == token) top = token.Parent;
        long ticks = Math.Max(0, end - token.Start);
        if (token.Parent != null) token.Parent.Children += ticks;
        var sample = new Event(token.Id, token.Parent?.Id ?? 0, token.Thread, token.Frame, Ms(token.Start - Origin),
            Ms(ticks), Ms(Math.Max(0, ticks - token.Children)), Math.Max(0, bytes - token.Bytes), token.Stage, token.Detail, outcome, token.Notes);
        lock (gate)
        {
            inFlight--;
            if (closed) return;
            string key = token.Stage + "\n" + token.Detail;
            if (!groups.TryGetValue(key, out var group))
            {
                if (groups.Count == MaxGroups) { omittedGroups++; return; }
                group = new Group { Stage = token.Stage, Detail = token.Detail }; groups.Add(key, group);
            }
            group.Calls++; group.Errors += outcome.StartsWith("exception:", StringComparison.Ordinal) ? 1 : 0;
            group.TotalMs += sample.DurationMs; group.SelfMs += sample.SelfMs;
            group.AllocatedBytes += sample.AllocatedBytes; group.MaxMs = Math.Max(group.MaxMs, sample.DurationMs);
            bool first = group.First.Count < 3 || sample.Id < group.First[^1].Id;
            if (first)
            {
                group.First.Add(sample); group.First.Sort((a, b) => a.Id.CompareTo(b.Id));
                if (group.First.Count > 3) group.First.RemoveAt(3);
            }
            // Retain first calls, slow calls and errors; aggregate every call. No per-frame disk I/O.
            if (first || sample.DurationMs >= 2 || outcome.StartsWith("exception:", StringComparison.Ordinal))
            {
                if (events.Count < MaxEvents) events.Add(sample); else omittedEvents++;
            }
        }
    }
    internal void AddFrame(Frame frame)
    {
        lock (gate)
        {
            if (closed) return;
            if (frames.Count < MaxFrames) frames.Add(frame); else omittedFrames++;
        }
    }
    internal void Mark(string label, int frame)
    {
        lock (gate)
        {
            if (closed) return;
            if (events.Count < MaxEvents) events.Add(new Event(++sequence, 0, Environment.CurrentManagedThreadId,
                frame, Ms(Stopwatch.GetTimestamp() - Origin), 0, 0, 0, "MARK", label, "ok"));
            else omittedEvents++;
        }
    }
    internal void ProbeError() { lock (gate) probeErrors++; }
    internal void Close() { lock (gate) closed = true; }
    internal Snapshot Read()
    {
        lock (gate)
            return new Snapshot(events.OrderBy(e => e.Id).ToArray(), frames.ToArray(), groups.Values.Select(g =>
            {
                var copy = new Group { Stage = g.Stage, Detail = g.Detail, Calls = g.Calls, Errors = g.Errors,
                    TotalMs = g.TotalMs, SelfMs = g.SelfMs, MaxMs = g.MaxMs, AllocatedBytes = g.AllocatedBytes };
                copy.First.AddRange(g.First); return copy;
            }).ToArray(), omittedEvents, omittedGroups, omittedFrames, inFlight, probeErrors, closed);
    }
    internal static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
