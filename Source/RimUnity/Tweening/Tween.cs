using System;

namespace RimUnity.Tweening;

//界面补间使用 RimUnity 帧时间和对象生命周期，不依赖宿主 Unity 组件。
public class Tween
{
    internal float duration, position;
    internal bool active = true, playing = true, unscaled, started;
    internal object owner;
    internal Object target;
    internal GameObject link;
    internal bool linked;
    internal Sequence parent;
    internal int loops = 1, completedLoops;
    private Ease ease = Ease.OutQuad;
    private readonly Action<float> apply;
    private readonly Action capture;
    private Action complete;

    //延迟到首次推进时读取起点，保证顺序动画从上一段的终点开始。
    internal Tween(float seconds, Action capture = null, Action<float> apply = null)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        duration = seconds; this.capture = capture; this.apply = apply;
        DOTween.Register(this);
    }
    public bool IsActive() => active;
    public bool IsPlaying() => active && playing;
    public float Elapsed(bool includeLoops = true) => position + (includeLoops ? completedLoops * duration : 0);
    public void Pause() => playing = false;
    public void Play() => playing = true;
    public void Kill(bool complete = false)
    {
        if (!active) return;
        if (complete) Evaluate(position, duration);
        active = false;
    }
    internal void SetCompletion(Action callback) => complete = callback;
    internal void SetEasing(Ease value) => ease = value;

    //以段为单位消费帧增量，跨过多个边界时仍按顺序执行回调。
    internal void Step(float delta)
    {
        while (active && playing)
        {
            float previous = started ? position : -1;
            float advance = Math.Min(delta, duration - position);
            position += advance; delta -= advance;
            Evaluate(previous, position);
            if (!active || position < duration) return;
            completedLoops++;
            if (loops >= 0 && completedLoops >= loops)
            {
                active = false;
                complete?.Invoke();
                return;
            }
            if (duration == 0) throw new InvalidOperationException("无限循环补间必须具有正时长。");
            ResetCycle();
            if (delta <= 0) return;
        }
    }

    //顺序动画的子项由父项推进，完成通知不重复执行。
    internal void Sample(float previous, float current)
    {
        Evaluate(previous, current);
        if (previous < duration && current >= duration) complete?.Invoke();
    }
    internal virtual void Evaluate(float previous, float current)
    {
        if (!started) { capture?.Invoke(); started = true; }
        float t = duration == 0 ? 1 : Math.Clamp(current / duration, 0, 1);
        t = ease switch
        {
            Ease.InQuad => t * t,
            Ease.OutQuad => 1 - (1 - t) * (1 - t),
            Ease.InSine => 1 - MathF.Cos(t * MathF.PI * .5f),
            Ease.OutCubic => 1 - MathF.Pow(1 - t, 3),
            _ => t
        };
        apply?.Invoke(t);
    }
    internal virtual void ResetCycle() { position = 0; started = false; }
}

//只提供当前界面实际使用的缓动和循环方式。
public enum Ease { Linear, InQuad, OutQuad, InSine, OutCubic }
public enum LoopType { Restart }
public enum LinkBehaviour { KillOnDisable }
