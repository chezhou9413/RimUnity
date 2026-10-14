using System;
using System.Collections;
using System.Collections.Generic;

namespace RimUnity.Runtime;

//单线程推进协程栈，回调中启动和取消协程不会修改当前遍历快照。
internal static class CoroutineScheduler
{
    private static readonly List<Coroutine> Routines = new();
    private static readonly List<Coroutine> Scratch = new();
    private static bool stepping;
    internal static Coroutine Start(MonoBehaviour owner, IEnumerator routine)
    {
        NativeRuntime.AssertThread(); ArgumentNullException.ThrowIfNull(routine);
        var handle = new Coroutine { Owner = owner, Root = routine };
        handle.Stack.Push(routine); Routines.Add(handle); Advance(handle);
        return handle;
    }
    internal static void Stop(MonoBehaviour owner, Coroutine handle)
    {
        NativeRuntime.AssertThread();
        if (handle == null) return;
        if (handle.Owner != owner) throw new InvalidOperationException("不能停止属于其他脚本的协程。");
        Finish(handle);
    }
    internal static void Stop(MonoBehaviour owner, IEnumerator routine)
    {
        NativeRuntime.AssertThread();
        foreach (Coroutine handle in Routines.ToArray()) if (handle.Owner == owner && ReferenceEquals(handle.Root, routine)) Finish(handle);
    }
    internal static void StopAll(MonoBehaviour owner)
    {
        NativeRuntime.AssertThread();
        foreach (Coroutine handle in Routines.ToArray()) if (ReferenceEquals(handle.Owner, owner)) Finish(handle);
    }
    internal static void Step(CoroutinePhase phase)
    {
        if (stepping) throw new InvalidOperationException("协程调度不能重入。");
        stepping = true; Scratch.Clear(); Scratch.AddRange(Routines);
        try
        {
            foreach (Coroutine handle in Scratch)
            {
                if (handle.Done) continue;
                if (!handle.Owner || !handle.Owner.gameObject.activeInHierarchy) { Finish(handle); continue; }
                if (handle.Phase != phase || handle.ResumeFrame > Time.frameCount) continue;
                try
                {
                    if (handle.Deadline > Time.time || handle.Awaited is { Done: false } || handle.Custom is { keepWaiting: true }) continue;
                    handle.Awaited = null; handle.Custom = null;
                    Advance(handle);
                }
                catch (Exception error) { Finish(handle); NativeRuntime.Report(error, handle.Owner); }
            }
        }
        finally { Scratch.Clear(); Routines.RemoveAll(h => h.Done); stepping = false; }
    }
    private static void Advance(Coroutine handle)
    {
        handle.Executing = true;
        try
        {
            while (!handle.Done && handle.Stack.Count > 0)
            {
                IEnumerator current = handle.Stack.Peek();
                if (!current.MoveNext()) { handle.Stack.Pop(); (current as IDisposable)?.Dispose(); continue; }
                if (handle.Done) break;
                object yielded = current.Current;
                handle.Phase = CoroutinePhase.Update; handle.ResumeFrame = Time.frameCount + 1; handle.Deadline = 0;
                switch (yielded)
                {
                    case null: return;
                    case WaitForSeconds seconds: handle.Deadline = Time.time + seconds.Seconds; return;
                    case WaitForEndOfFrame: handle.Phase = CoroutinePhase.EndFrame; handle.ResumeFrame = Time.frameCount; return;
                    case WaitForFixedUpdate: handle.Phase = CoroutinePhase.Fixed; handle.ResumeFrame = Time.frameCount; return;
                    case Coroutine child:
                        if (ReferenceEquals(handle, child)) throw new InvalidOperationException("协程不能等待自己。");
                        if (child.Done) continue;
                        handle.Awaited = child; return;
                    case CustomYieldInstruction custom: handle.Custom = custom; return;
                    case IEnumerator nested: handle.Stack.Push(nested); continue;
                    default: throw new NotSupportedException("不支持的协程等待类型：" + yielded.GetType().FullName);
                }
            }
            handle.Done = true;
        }
        catch (Exception error) { handle.Done = true; NativeRuntime.Report(error, handle.Owner); }
        finally { handle.Executing = false; if (handle.Done) DisposeStack(handle); }
    }
    //执行中的协程延迟到 MoveNext 返回后释放迭代器，避免重入 finally。
    private static void Finish(Coroutine handle) { if (handle.Done) return; handle.Done = true; if (!handle.Executing) DisposeStack(handle); }
    private static void DisposeStack(Coroutine handle)
    {
        while (handle.Stack.TryPop(out var routine))
            try { (routine as IDisposable)?.Dispose(); }
            catch (Exception error) { NativeRuntime.Report(error, handle.Owner); }
    }
}
internal enum CoroutinePhase { Update, Fixed, EndFrame }
