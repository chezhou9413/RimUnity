using System.Collections.Generic;
using RimUnity.UI;

namespace RimUnity.UI.Runtime;

//先完成横向布局，再用实际宽度测量纵向内容。
internal static class UguiLayout
{
    //尺寸提供者按优先级参与竞争，同级取较大值，负值表示未指定。
    private static float Property(RectTransform rect, int axis, int kind)
    {
        float result = 0f;
        int priority = int.MinValue;
        foreach (Component component in rect.GetComponents<Component>())
        {
            if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled) continue;
            float value;
            int candidatePriority;
            if (component is ILayoutElement element)
            {
                value = kind == 0 ? (axis == 0 ? element.minWidth : element.minHeight) : kind == 1 ?
                    (axis == 0 ? element.preferredWidth : element.preferredHeight) :
                    (axis == 0 ? element.flexibleWidth : element.flexibleHeight);
                candidatePriority = element.layoutPriority;
            }
            else if (component is RimUnity.UI.Image image)
            {
                value = kind == 2 ? -1f : kind == 0 ? 0f : ImagePreferredSize(image)[axis];
                candidatePriority = 0;
            }
            else if (component is RimUnity.UI.Text text)
            {
                value = kind == 2 ? -1f : kind == 0 ? 0f : TextPreferredSize(text, rect)[axis];
                candidatePriority = 0;
            }
            else continue;
            if (value < 0 || candidatePriority < priority) continue;
            result = candidatePriority > priority ? value : Mathf.Max(result, value);
            priority = candidatePriority;
        }
        return result;
    }

    //读取布局允许的最小尺寸。
    internal static float GetMinSize(RectTransform rect, int axis) => Property(rect, axis, 0);
    //首选尺寸始终不小于最小尺寸。
    internal static float GetPreferredSize(RectTransform rect, int axis) => Mathf.Max(GetMinSize(rect, axis), Property(rect, axis, 1));
    //读取分配剩余空间时的权重。
    internal static float GetFlexibleSize(RectTransform rect, int axis) => Property(rect, axis, 2);

    //每个轴先自底向上测量，再自顶向下分配尺寸。
    internal static void Rebuild(RectTransform root)
    {
        for (int axis = 0; axis < 2; axis++)
        {
            Measure(root, axis);
            Arrange(root, axis);
        }
    }

    //只把活动的矩形子节点纳入布局遍历。
    internal static List<RectTransform> Children(RectTransform parent)
    {
        List<RectTransform> result = new();
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i) is RectTransform child && child.gameObject.activeSelf) result.Add(child);
        return result;
    }

    //子节点的测量结果先于父布局可用。
    private static void Measure(RectTransform rect, int axis)
    {
        foreach (RectTransform child in Children(rect)) Measure(child, axis);
        foreach (Component component in rect.GetComponents<Component>())
        {
            if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled) continue;
            if (component is not ILayoutElement element) continue;
            if (axis == 0) element.CalculateLayoutInputHorizontal();
            else element.CalculateLayoutInputVertical();
        }
    }

    //自适应先调整当前节点，再让布局组控制子节点。
    private static void Arrange(RectTransform rect, int axis)
    {
        rect.RefreshAnchors();
        Component[] components = rect.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled) continue;
            if (component is not ILayoutSelfController self) continue;
            if (axis == 0) self.SetLayoutHorizontal();
            else self.SetLayoutVertical();
        }
        foreach (Component component in components)
        {
            if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled) continue;
            if (component is not ILayoutController controller || component is ILayoutSelfController) continue;
            if (axis == 0) controller.SetLayoutHorizontal();
            else controller.SetLayoutVertical();
        }
        foreach (RectTransform child in Children(rect)) Arrange(child, axis);
    }

    //换行高度使用父布局已经分配的宽度。
    private static Vector2 TextPreferredSize(RimUnity.UI.Text text, RectTransform rect)
    {
        Vector2 size = UguiTextRenderer.Measure(text, -1, false);
        if (text.wordWrap) size.y = UguiTextRenderer.Measure(text, Mathf.Max(1f, rect.rect.width), true).y;
        return size;
    }

    //图片使用画布像素比例，九宫格和平铺只要求容纳边框。
    private static Vector2 ImagePreferredSize(RimUnity.UI.Image image)
    {
        Sprite sprite = image.overrideSprite ?? image.sprite;
        if (sprite == null) return Vector2.zero;
        Canvas canvas = image.GetComponentInParent<Canvas>();
        float ratio = sprite.pixelsPerUnit / (canvas != null ? canvas.referencePixelsPerUnit : 100f);
        Vector2 size = image.type == RimUnity.UI.Image.Type.Sliced || image.type == RimUnity.UI.Image.Type.Tiled
            ? new Vector2(sprite.border.x + sprite.border.z, sprite.border.y + sprite.border.w)
            : sprite.rect.size;
        return size / ratio;
    }
}
