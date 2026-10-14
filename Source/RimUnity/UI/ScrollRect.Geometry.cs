
namespace RimUnity.UI;

//统一在视口坐标中测量边界，再转换回内容父级的位移。
public partial class ScrollRect
{
    private readonly Vector3[] corners = new Vector3[4];
    private Rect viewBounds, contentBounds;
    private Vector2 rawContentSize;
    private RectTransform View => viewport ?? (RectTransform)transform;

    //小于视口的内容按 pivot 补足边界，保持顶部、居中或底部对齐。
    private void UpdateBounds()
    {
        viewBounds = View.rect;
        if (content == null) { contentBounds = new Rect(); rawContentSize = Vector2.zero; return; }
        content.GetWorldCorners(corners);
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in corners)
        {
            Vector2 local = View.InverseTransformPoint(corner);
            min = Vector2.Min(min, local); max = Vector2.Max(max, local);
        }
        Vector2 size = max - min;
        rawContentSize = size;
        Vector2 center = (min + max) * 0.5f;
        for (int axis = 0; axis < 2; axis++)
        {
            float excess = viewBounds.size[axis] - size[axis];
            if (excess <= 0f) continue;
            center[axis] -= excess * (content.pivot[axis] - 0.5f);
            size[axis] = viewBounds.size[axis];
        }
        contentBounds = new Rect(center - size * 0.5f, size);
    }

    //求应用位移后重新覆盖视口所需的修正量。
    private Vector2 Offset(Vector2 delta)
    {
        if (movementType == MovementType.Unrestricted) return Vector2.zero;
        Vector2 result = Vector2.zero;
        for (int axis = 0; axis < 2; axis++)
        {
            if (axis == 0 ? !horizontal : !vertical) continue;
            float low = viewBounds.min[axis] - contentBounds.min[axis] - delta[axis];
            float high = viewBounds.max[axis] - contentBounds.max[axis] - delta[axis];
            if (axis == 0) result[axis] = low < -0.001f ? low : high > 0.001f ? high : 0f;
            else result[axis] = high > 0.001f ? high : low < -0.001f ? low : 0f;
        }
        return result;
    }

    //归一化位置按左下为零、右上为一，与滚动条一致。
    private Vector2 Normalized()
    {
        Vector2 result = Vector2.zero;
        for (int axis = 0; axis < 2; axis++)
        {
            float hidden = contentBounds.size[axis] - viewBounds.size[axis];
            result[axis] = hidden <= 0.0001f ? (viewBounds.min[axis] > contentBounds.min[axis] ? 1f : 0f) :
                (viewBounds.min[axis] - contentBounds.min[axis]) / hidden;
        }
        return result;
    }

    //把内容父级位移转换为视口方向。
    private Vector2 ToView(Vector2 delta) => View.InverseTransformVector(content.parent.TransformVector(delta));
    //把视口修正量转换为内容父级方向。
    private Vector2 ToParent(Vector2 delta) => content.parent.InverseTransformVector(View.TransformVector(delta));
}
