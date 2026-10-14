using System;
using System.Collections.Generic;
using RimUnity.Runtime;

namespace RimUnity.Tweening;

//统一推进界面动画；关闭界面、销毁目标或更换游戏时释放动画引用。
public static class DOTween
{
    private static readonly List<Tween> tweens = new();
    internal static void Register(Tween tween)
    {
        NativeRuntime.AssertThread();
        tween.owner = NativeRuntime.SceneOwnerProvider?.Invoke();
        tweens.Add(tween);
    }
    internal static void Remove(Tween tween) => tweens.Remove(tween);
    public static Sequence Sequence() => new();
    internal static void Update()
    {
        object owner = NativeRuntime.SceneOwnerProvider?.Invoke();
        foreach (Tween tween in tweens.ToArray())
        {
            if (!ReferenceEquals(owner, tween.owner) ||
                (!ReferenceEquals(tween.target, null) && !tween.target) ||
                (tween.linked && (!tween.link || !tween.link.activeInHierarchy))) tween.Kill();
            if (tween.active && tween.playing)
            {
                try { tween.Step(tween.unscaled ? Time.unscaledDeltaTime : Time.deltaTime); }
                catch (Exception error) { tween.Kill(); Verse.Log.Error("[RimUnity] 界面动画失败：" + error); }
            }
        }
        tweens.RemoveAll(t => !t.active);
    }
    internal static void Clear() { foreach (Tween tween in tweens) tween.Kill(); tweens.Clear(); }
    internal static void KillTarget(Object target)
    {
        foreach (Tween tween in tweens.ToArray()) if (ReferenceEquals(tween.target, target)) tween.Kill();
    }
}

//保留调用侧的链式类型，顺序动画配置后仍可追加子动画。
public static class TweenSettingsExtensions
{
    public static T SetEase<T>(this T tween, Ease ease) where T : Tween { tween.SetEasing(ease); return tween; }
    public static T SetUpdate<T>(this T tween, bool independent) where T : Tween { tween.unscaled = independent; return tween; }
    public static T SetLink<T>(this T tween, GameObject target, LinkBehaviour behaviour) where T : Tween
    { tween.link = target ?? throw new ArgumentNullException(nameof(target)); tween.linked = true; return tween; }
    public static T SetLoops<T>(this T tween, int loops, LoopType type) where T : Tween
    { if (loops == 0 || loops < -1) throw new ArgumentOutOfRangeException(nameof(loops)); tween.loops = loops; return tween; }
    public static T OnComplete<T>(this T tween, Action callback) where T : Tween { tween.SetCompletion(callback); return tween; }
}

//延迟调用与其他界面动画共享场景清理和时间来源。
public static class DOVirtual
{
    public static Tween DelayedCall(float delay, Action callback, bool ignoreTimeScale = true)
        => new Tween(delay).SetUpdate(ignoreTimeScale).OnComplete(callback);
}
