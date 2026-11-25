using RimUnity.EventSystems;
using RimUnity.Events;

namespace RimUnity.UI;

//滚动条把手柄尺寸与可移动范围分开计算，交互行为对齐 UGUI 1.0.0 Scrollbar。
public class Scrollbar : Selectable, IBeginDragHandler, IDragHandler, IInitializePotentialDragHandler
{
    public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
    private Direction _currentDirection;
    private RectTransform _currentHandleRect;
    private int _steps;
    private float current, extent = 0.2f;
    private Vector2 _dragOffset;

    public Direction direction { get => _currentDirection; set { _currentDirection = value; Refresh(); } }
    public RectTransform handleRect { get => _currentHandleRect; set { _currentHandleRect = value; Refresh(); } }
    public int numberOfSteps { get => _steps; set { _steps = value; Set(current, true); } }
    public UnityEvent<float> onValueChanged { get; } = new();
    public float value { get => current; set => Set(value, true); }
    public float size { get => extent; set { extent = Mathf.Clamp01(value); Refresh(); } }
    public void SetValueWithoutNotify(float next) => Set(next, false);

    private int AxisIndex => (int)direction >= 2 ? 1 : 0;
    private bool ReverseValue => direction == Direction.RightToLeft || direction == Direction.TopToBottom;
    private RectTransform ContainerRect => handleRect != null ? handleRect.parent as RectTransform : null;

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
        int axis = AxisIndex;
        float movement = Mathf.Clamp01(current) * (1f - extent);
        Vector2 min = Vector2.zero, max = Vector2.one;
        if (ReverseValue)
        {
            min[axis] = 1f - movement - extent;
            max[axis] = 1f - movement;
        }
        else
        {
            min[axis] = movement;
            max[axis] = movement + extent;
        }
        Runtime.UguiDirty.SetScrollbarAnchors(handleRect, min, max);
    }

    public virtual void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;

    public virtual void OnBeginDrag(PointerEventData data)
    {
        if (!_MayDrag(data) || ContainerRect == null)
        {
            return;
        }
        _dragOffset = Vector2.zero;
        Vector2 pressPosition = data.pointerPressRaycast.isValid ? data.pointerPressRaycast.screenPosition : data.pressPosition;
        if (RectTransformUtility.RectangleContainsScreenPoint(handleRect, pressPosition, data.enterEventCamera))
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(handleRect, pressPosition, data.pressEventCamera, out Vector2 localMousePosition))
            {
                _dragOffset = localMousePosition - handleRect.rect.center;
            }
        }
    }

    public virtual void OnDrag(PointerEventData data)
    {
        if (!_MayDrag(data))
        {
            return;
        }
        _UpdateDrag(data);
    }

    public override void OnPointerDown(PointerEventData data)
    {
        if (!_MayDrag(data))
        {
            return;
        }
        base.OnPointerDown(data);
        if (handleRect == null)
        {
            return;
        }

        //点击滑块外部轨道时按一页步长向点击侧移动，点击滑块本身时不突跳。
        Vector2 pressPosition = data.pointerPressRaycast.isValid ? data.pointerPressRaycast.screenPosition : data.position;
        if (!RectTransformUtility.RectangleContainsScreenPoint(handleRect, pressPosition, data.enterEventCamera))
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(handleRect, pressPosition, data.enterEventCamera, out Vector2 localMousePosition))
            {
                Vector2 offsetFromHandleCenter = localMousePosition - handleRect.rect.center;
                float axisCoordinate = AxisIndex == 0 ? offsetFromHandleCenter.x : offsetFromHandleCenter.y;
                float stepDelta = axisCoordinate < 0f ? extent : -extent;
                float steppedValue = Mathf.Clamp01(current + (ReverseValue ? stepDelta : -stepDelta));
                value = Mathf.Round(steppedValue * 10000f) / 10000f;
            }
        }
    }

    public override void OnPointerUp(PointerEventData data)
    {
        base.OnPointerUp(data);
    }

    private bool _MayDrag(PointerEventData data)
        => IsActive() && IsInteractable() && data.button == PointerEventData.InputButton.Left;

    private void _UpdateDrag(PointerEventData data)
    {
        RectTransform containerRect = ContainerRect;
        if (containerRect == null)
        {
            return;
        }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(containerRect, data.position, data.pressEventCamera, out Vector2 localCursor))
        {
            return;
        }

        Vector2 handleCenterRelativeToContainerCorner = localCursor - _dragOffset - containerRect.rect.position;
        Vector2 handleCorner = handleCenterRelativeToContainerCorner - (handleRect.rect.size - handleRect.sizeDelta) * 0.5f;

        int axis = AxisIndex;
        float parentSize = axis == 0 ? containerRect.rect.width : containerRect.rect.height;
        float remainingSize = parentSize * (1f - extent);
        if (remainingSize <= 0f)
        {
            return;
        }

        float ratio = Mathf.Clamp01(handleCorner[axis] / remainingSize);
        Set(ReverseValue ? 1f - ratio : ratio, true);
    }

    //在数值轴没有显式邻居时，方向键按步长调整当前值。
    public override void OnMove(AxisEventData data)
    {
        bool horizontal = (int)direction < 2;
        bool sameAxis = horizontal ? data.moveDir == MoveDirection.Left || data.moveDir == MoveDirection.Right :
            data.moveDir == MoveDirection.Up || data.moveDir == MoveDirection.Down;
        if (!IsInteractable() || !sameAxis || NavigationTarget(data.moveDir) != null) { base.OnMove(data); return; }
        bool positive = data.moveDir == MoveDirection.Right || data.moveDir == MoveDirection.Up;
        float step = numberOfSteps > 1 ? 1f / (numberOfSteps - 1) : 0.1f;
        value += positive != ReverseValue ? step : -step;
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
