using System;
using RimUnity.UI;

namespace RimUnity.UI;

//让矩形按指定宽高比跟随自身尺寸或父矩形，对齐 UGUI 1.0.0 AspectRatioFitter。
public class AspectRatioFitter : MonoBehaviour, ILayoutSelfController
{
    public enum AspectMode { None, WidthControlsHeight, HeightControlsWidth, FitInParent, EnvelopeParent }
    private AspectMode _aspectMode;
    public AspectMode aspectMode
    {
        get => _aspectMode;
        set
        {
            if (SetProperty(ref _aspectMode, value)) Apply();
        }
    }
    private float ratio = 1f;
    public float aspectRatio
    {
        get => ratio;
        set
        {
            if (SetProperty(ref ratio, value)) Apply();
        }
    }

    //横向布局时同步由高度或父矩形决定的尺寸。
    public void SetLayoutHorizontal() => Apply();
    //纵向布局时使用父布局最终分配的尺寸。
    public void SetLayoutVertical() => Apply();
    //无画布的动态对象也在帧更新中应用比例。
    private void LateUpdate() => Apply();

    //屏幕根画布尺寸由画布管理，不接受比例控制。
    public bool IsComponentValidOnObject()
    {
        Canvas canvas = GetComponent<Canvas>();
        return canvas == null || canvas.renderMode == RenderMode.WorldSpace ||
            (transform.parent != null && transform.parent.GetComponentInParent<Canvas>() != null);
    }

    //填充父级的模式必须有矩形父节点。
    public bool IsAspectModeValid() => _aspectMode != AspectMode.FitInParent && _aspectMode != AspectMode.EnvelopeParent ||
        transform.parent is RectTransform;

    //全拉伸锚点保留父级位置语义，差值只改变满足比例所需的轴。
    private void Apply()
    {
        if (!isActiveAndEnabled || !float.IsFinite(ratio) || ratio <= 0f || !IsComponentValidOnObject() || !IsAspectModeValid()) return;
        RectTransform rect = (RectTransform)transform;
        if (_aspectMode == AspectMode.WidthControlsHeight)
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rect.rect.width / ratio);
        else if (_aspectMode == AspectMode.HeightControlsWidth)
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rect.rect.height * ratio);
        else if (_aspectMode == AspectMode.FitInParent || _aspectMode == AspectMode.EnvelopeParent)
        {
            Vector2 parentSize = ((RectTransform)rect.parent).rect.size;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            Vector2 delta = Vector2.zero;
            bool useWidth = (parentSize.y * ratio < parentSize.x) ^ (_aspectMode == AspectMode.FitInParent);
            if (useWidth) delta.y = parentSize.x / ratio - parentSize.y;
            else delta.x = parentSize.y * ratio - parentSize.x;
            rect.sizeDelta = delta;
            rect.anchoredPosition = Vector2.zero;
        }
    }
}
