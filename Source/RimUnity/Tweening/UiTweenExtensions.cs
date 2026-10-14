using System;
using RimUnity.UI;

namespace RimUnity.Tweening;

//直接更新 RimUnity UI 属性，后续布局和绘制读取同一对象。
public static class UiTweenExtensions
{
    private static Tween Float(Object target, Func<float> read, Action<float> write, float end, float seconds)
    {
        float start = 0;
        return new Tween(seconds, () => start = read(), t => write(start + (end - start) * t)) { target = target };
    }
    public static Tween DOAnchorPosX(this RectTransform rect, float end, float seconds)
        => Float(rect, () => rect.anchoredPosition.x, x => rect.anchoredPosition = new(x, rect.anchoredPosition.y), end, seconds);
    public static Tween DOAnchorPosY(this RectTransform rect, float end, float seconds)
        => Float(rect, () => rect.anchoredPosition.y, y => rect.anchoredPosition = new(rect.anchoredPosition.x, y), end, seconds);
    public static Tween DOAnchorPos(this RectTransform rect, Vector2 end, float seconds)
    {
        Vector2 start = default;
        return new Tween(seconds, () => start = rect.anchoredPosition,
            t => rect.anchoredPosition = start + (end - start) * t) { target = rect };
    }
    public static Tween DOScaleX(this Transform transform, float end, float seconds)
        => Float(transform, () => transform.localScale.x,
            x => transform.localScale = new(x, transform.localScale.y, transform.localScale.z), end, seconds);
    public static Tween DOFade(this Graphic graphic, float end, float seconds)
        => Float(graphic, () => graphic.color.a,
            a => graphic.color = new(graphic.color.r, graphic.color.g, graphic.color.b, a), end, seconds);
    public static void DOKill(this Object target) => DOTween.KillTarget(target);
    //缩放插值不钳制缓动结果，保留 Back 曲线的超调。
    public static Tween DOScale(this Transform transform, Vector3 end, float seconds)
    {
        Vector3 start = default;
        return new Tween(seconds, () => start = transform.localScale, t => transform.localScale = start + (end - start) * t) { target = transform };
    }
    public static Tween DOScale(this Transform transform, float end, float seconds) => DOScale(transform, Vector3.one * end, seconds);
    public static Tween DOLocalMoveY(this Transform transform, float end, float seconds)
        => Float(transform, () => transform.localPosition.y, y => transform.localPosition = new(transform.localPosition.x, y, transform.localPosition.z), end, seconds);
    //默认旋转沿每个欧拉轴的最短角度运动。
    public static Tween DOLocalRotate(this Transform transform, Vector3 end, float seconds)
    {
        Vector3 start = default, delta = default;
        return new Tween(seconds, () =>
        {
            start = transform.localEulerAngles;
            delta = new(Mathf.DeltaAngle(start.x, end.x), Mathf.DeltaAngle(start.y, end.y), Mathf.DeltaAngle(start.z, end.z));
        }, t => transform.localRotation = Quaternion.Euler(start + delta * t)) { target = transform };
    }
}
