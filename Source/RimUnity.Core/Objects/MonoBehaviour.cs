using System;
using System.Collections;

namespace RimUnity;

//消息和协程全部由 RimUnity 的主线程调度器推进。
public class MonoBehaviour : Behaviour
{
    internal bool Awakened, EnableSent, Started;
    internal Runtime.MessageMethods Messages;
    public bool useGUILayout { get; set; } = true;
    public Coroutine StartCoroutine(IEnumerator routine)
    {
        if (!this || Owner == null || !gameObject.activeInHierarchy) throw new InvalidOperationException("只能在活动对象的脚本上启动协程。");
        return Runtime.CoroutineScheduler.Start(this, routine);
    }
    public void StopCoroutine(Coroutine coroutine) => Runtime.CoroutineScheduler.Stop(this, coroutine);
    public void StopCoroutine(IEnumerator routine) => Runtime.CoroutineScheduler.Stop(this, routine);
    public void StopAllCoroutines() => Runtime.CoroutineScheduler.StopAll(this);
}
