using RimUnity.EventSystems;
using RimUnity.Events;

namespace RimUnity.UI;

//滚动条把手柄尺寸与可移动范围分开计算。
public class Scrollbar : Selectable, IPointerDownHandler, IDragHandler
{
    public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
    public Direction direction { get; set; }
    public RectTransform handleRect { get; set; }
    public int numberOfSteps { get; set; }
    public UnityEvent<float> onValueChanged { get; } = new();
    private float current, extent = 0.2f;
    public float value { get => current; set => Set(value, true); }
    public float size { get => extent; set { extent = Mathf.Clamp01(value); Refresh(); } }
    public void SetValueWithoutNotify(float next) => Set(next, false);

    private void Set(float next, bool notify)
    {
        next = Mathf.Clamp01(next);
        if (numberOfSteps > 1) next = Mathf.Round(next * (numberOfSteps - 1)) / (numberOfSteps - 1);
        bool changed = current != next; current = next; Refresh();
        if (changed && notify) onValueChanged.Invoke(next);
    }
    protected override void OnEnable() { base.OnEnable(); Refresh(); }
    //锚点范围表示占比，额外的 sizeDelta 仍由作者控制。
    private void Refresh()
    {
        if (handleRect == null) return;
        int axis = (int)direction >= 2 ? 1 : 0;
        float t = direction == Direction.RightToLeft || direction == Direction.TopToBottom ? 1 - current : current;
        Vector2 min = Vector2.zero, max = Vector2.one;
        min[axis] = t * (1 - extent); max[axis] = min[axis] + extent;
        handleRect.anchorMin = min; handleRect.anchorMax = max;
    }
    private void Point(PointerEventData data)
    {
        if (!IsInteractable() || data.button != PointerEventData.InputButton.Left || handleRect?.parent is not RectTransform area) return;
        int axis = (int)direction >= 2 ? 1 : 0;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, data.position, null, out Vector2 point);
        if (area.rect.size[axis] <= 0 || extent >= 1) return;
        float t = ((point[axis] - area.rect.min[axis]) / area.rect.size[axis] - extent * 0.5f) / (1 - extent);
        value = direction == Direction.RightToLeft || direction == Direction.TopToBottom ? 1 - t : t;
    }
    public void OnPointerDown(PointerEventData data) => Point(data);
    public void OnDrag(PointerEventData data) => Point(data);

    //在数值轴没有显式邻居时，方向键按步长调整当前值。
    public override void OnMove(AxisEventData data)
    {
        bool horizontal = (int)direction < 2;
        bool sameAxis = horizontal ? data.moveDir == MoveDirection.Left || data.moveDir == MoveDirection.Right :
            data.moveDir == MoveDirection.Up || data.moveDir == MoveDirection.Down;
        if (!IsInteractable() || !sameAxis || NavigationTarget(data.moveDir) != null) { base.OnMove(data); return; }
        bool positive = data.moveDir == MoveDirection.Right || data.moveDir == MoveDirection.Up;
        bool reverse = direction == Direction.RightToLeft || direction == Direction.TopToBottom;
        float step = numberOfSteps > 1 ? 1f / (numberOfSteps - 1) : 0.1f;
        value += positive != reverse ? step : -step;
        data.Use();
    }
    //自动模式保留数值轴按键，其他方向继续导航。
    public override Selectable FindSelectableOnLeft() => navigation.mode == Navigation.Mode.Automatic && (int)direction < 2 ? null : base.FindSelectableOnLeft();
    //自动模式保留数值轴按键，其他方向继续导航。
    public override Selectable FindSelectableOnRight() => navigation.mode == Navigation.Mode.Automatic && (int)direction < 2 ? null : base.FindSelectableOnRight();
    //自动模式保留数值轴按键，其他方向继续导航。
    public override Selectable FindSelectableOnUp() => navigation.mode == Navigation.Mode.Automatic && (int)direction >= 2 ? null : base.FindSelectableOnUp();
    //自动模式保留数值轴按键，其他方向继续导航。
    public override Selectable FindSelectableOnDown() => navigation.mode == Navigation.Mode.Automatic && (int)direction >= 2 ? null : base.FindSelectableOnDown();
}
