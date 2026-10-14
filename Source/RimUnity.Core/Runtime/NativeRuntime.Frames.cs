using System;
using System.Collections;
using System.Collections.Generic;

namespace RimUnity.Runtime;

//按帧推进脚本生命周期、协程与对象回收。
public static partial class NativeRuntime
{
    private static readonly List<MonoBehaviour> FrameScripts = new();
    private static readonly Dictionary<Object, float> DestroyQueue = new();
    private static readonly List<Object> DueDestroy = new();
    private static float fixedRemainder;
    private static bool frameOpen, updating;

    //捕获本帧脚本集合，更新期间创建的脚本最早在下一帧开始 Update。
    public static void BeginFrame(float unscaledDeltaTime)
    {
        AssertThread();
        if (frameOpen) throw new InvalidOperationException("上一帧尚未结束。");
        Time.Advance(unscaledDeltaTime); frameOpen = true;
        Drain(); FrameScripts.Clear(); FrameScripts.AddRange(Behaviours);
    }
    public static void Update()
    {
        RequireFrame();
        if (updating) throw new InvalidOperationException("运行时帧更新不能重入。");
        updating = true;
        try
        {
            //本帧已有的活动脚本先完成 Start，再进入固定更新和普通更新。
            foreach (MonoBehaviour mono in FrameScripts) Ready(mono);
            float step = Time.fixedDeltaTime;
            if (!float.IsFinite(step) || step <= 0) throw new InvalidOperationException("fixedDeltaTime 必须为有限正数。");
            fixedRemainder += Time.deltaTime;
            float frameDelta = Time.deltaTime;
            try
            {
                Time.deltaTime = step;
                while (fixedRemainder >= step)
                {
                    fixedRemainder -= step;
                    foreach (MonoBehaviour mono in FrameScripts) if (Ready(mono)) Invoke(mono, mono.Messages.FixedUpdate);
                    CoroutineScheduler.Step(CoroutinePhase.Fixed);
                }
            }
            finally { Time.deltaTime = frameDelta; }
            foreach (MonoBehaviour mono in FrameScripts) if (Ready(mono)) Invoke(mono, mono.Messages.Update);
            CoroutineScheduler.Step(CoroutinePhase.Update);
        }
        finally { updating = false; }
    }
    public static void LateUpdate()
    {
        RequireFrame();
        foreach (MonoBehaviour mono in FrameScripts) if (mono && mono.Started && mono.isActiveAndEnabled) Invoke(mono, mono.Messages.LateUpdate);
    }
    //宿主在本帧渲染提交结束后调用，推进帧尾等待并统一回收对象。
    public static void EndFrame()
    {
        RequireFrame();
        try
        {
            CoroutineScheduler.Step(CoroutinePhase.EndFrame);
            DueDestroy.Clear();
            foreach (var pair in DestroyQueue) if (!pair.Key || pair.Value <= Time.time) DueDestroy.Add(pair.Key);
            foreach (Object obj in DueDestroy) { DestroyQueue.Remove(obj); Object.DestroyImmediate(obj); }
            DueDestroy.Clear();
        }
        finally { frameOpen = false; FrameScripts.Clear(); }
    }
    private static void RequireFrame() { AssertThread(); if (!frameOpen) throw new InvalidOperationException("必须先调用 BeginFrame。"); }
    private static bool Ready(MonoBehaviour mono)
    {
        if (!mono || !mono.isActiveAndEnabled) return false;
        if (!mono.Started)
        {
            mono.Started = true;
            try
            {
                IEnumerator routine = mono.Messages.Start?.Invoke(mono);
                if (routine != null && mono && mono.gameObject.activeInHierarchy) mono.StartCoroutine(routine);
            }
            catch (Exception error) { Report(error, mono); }
        }
        return mono && mono.isActiveAndEnabled;
    }
    internal static void QueueDestroy(Object obj, float delay)
    {
        AssertThread(); float deadline = Time.time + delay;
        if (!DestroyQueue.TryGetValue(obj, out float previous) || deadline < previous) DestroyQueue[obj] = deadline;
    }
}
