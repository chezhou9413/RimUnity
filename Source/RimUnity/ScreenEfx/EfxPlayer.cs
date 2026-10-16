using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Verse;

namespace RimUnity.ScreenEfx;

/// <summary>Owns submitted clips for the current game. Playback is not saved.</summary>
public static class EfxPlayer
{
    //Main-thread session ownership; GPU retirement only enqueues releases/errors.
    private static readonly List<EfxLease> playing = new();
    private static readonly List<EfxLease> ordered = new();
    private static readonly ConcurrentQueue<EfxLease> releases = new();
    private static readonly ConcurrentQueue<(EfxLease lease, Exception error)> errors = new();
    private static long sequence;
    private static int activeCount;
    //Render-thread cache policy reads only this published count, never the mutable clip registry.
    internal static bool HasPlaying => Volatile.Read(ref activeCount) != 0;
    internal static void Stopped() => Interlocked.Decrement(ref activeCount);

    internal static void AssertThread()
    {
        if (!UnityData.IsInMainThread) throw new InvalidOperationException("Screen effects must be controlled on the game thread.");
    }

    public static EfxHandle Play(EfxClip clip)
    {
        AssertThread(); ArgumentNullException.ThrowIfNull(clip);
        if (Current.Game == null) throw new InvalidOperationException("Screen effects require a game session.");
        if (clip.Submitted) throw new InvalidOperationException("An EfxClip instance can only be submitted once.");
        clip.Submitted = true;
        var lease = new EfxLease(clip, Current.Game, sequence++);
        playing.Add(lease);
        Interlocked.Increment(ref activeCount);
        return new EfxHandle { Lease = lease };
    }

    internal static void Update(EfxUpdateContext context)
    {
        AssertThread(); Drain();
        if (playing.Count == 0) return;
        //Callbacks may submit or stop clips; newly submitted clips start next update.
        int count = playing.Count;
        for (int i = 0; i < count; i++)
        {
            var lease = playing[i];
            if (!ReferenceEquals(lease.Owner, Current.Game)) lease.Stop();
            if (lease.Stopped || !lease.Clip.UpdateEnabled) continue;
            try { lease.Clip.Update(context); }
            catch (Exception error) { Fail(lease, error); }
        }
        playing.RemoveAll(item => item.Stopped);
        if (playing.Count == 0) EfxFrame.ClearPool();
        Drain();
    }

    internal static void SynchronizeSession()
    {
        AssertThread();
        foreach (var lease in playing) if (!ReferenceEquals(lease.Owner, Current.Game)) lease.Stop();
        playing.RemoveAll(item => item.Stopped);
        EfxFrame.ClearPool();
        Drain();
    }

    internal static EfxFrame Capture()
    {
        AssertThread();
        if (playing.Count == 0 || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return null;
        ordered.Clear();
        foreach (var lease in playing)
            if (!lease.Stopped && ReferenceEquals(lease.Owner, Current.Game) && lease.Clip.Enabled) ordered.Add(lease);
        ordered.Sort((a, b) => { int order = a.Clip.Order.CompareTo(b.Clip.Order); return order != 0 ? order : a.Sequence.CompareTo(b.Sequence); });
        if (ordered.Count == 0) return null;
        var frame = EfxFrame.Rent();
        try
        {
            foreach (var lease in ordered)
            {
                if (lease.Stopped) continue;
                using var builder = frame.Builder;
                builder.Begin(lease);
                try { lease.Clip.BuildFrame(builder); if (!lease.Stopped) builder.Commit(frame); }
                catch (Exception error) { Fail(lease, error); }
            }
            if (frame.Passes.Count != 0) return frame;
            frame.Dispose(); return null;
        }
        catch { frame.Dispose(); throw; }
        finally { ordered.Clear(); }
    }

    internal static EfxFrame CaptureWarmup()
    {
        AssertThread();
        if (!HasPlaying) return null;
        var frame = EfxFrame.Rent();
        try
        {
            //Freeze each opt-in material once per submission, after saved game state is initialized.
            int count = playing.Count;
            for (int i = 0; i < count; i++)
            {
                var lease = playing[i];
                if (lease.Stopped || lease.Warmed || !ReferenceEquals(lease.Owner, Current.Game)) continue;
                lease.Warmed = true;
                using var builder = frame.Builder;
                builder.Begin(lease);
                try { lease.Clip.Warmup(builder); if (!lease.Stopped) builder.Commit(frame); }
                catch (Exception error) { Fail(lease, error); }
            }
            if (frame.Passes.Count != 0) return frame;
            frame.Dispose(); return null;
        }
        catch { frame.Dispose(); throw; }
    }

    internal static void Clear()
    {
        AssertThread();
        foreach (var lease in playing) lease.Stop();
        playing.Clear(); ordered.Clear(); EfxFrame.ClearPool(); Drain();
    }

    internal static void Retire(EfxLease lease) => releases.Enqueue(lease);
    internal static void Report(EfxLease lease, Exception error)
    {
        if (Interlocked.Exchange(ref lease.FailureReported, 1) == 0) errors.Enqueue((lease, error));
    }
    private static void Fail(EfxLease lease, Exception error)
    {
        if (lease.Stopped) return;
        Log.Error($"[RimUnity] Screen effect {lease.Clip.GetType().FullName} stopped: {error}");
        lease.Stop();
    }
    internal static void Drain()
    {
        while (errors.TryDequeue(out var failure)) Fail(failure.lease, failure.error);
        while (releases.TryDequeue(out var lease))
        {
            try { lease.Release(); }
            catch (Exception error) { Log.Error($"[RimUnity] Screen effect release failed: {error}"); }
        }
    }
}

internal sealed class EfxLease(EfxClip clip, Game owner, long sequence)
{
    internal EfxClip Clip { get; private set; } = clip;
    internal Game Owner { get; private set; } = owner;
    internal readonly long Sequence = sequence;
    internal bool Stopped { get; private set; }
    internal int FailureReported;
    internal bool Warmed;
    internal bool RenderFailed => Volatile.Read(ref FailureReported) != 0;
    private int references = 1;
    internal void Retain() => Interlocked.Increment(ref references);
    internal void Drop() { if (Interlocked.Decrement(ref references) == 0) EfxPlayer.Retire(this); }
    internal void Stop() { if (Stopped) return; Stopped = true; EfxPlayer.Stopped(); Drop(); }
    internal void Release()
    {
        var clip = Clip; Clip = null; Owner = null;
        clip?.OnRelease();
    }
}
