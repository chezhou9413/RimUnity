using System;
using System.Collections;

namespace RimUnity;

//协程调度器使用的等待指令。
public abstract class YieldInstruction { }
public sealed class WaitForSeconds : YieldInstruction
{
    internal float Seconds { get; }
    public WaitForSeconds(float seconds) { if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds)); Seconds = seconds; }
}
public sealed class WaitForEndOfFrame : YieldInstruction { }
public sealed class WaitForFixedUpdate : YieldInstruction { }

//自定义等待在每个帧更新阶段检查一次。
public abstract class CustomYieldInstruction : IEnumerator
{
    public abstract bool keepWaiting { get; }
    public object Current => null;
    public bool MoveNext() => keepWaiting;
    public virtual void Reset() { }
}
public sealed class WaitUntil : CustomYieldInstruction
{
    private readonly Func<bool> predicate;
    public WaitUntil(Func<bool> predicate) => this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    public override bool keepWaiting => !predicate();
}
public sealed class WaitWhile : CustomYieldInstruction
{
    private readonly Func<bool> predicate;
    public WaitWhile(Func<bool> predicate) => this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    public override bool keepWaiting => predicate();
}
public sealed class WaitForSecondsRealtime : CustomYieldInstruction
{
    private float deadline = -1;
    public float waitTime { get; set; }
    public WaitForSecondsRealtime(float time) => waitTime = time;
    public override bool keepWaiting
    {
        get
        {
            if (deadline < 0) deadline = Time.unscaledTime + waitTime;
            if (Time.unscaledTime < deadline) return true;
            Reset(); return false;
        }
    }
    public override void Reset() => deadline = -1;
}
