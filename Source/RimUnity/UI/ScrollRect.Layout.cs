
namespace RimUnity.UI;

//滚动条可常驻、自动隐藏，或在隐藏时把空间还给视口。
public partial class ScrollRect
{
    public enum ScrollbarVisibility { Permanent, AutoHide, AutoHideAndExpandViewport }
    public ScrollbarVisibility horizontalScrollbarVisibility { get; set => SetProperty(ref field, value); }
    public ScrollbarVisibility verticalScrollbarVisibility { get; set => SetProperty(ref field, value); }
    public float horizontalScrollbarSpacing { get; set => SetProperty(ref field, value); }
    public float verticalScrollbarSpacing { get; set => SetProperty(ref field, value); }
    private bool NeedsHorizontal => horizontal && rawContentSize.x > viewBounds.width + 0.01f;
    private bool NeedsVertical => vertical && rawContentSize.y > viewBounds.height + 0.01f;

    //只有视口和滚动条均为直属子节点时才控制其布局。
    private bool CanExpand => View.parent == transform &&
        (horizontalBar == null || horizontalBar.transform.parent == transform) &&
        (verticalBar == null || verticalBar.transform.parent == transform);
    private bool ExpandHorizontal => CanExpand && horizontalBar != null && horizontalScrollbarVisibility == ScrollbarVisibility.AutoHideAndExpandViewport;
    private bool ExpandVertical => CanExpand && verticalBar != null && verticalScrollbarVisibility == ScrollbarVisibility.AutoHideAndExpandViewport;
    private float HorizontalSpace => ((RectTransform)horizontalBar.transform).rect.height + horizontalScrollbarSpacing;
    private float VerticalSpace => ((RectTransform)verticalBar.transform).rect.width + verticalScrollbarSpacing;

    //先用完整视口测量内容，再为实际需要的滚动条预留空间。
    public void SetLayoutHorizontal()
    {
        if (content == null) return;
        bool expandHorizontal = ExpandHorizontal, expandVertical = ExpandVertical;
        if (!expandHorizontal && !expandVertical) return;
        if (content == transform) throw new System.InvalidOperationException("ScrollRect 的 content 不能是自身矩形。");
        View.anchorMin = Vector2.zero;
        View.anchorMax = Vector2.one;
        View.sizeDelta = Vector2.zero;
        View.anchoredPosition = Vector2.zero;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        UpdateBounds();
        if (expandVertical && NeedsVertical)
        {
            View.sizeDelta = new Vector2(-VerticalSpace, View.sizeDelta.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            UpdateBounds();
        }
        if (expandHorizontal && NeedsHorizontal)
        {
            View.sizeDelta = new Vector2(View.sizeDelta.x, -HorizontalSpace);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            UpdateBounds();
        }
        if (expandVertical && NeedsVertical && View.sizeDelta.x == 0f && View.sizeDelta.y < 0f)
        {
            View.sizeDelta = new Vector2(-VerticalSpace, View.sizeDelta.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }
    }

    //相邻滚动条为彼此留出角落，隐藏时恢复完整长度。
    public void SetLayoutVertical()
    {
        UpdateBounds();
        if (ExpandVertical && horizontalBar != null)
            FitBar((RectTransform)horizontalBar.transform, 0, NeedsVertical ? VerticalSpace : 0f);
        if (ExpandHorizontal && verticalBar != null)
            FitBar((RectTransform)verticalBar.transform, 1, NeedsHorizontal ? HorizontalSpace : 0f);
        UpdateScrollbarVisibility();
    }

    //只控制滚动条的长度轴，保留厚度与另外一轴的位置。
    private static void FitBar(RectTransform rect, int axis, float space)
    {
        Vector2 min = rect.anchorMin, max = rect.anchorMax, size = rect.sizeDelta, position = rect.anchoredPosition;
        min[axis] = 0f; max[axis] = 1f; size[axis] = -space; position[axis] = 0f;
        rect.anchorMin = min; rect.anchorMax = max; rect.sizeDelta = size; rect.anchoredPosition = position;
    }

    //自动隐藏依据实际内容尺寸，常驻模式只跟随滚动轴开关。
    private void UpdateScrollbarVisibility()
    {
        Show(horizontalBar, horizontalScrollbarVisibility == ScrollbarVisibility.Permanent ? horizontal : NeedsHorizontal);
        Show(verticalBar, verticalScrollbarVisibility == ScrollbarVisibility.Permanent ? vertical : NeedsVertical);
    }

    //避免对状态未变的对象重复调用生命周期。
    private static void Show(Scrollbar bar, bool visible)
    {
        if (bar != null && bar.gameObject.activeSelf != visible) bar.gameObject.SetActive(visible);
    }
}
