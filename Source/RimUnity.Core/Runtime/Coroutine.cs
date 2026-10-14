using System.Collections;
using System.Collections.Generic;

namespace RimUnity;

//协程句柄保存等待状态，所属对象停用或销毁时自动取消。
public sealed class Coroutine : YieldInstruction
{
    internal MonoBehaviour Owner;
    internal IEnumerator Root;
    internal readonly Stack<IEnumerator> Stack = new();
    internal bool Done, Executing;
    internal int ResumeFrame;
    internal float Deadline;
    internal Runtime.CoroutinePhase Phase;
    internal Coroutine Awaited;
    internal CustomYieldInstruction Custom;
}
