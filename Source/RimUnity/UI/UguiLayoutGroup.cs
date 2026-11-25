using System.Collections.Generic;
namespace RimUnity.UI;

//收集布局子节点并计算尺寸、对齐和位置，对齐 UGUI 1.0.0 LayoutGroup.SetChildAlongAxisWithScale。
public abstract class UguiLayoutGroup : LayoutGroup
{
    protected RectTransform rectTransform => (RectTransform)transform;
    protected readonly List<RectTransform> rectChildren = new();
    public bool childScaleWidth { get; set => SetProperty(ref field, value); }
    public bool childScaleHeight { get; set => SetProperty(ref field, value); }
    public bool reverseArrangement { get; set => SetProperty(ref field, value); }
    //收集允许参与布局的活动子节点。
    protected void CollectChildren()
    {
        rectChildren.Clear();
        for (int i = 0; i < transform.childCount; i++)
        {
            if (transform.GetChild(i) is not RectTransform r || !r.gameObject.activeSelf) continue;
            bool ignored = false;
            foreach (LayoutElement element in r.GetComponents<LayoutElement>())
                if (element.isActiveAndEnabled && element.ignoreLayout) { ignored = true; break; }
            if (!ignored) rectChildren.Add(r);
        }
        if (reverseArrangement) rectChildren.Reverse();
    }
    protected float GetTotalMinSize(int axis) => axis == 0 ? minWidth : minHeight;
    protected float GetTotalPreferredSize(int axis) => axis == 0 ? preferredWidth : preferredHeight;
    protected float GetTotalFlexibleSize(int axis) => axis == 0 ? flexibleWidth : flexibleHeight;
    protected float GetAlignmentOnAxis(int axis) => (axis == 0 ? (int)childAlignment % 3 : (int)childAlignment / 3) * 0.5f;
    //把多余空间按父布局的对齐方式分配到两端。
    protected float GetStartOffset(int axis, float required) => (axis == 0 ? padding.left : padding.top) +
        (rectTransform.rect.size[axis] - required - (axis == 0 ? padding.horizontal : padding.vertical)) * GetAlignmentOnAxis(axis);
    //写回引擎已有的布局元素属性。
    protected void SetLayoutInputForAxis(float min, float preferred, float flexible, int axis)
    {
        if (axis == 0) { minWidth = min; preferredWidth = preferred; flexibleWidth = flexible; }
        else { minHeight = min; preferredHeight = preferred; flexibleHeight = flexible; }
    }
    protected void SetChildAlongAxis(RectTransform child, int axis, float pos)
        => SetChildAlongAxisWithScale(child, axis, pos, 1f);
    protected void SetChildAlongAxisWithScale(RectTransform child, int axis, float pos, float scaleFactor)
    {
        if (child == null) return;
        child.anchorMin = child.anchorMax = Vector2.up;
        Vector2 point = child.anchoredPosition;
        point[axis] = axis == 0
            ? pos + child.sizeDelta[axis] * child.pivot[axis] * scaleFactor
            : -pos - child.sizeDelta[axis] * (1f - child.pivot[axis]) * scaleFactor;
        child.anchoredPosition = point;
    }
    protected void SetChildAlongAxis(RectTransform child, int axis, float pos, float size)
        => SetChildAlongAxisWithScale(child, axis, pos, size, 1f);
    //对齐 UGUI 1.0.0：sizeDelta 始终写入未缩放尺寸，scaleFactor 仅参与锚点位置换算。
    protected void SetChildAlongAxisWithScale(RectTransform child, int axis, float pos, float size, float scaleFactor)
    {
        if (child == null) return;
        child.anchorMin = child.anchorMax = Vector2.up;
        Vector2 delta = child.sizeDelta;
        delta[axis] = size;
        child.sizeDelta = delta;
        Vector2 point = child.anchoredPosition;
        point[axis] = axis == 0
            ? pos + size * child.pivot[axis] * scaleFactor
            : -pos - size * (1f - child.pivot[axis]) * scaleFactor;
        child.anchoredPosition = point;
    }
}
