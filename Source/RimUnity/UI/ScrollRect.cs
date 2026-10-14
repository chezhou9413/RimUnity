using RimUnity.EventSystems;
using RimUnity.Events;

namespace RimUnity.UI;

//处理拖动、滚轮、惯性和弹性边界，并与滚动条双向同步。
public partial class ScrollRect : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, RimUnity.UI.ILayoutGroup
{
    public enum MovementType { Unrestricted, Elastic, Clamped }
    public RectTransform content, viewport;
    public bool horizontal = true, vertical = true, inertia = true;
    public MovementType movementType = MovementType.Elastic;
    public float scrollSensitivity = 1f, decelerationRate = 0.135f, elasticity = 0.1f;
    public UnityEvent<Vector2> onValueChanged { get; } = new();
    public Vector2 velocity { get; set; }
    private Scrollbar horizontalBar, verticalBar;
    private bool dragging, hasPrevious;
    private Vector2 startPointer, startContent, previousPosition, lastValue, lastViewSize, lastContentSize;

    public Scrollbar horizontalScrollbar
    {
        get => horizontalBar;
        set
        {
            if (horizontalBar == value) return;
            if (horizontalBar != null) horizontalBar.onValueChanged.RemoveListener(SetHorizontal);
            horizontalBar = value;
            if (isActiveAndEnabled && horizontalBar != null) horizontalBar.onValueChanged.AddListener(SetHorizontal);
            NotifyPropertyChanged();
        }
    }
    public Scrollbar verticalScrollbar
    {
        get => verticalBar;
        set
        {
            if (verticalBar == value) return;
            if (verticalBar != null) verticalBar.onValueChanged.RemoveListener(SetVertical);
            verticalBar = value;
            if (isActiveAndEnabled && verticalBar != null) verticalBar.onValueChanged.AddListener(SetVertical);
            NotifyPropertyChanged();
        }
    }
    public Vector2 normalizedPosition
    {
        get { UpdateBounds(); return Normalized(); }
        set { SetNormalized(value.x, 0); SetNormalized(value.y, 1); }
    }
    public float horizontalNormalizedPosition { get => normalizedPosition.x; set => SetNormalized(value, 0); }
    public float verticalNormalizedPosition { get => normalizedPosition.y; set => SetNormalized(value, 1); }

    //激活时接入滚动条，实例间不共享监听器。
    private void OnEnable()
    {
        if (horizontalBar != null) { horizontalBar.onValueChanged.RemoveListener(SetHorizontal); horizontalBar.onValueChanged.AddListener(SetHorizontal); }
        if (verticalBar != null) { verticalBar.onValueChanged.RemoveListener(SetVertical); verticalBar.onValueChanged.AddListener(SetVertical); }
        hasPrevious = false;
    }
    //停用时解除监听并停止惯性。
    private void OnDisable()
    {
        if (horizontalBar != null) horizontalBar.onValueChanged.RemoveListener(SetHorizontal);
        if (verticalBar != null) verticalBar.onValueChanged.RemoveListener(SetVertical);
        dragging = false;
        StopMovement();
    }
    //滚动条只驱动对应轴。
    private void SetHorizontal(float value) => SetNormalized(value, 0);
    //滚动条只驱动对应轴。
    private void SetVertical(float value) => SetNormalized(value, 1);
    //清空惯性速度，但保留当前滚动位置。
    public void StopMovement() => velocity = Vector2.zero;

    //记录按下时的内容位置，避免弹性阻力在连续事件中重复累积。
    public void OnBeginDrag(PointerEventData e)
    {
        if (content == null || e.button != PointerEventData.InputButton.Left) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)content.parent, e.pressPosition, null, out startPointer);
        startContent = previousPosition = content.anchoredPosition;
        dragging = true;
        StopMovement();
    }
    //拖动超出边界时逐渐增加阻力。
    public void OnDrag(PointerEventData e)
    {
        if (!dragging || content == null || e.button != PointerEventData.InputButton.Left) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)content.parent, e.position, null, out Vector2 pointer);
        UpdateBounds();
        Vector2 target = startContent + pointer - startPointer;
        Vector2 offset = Offset(ToView(target - content.anchoredPosition));
        target += ToParent(offset);
        if (movementType == MovementType.Elastic)
        {
            Vector2 rubber = new(Rubber(offset.x, viewBounds.width), Rubber(offset.y, viewBounds.height));
            target -= ToParent(rubber);
        }
        SetPosition(target);
    }
    //释放拖动后由帧更新继续惯性与回弹。
    public void OnEndDrag(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) dragging = false;
    }
    //仅有单个滚动轴时，把主要滚轮分量映射到可用轴。
    public void OnScroll(PointerEventData e)
    {
        if (content == null) return;
        StopMovement();
        Vector2 delta = new(-e.scrollDelta.x, e.scrollDelta.y);
        if (vertical && !horizontal && Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) delta.y = delta.x;
        if (horizontal && !vertical && Mathf.Abs(delta.y) > Mathf.Abs(delta.x)) delta.x = delta.y;
        if (!horizontal) delta.x = 0f;
        if (!vertical) delta.y = 0f;
        UpdateBounds();
        delta *= scrollSensitivity;
        if (movementType == MovementType.Clamped) delta += Offset(delta);
        SetPosition(content.anchoredPosition + ToParent(delta));
        Synchronize();
    }

    //弹性在关闭惯性时也会回到边界；速度按未缩放时间衰减。
    private void LateUpdate()
    {
        if (content == null) return;
        float dt = Time.unscaledDeltaTime;
        UpdateBounds();
        Vector2 position = content.anchoredPosition;
        Vector2 speed = velocity;
        if (!dragging && dt > 0f)
        {
            Vector2 correction = ToParent(Offset(Vector2.zero));
            for (int axis = 0; axis < 2; axis++)
            {
                if (axis == 0 ? !horizontal : !vertical) { speed[axis] = 0; continue; }
                if (movementType == MovementType.Elastic && Mathf.Abs(correction[axis]) > 0.001f)
                {
                    float axisSpeed = speed[axis];
                    position[axis] = Mathf.SmoothDamp(position[axis], position[axis] + correction[axis],
                        ref axisSpeed, elasticity, float.PositiveInfinity, dt);
                    speed[axis] = Mathf.Abs(axisSpeed) < 1f ? 0f : axisSpeed;
                }
                else if (inertia)
                {
                    speed[axis] *= Mathf.Pow(decelerationRate, dt);
                    if (Mathf.Abs(speed[axis]) < 1f) speed[axis] = 0f;
                    position[axis] += speed[axis] * dt;
                }
                else speed[axis] = 0f;
            }
            if (movementType == MovementType.Clamped)
                position += ToParent(Offset(ToView(position - content.anchoredPosition)));
            SetPosition(position);
        }
        else if (dragging && inertia && dt > 0f)
            speed = Vector2.Lerp(speed, (position - previousPosition) / dt, dt * 10f);
        velocity = speed;
        previousPosition = content.anchoredPosition;
        Synchronize();
    }

    //只修改允许滚动的轴，确保禁用方向保持作者设置。
    private void SetPosition(Vector2 position)
    {
        if (!horizontal) position.x = content.anchoredPosition.x;
        if (!vertical) position.y = content.anchoredPosition.y;
        content.anchoredPosition = position;
    }

    //公开归一化接口允许显式设置任意轴，并停止该轴惯性。
    private void SetNormalized(float value, int axis)
    {
        if (content == null) return;
        UpdateBounds();
        float hidden = contentBounds.size[axis] - viewBounds.size[axis];
        Vector2 delta = Vector2.zero;
        delta[axis] = viewBounds.min[axis] - value * hidden - contentBounds.min[axis];
        content.anchoredPosition += ToParent(delta);
        Vector2 speed = velocity; speed[axis] = 0f; velocity = speed;
        Synchronize();
    }

    //尺寸变化和位置变化均通知滚动条及业务监听器。
    private void Synchronize()
    {
        if (content == null) return;
        UpdateBounds();
        Vector2 value = Normalized();
        UpdateScrollbarVisibility();
        Vector2 offset = Offset(Vector2.zero);
        if (horizontalBar != null)
        {
            horizontalBar.size = contentBounds.width > 0 ? Mathf.Clamp01((viewBounds.width - Mathf.Abs(offset.x)) / contentBounds.width) : 1f;
            horizontalBar.SetValueWithoutNotify(value.x);
        }
        if (verticalBar != null)
        {
            verticalBar.size = contentBounds.height > 0 ? Mathf.Clamp01((viewBounds.height - Mathf.Abs(offset.y)) / contentBounds.height) : 1f;
            verticalBar.SetValueWithoutNotify(value.y);
        }
        bool changed = !hasPrevious || value != lastValue || viewBounds.size != lastViewSize || contentBounds.size != lastContentSize;
        lastValue = value; lastViewSize = viewBounds.size; lastContentSize = contentBounds.size; hasPrevious = true;
        if (changed) onValueChanged.Invoke(value);
    }

    //越界距离越大，额外拖动产生的位移越小。
    private static float Rubber(float distance, float size)
        => size <= 0f ? 0f : (1f - 1f / (Mathf.Abs(distance) * 0.55f / size + 1f)) * size * Mathf.Sign(distance);
}
