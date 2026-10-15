using RimUnity.Rendering;

void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
using (var work = new LoadingWarmupWork())
{
    object first = work.Payload, queued = work.Payload;
    //The real host applies exactly this disposal rule when retiring or discarding a frame.
    if (first is IDisposable owner) owner.Dispose();
    work.Run(() => { }); work.Wait();
    if (queued is IDisposable anotherOwner) anotherOwner.Dispose();
    work.Run(() => throw new Exception("Already executed"));
    Check(work.Finished && work.Error == null && first is not IDisposable,
        "retiring borrowed frame payloads cannot dispose the shared loading request");
}
{
    var work = new LoadingWarmupWork(); object queued = work.Payload;
    work.Run(() => { }); work.Wait(); work.Dispose();
    ((LoadingWarmupWork.FramePayload)queued).Work.Run(() => throw new Exception("Retired request executed again"));
    Check(true, "a queued frame after owner cleanup safely ignores the completed request");
}
using (var work = new LoadingWarmupWork())
{
    int calls = 0;
    Parallel.For(0, 64, _ => work.Run(() => Interlocked.Increment(ref calls)));
    work.Wait(); Check(calls == 1 && work.Finished && work.Error == null, "queued render frames execute one warmup only");
}
using (var work = new LoadingWarmupWork())
{
    Check(work.CancelPending(), "request can be cancelled before rendering");
    work.Run(() => throw new Exception("Must not execute")); work.Wait();
    Check(work.Cancelled && work.Error == null, "cancelled request never reaches GPU work");
}
using (var work = new LoadingWarmupWork())
{
    work.Wait(TimeSpan.Zero);
    work.Run(() => throw new Exception("No render frame should execute after the loading timeout"));
    Check(work.Cancelled && work.Error == null, "missing render frames release loading at the timeout and reject late work");
}
using (var work = new LoadingWarmupWork())
{
    var expected = new InvalidOperationException("driver failure");
    var task = Task.Run(() => work.Run(() => throw expected)); work.Wait(); await task;
    Check(work.Finished && ReferenceEquals(work.Error, expected), "render failure is published before loading resumes");
}
using (var work = new LoadingWarmupWork())
using (var entered = new ManualResetEventSlim())
using (var release = new ManualResetEventSlim())
{
    var task = Task.Run(() => work.Run(() => { entered.Set(); release.Wait(); }));
    entered.Wait(); Check(!work.CancelPending() && !work.Finished, "running GPU work cannot be cancelled out from under the render thread");
    release.Set(); work.Wait(); await task;
    Check(work.Finished && !work.Cancelled, "loading releases request only after rendering finishes");
}
