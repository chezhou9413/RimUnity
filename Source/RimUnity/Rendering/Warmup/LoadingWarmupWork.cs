using System;
using System.Threading;

namespace RimUnity.Rendering;

//One immutable render request, consumed once. No map/game state crosses this boundary.
internal sealed class LoadingWarmupWork : IDisposable
{
    //RenderFrame owns/disposes IDisposable payloads. This borrowed envelope must not own the request.
    internal sealed record FramePayload(LoadingWarmupWork Work);
    internal FramePayload Payload { get; }
    private readonly ManualResetEventSlim completed = new(false);
    private int state; //0 pending, 1 running, 2 completed, 3 cancelled before execution
    internal Exception Error { get; private set; }
    internal bool Finished => completed.IsSet;
    internal bool Cancelled => Volatile.Read(ref state) == 3;
    internal LoadingWarmupWork() => Payload = new FramePayload(this);

    internal void Run(Action action)
    {
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
        try { action(); }
        catch (Exception error) { Error = error; }
        finally { Volatile.Write(ref state, 2); completed.Set(); }
    }
    internal bool CancelPending()
    {
        if (Interlocked.CompareExchange(ref state, 3, 0) != 0) return false;
        completed.Set(); return true;
    }
    internal void Wait(TimeSpan? timeout = null)
    {
        //A minimized/headless window may never submit a render frame. Do not hang loading.
        if (!completed.Wait(timeout ?? TimeSpan.FromSeconds(30)) && !CancelPending()) completed.Wait();
    }
    public void Dispose() => completed.Dispose();
}
