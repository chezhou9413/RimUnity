using RimUnity.EventSystems;
using RimUnity.Events;

namespace RimUnity.UI;

//值与填充、手柄锚点共用归一化进度。
public class Slider : Selectable, IPointerDownHandler, IDragHandler
{
    public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
    private Direction currentDirection;
    public Direction direction { get => currentDirection; set { currentDirection = value; Refresh(); } }
    public RectTransform fillRect { get; set; }
    public RectTransform handleRect { get; set; }
    private bool integer;
    private float minimum, maximum = 1;
    public bool wholeNumbers { get => integer; set { integer = value; Set(current, true); } }
    public float minValue { get => minimum; set { minimum = value; Set(current, true); } }
    public float maxValue { get => maximum; set { maximum = value; Set(current, true); } }
    public UnityEvent<float> onValueChanged { get; } = new();
    private float current;
    public float value { get => current; set => Set(value, true); }
    public float normalizedValue
    {
        get => Mathf.Approximately(minValue, maxValue) ? 0 : Mathf.InverseLerp(minValue, maxValue, current);
        set => this.value = Mathf.Lerp(minValue, maxValue, value);
    }

    public void SetValueWithoutNotify(float value) => Set(value, false);
    private void Set(float next, bool notify)
    {
        next = Mathf.Clamp(wholeNumbers ? Mathf.Round(next) : next, minValue, maxValue);
        bool changed = next != current; current = next; Refresh();
        if (changed && notify) onValueChanged.Invoke(next);
    }

    protected override void OnEnable() { base.OnEnable(); Refresh(); }
    //Inspector 初始引用在 OnEnable 前已绑定。
    internal void Refresh()
    {
        int axis = (int)direction >= 2 ? 1 : 0;
        bool reverse = direction == Direction.RightToLeft || direction == Direction.TopToBottom;
        float t = normalizedValue;
        if (fillRect != null)
        {
            Vector2 min = Vector2.zero, max = Vector2.one;
            if (reverse) min[axis] = 1 - t; else max[axis] = t;
            fillRect.anchorMin = min; fillRect.anchorMax = max;
        }
        if (handleRect != null)
        {
            Vector2 min = Vector2.zero, max = Vector2.one; min[axis] = max[axis] = reverse ? 1 - t : t;
            handleRect.anchorMin = min; handleRect.anchorMax = max;
        }
    }

    //点击和拖动使用手柄区域坐标，支持父级缩放及旋转。
    private void Point(PointerEventData data)
    {
        if (!IsInteractable() || data.button != PointerEventData.InputButton.Left) return;
        RectTransform area = (handleRect?.parent ?? fillRect?.parent ?? transform) as RectTransform;
        if (area == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, data.position, null, out Vector2 point);
        int axis = (int)direction >= 2 ? 1 : 0;
        float size = area.rect.size[axis];
        if (size <= 0) return;
        float t = Mathf.Clamp01((point[axis] - area.rect.min[axis]) / size);
        normalizedValue = direction == Direction.RightToLeft || direction == Direction.TopToBottom ? 1 - t : t;
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
        float step = wholeNumbers ? 1f : (maxValue - minValue) * 0.1f;
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
