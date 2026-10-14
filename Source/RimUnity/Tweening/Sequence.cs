using System;
using System.Collections.Generic;

namespace RimUnity.Tweening;

//将位移、等待和回调按时间顺序组合，子项不再独立更新。
public sealed class Sequence : Tween
{
    private readonly List<(float start, Tween tween, Action callback)> items = new();
    private float lastStart;
    internal Sequence() : base(0) { }
    public Sequence Append(Tween tween)
    {
        ArgumentNullException.ThrowIfNull(tween);
        if (started || tween.started || tween.parent != null || !tween.active || tween == this)
            throw new InvalidOperationException("只能将未开始的独立补间加入顺序动画。");
        if (tween.loops != 1) throw new NotSupportedException("循环应设置在顺序动画上。");
        tween.parent = this; DOTween.Remove(tween);
        lastStart = duration; items.Add((duration, tween, null)); duration += tween.duration;
        return this;
    }
    //与最后追加的段同时开始，延长总时长但不改变其余并行动画的起点。
    public Sequence Join(Tween tween)
    {
        ArgumentNullException.ThrowIfNull(tween);
        if (started || tween.started || tween.parent != null || !tween.active || tween == this || tween.loops != 1)
            throw new InvalidOperationException("只能合并尚未开始且不循环的独立动画。");
        tween.parent = this; DOTween.Remove(tween);
        items.Add((lastStart, tween, null)); duration = Math.Max(duration, lastStart + tween.duration);
        return this;
    }
    public Sequence AppendCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (started) throw new InvalidOperationException("已开始的顺序动画不能追加回调。");
        items.Add((duration, null, callback)); return this;
    }
    public Sequence AppendInterval(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0 || started) throw new ArgumentOutOfRangeException(nameof(seconds));
        duration += seconds; return this;
    }
    internal override void Evaluate(float previous, float current)
    {
        started = true;
        foreach (var item in items)
        {
            if (!active) break;
            if (current < item.start) continue;
            if (item.callback != null)
            {
                if (previous < item.start) item.callback();
            }
            else if (previous < item.start + item.tween.duration)
                item.tween.Sample(previous < item.start ? -1 : previous - item.start,
                    Math.Min(current - item.start, item.tween.duration));
        }
    }
    internal override void ResetCycle()
    {
        base.ResetCycle();
        foreach (var item in items) item.tween?.ResetCycle();
    }
}
