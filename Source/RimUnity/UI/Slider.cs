using RimUnity.EventSystems;
using RimUnity.Events;

namespace RimUnity.UI;

//值与填充、手柄锚点共用归一化进度，交互行为对齐 UGUI 1.0.0 Slider。
public class Slider : Selectable, IDragHandler, IInitializePotentialDragHandler
{
    public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
    private Direction _currentDirection;
    private RectTransform _currentFillRect;
    private RectTransform _currentHandleRect;
    private bool integer;
    private float minimum, maximum = 1;
    private float current;
    private Vector2 _dragOffset;

    public Direction direction { get => _currentDirection; set { _currentDirection = value; Refresh(); } }
    public RectTransform fillRect { get => _currentFillRect; set { _currentFillRect = value; Refresh(); } }
    public RectTransform handleRect { get => _currentHandleRect; set { _currentHandleRect = value; Refresh(); } }
    public bool wholeNumbers { get => integer; set { integer = value; Set(current, true); } }
    public float minValue { get => minimum; set { minimum = value; Set(current, true); } }
    public float maxValue { get => maximum; set { maximum = value; Set(current, true); } }
    public UnityEvent<float> onValueChanged { get; } = new();
    public float value { get => current; set => Set(value, true); }
    public float normalizedValue
    {
        get => Mathf.Approximately(minValue, maxValue) ? 0 : Mathf.InverseLerp(minValue, maxValue, current);
        set => this.value = Mathf.Lerp(minValue, maxValue, value);
    }

    private int AxisIndex => (int)direction >= 2 ? 1 : 0;
    private bool ReverseValue => direction == Direction.RightToLeft || direction == Direction.TopToBottom;
    private RectTransform HandleContainerRect => handleRect != null && handleRect != transform ? handleRect.parent as RectTransform : null;
    private RectTransform FillContainerRect => fillRect != null && fillRect != transform ? fillRect.parent as RectTransform : null;

    public void SetValueWithoutNotify(float value) => Set(value, false);

    private void Set(float next, bool notify)
    {
        next = _ClampValue(next);
        bool changed = next != current; current = next; Refresh();
        if (changed && notify) onValueChanged.Invoke(next);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Set(current, false);
        Refresh();
    }

    //Inspector 初始引用在 OnEnable 前已绑定；支持 Filled 类型 Image 与拉伸锚点两种填充。
    internal void Refresh()
    {
        int axis = AxisIndex;
        bool reverse = ReverseValue;
        float progress = normalizedValue;
        if (fillRect != null)
        {
            Vector2 min = Vector2.zero, max = Vector2.one;
            Image fillImage = fillRect.GetComponent<Image>();
            if (fillImage != null && fillImage.type == Image.Type.Filled)
            {
                fillImage.fillAmount = progress;
            }
            else if (reverse)
            {
                min[axis] = 1f - progress;
            }
            else
            {
                max[axis] = progress;
            }
            fillRect.anchorMin = min; fillRect.anchorMax = max;
            fillRect.RefreshAnchors();
        }
        if (handleRect != null)
        {
            Vector2 min = Vector2.zero, max = Vector2.one;
            min[axis] = max[axis] = reverse ? 1f - progress : progress;
            handleRect.anchorMin = min; handleRect.anchorMax = max;
            handleRect.RefreshAnchors();
        }
    }

    public virtual void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;

    public override void OnPointerDown(PointerEventData data)
    {
        if (!_MayDrag(data))
        {
            return;
        }
        base.OnPointerDown(data);

        _dragOffset = Vector2.zero;
        Vector2 pressPosition = data.pointerPressRaycast.isValid ? data.pointerPressRaycast.screenPosition : data.position;
        if (HandleContainerRect != null && RectTransformUtility.RectangleContainsScreenPoint(handleRect, pressPosition, data.enterEventCamera))
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(handleRect, pressPosition, data.pressEventCamera, out Vector2 localMousePosition))
            {
                _dragOffset = localMousePosition;
            }
        }
        else
        {
            _UpdateDrag(data, data.pressEventCamera);
        }
    }

    public override void OnPointerUp(PointerEventData data)
    {
        base.OnPointerUp(data);
    }

    public virtual void OnDrag(PointerEventData data)
    {
        if (!_MayDrag(data))
        {
            return;
        }
        _UpdateDrag(data, data.pressEventCamera);
    }

    private float _ClampValue(float inputValue)
    {
        float clampedValue = Mathf.Clamp(inputValue, minValue, maxValue);
        if (wholeNumbers)
        {
            clampedValue = Mathf.Round(clampedValue);
        }
        return clampedValue;
    }

    private bool _MayDrag(PointerEventData data)
        => IsActive() && IsInteractable() && data.button == PointerEventData.InputButton.Left;

    private void _UpdateDrag(PointerEventData data, Camera eventCamera)
    {
        RectTransform clickRect = HandleContainerRect != null ? HandleContainerRect : FillContainerRect;
        if (clickRect == null)
        {
            clickRect = transform as RectTransform;
        }
        int axis = AxisIndex;
        if (clickRect == null || clickRect.rect.size[axis] <= 0f)
        {
            return;
        }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(clickRect, data.position, eventCamera, out Vector2 localCursor))
        {
            return;
        }
        localCursor -= clickRect.rect.position;
        float normalizedProgress = Mathf.Clamp01((localCursor - _dragOffset)[axis] / clickRect.rect.size[axis]);
        normalizedValue = ReverseValue ? 1f - normalizedProgress : normalizedProgress;
    }

    //在数值轴没有显式邻居时，方向键按步长调整当前值。
    public override void OnMove(AxisEventData data)
    {
        bool horizontal = (int)direction < 2;
        bool sameAxis = horizontal ? data.moveDir == MoveDirection.Left || data.moveDir == MoveDirection.Right :
            data.moveDir == MoveDirection.Up || data.moveDir == MoveDirection.Down;
        if (!IsInteractable() || !sameAxis || NavigationTarget(data.moveDir) != null) { base.OnMove(data); return; }
        bool positive = data.moveDir == MoveDirection.Right || data.moveDir == MoveDirection.Up;
        float step = wholeNumbers ? 1f : (maxValue - minValue) * 0.1f;
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
