using RimUnity.EventSystems;
namespace RimUnity.UI;

//共享控件过渡与指针交互状态，对齐 UGUI 1.0.0 Selectable 事件接口与状态优先级。
public abstract partial class Selectable : MonoBehaviour,
    IMoveHandler,
    IPointerDownHandler, IPointerUpHandler,
    IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler
{
    public enum Transition { None, ColorTint, SpriteSwap, Animation }
    protected enum SelectionState { Normal, Highlighted, Pressed, Selected, Disabled }

    public Transition transition { get; set; } = Transition.ColorTint;
    public bool interactable { get; set; } = true;
    public RimUnity.UI.Graphic targetGraphic { get; set; }
    public Image image { get => targetGraphic as Image; set => targetGraphic = value; }
    protected bool isPointerInside { get; set; }
    protected bool isPointerDown { get; set; }
    protected bool hasSelection { get; set; }
    internal bool Hovered { get => isPointerInside; set => isPointerInside = value; }
    internal bool Held { get => isPointerDown; set => isPointerDown = value; }
    private bool selected { get => hasSelection; set => hasSelection = value; }
    private float submitUntil;
    private bool Pressed => isPointerDown || Time.unscaledTime < submitUntil;
    public Navigation navigation { get; set; } = Navigation.defaultNavigation;
    internal Sprite HighlightedSprite = null, PressedSprite = null, DisabledSprite = null;
    private Color previousTint = Color.white, nextTint = Color.white;
    private float tintStarted;
    public ColorBlock colors { get; set; } = new()
    {
        normalColor = Color.white, highlightedColor = Color.white, pressedColor = Color.gray,
        selectedColor = Color.white, disabledColor = Color.gray, colorMultiplier = 1f, fadeDuration = 0.1f
    };

    protected virtual void Awake()
    {
        if (targetGraphic == null) targetGraphic = GetComponent<Graphic>();
    }

    public bool IsActive() => isActiveAndEnabled;

    //CanvasGroup 的交互规则与输入命中使用相同的父级范围。
    public virtual bool IsInteractable()
    {
        if (!isActiveAndEnabled || !interactable) return false;
        for (Transform t = transform; t != null; t = t.parent)
        {
            CanvasGroup group = t.GetComponent<CanvasGroup>();
            if (group == null || !group.enabled) continue;
            if (!group.interactable) return false;
            if (group.ignoreParentGroups) break;
        }
        return true;
    }

    protected SelectionState currentSelectionState
    {
        get
        {
            if (!IsInteractable()) return SelectionState.Disabled;
            if (Pressed) return SelectionState.Pressed;
            if (hasSelection) return SelectionState.Selected;
            if (isPointerInside) return SelectionState.Highlighted;
            return SelectionState.Normal;
        }
    }

    protected bool IsHighlighted() => IsActive() && IsInteractable() && isPointerInside && !isPointerDown && !hasSelection;
    protected bool IsPressed() => IsActive() && IsInteractable() && isPointerDown;

    //绘制状态色不覆盖脚本设置的 Graphic.color。
    internal Color Tint
    {
        get
        {
            if (transition != Transition.ColorTint) return Color.white;
            Color desired = (currentSelectionState switch
            {
                SelectionState.Disabled => colors.disabledColor,
                SelectionState.Pressed => colors.pressedColor,
                SelectionState.Selected => colors.selectedColor,
                SelectionState.Highlighted => colors.highlightedColor,
                _ => colors.normalColor
            }) * colors.colorMultiplier;
            float progress = colors.fadeDuration <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - tintStarted) / colors.fadeDuration);
            Color current = Color.Lerp(previousTint, nextTint, progress);
            if (desired != nextTint) { previousTint = current; nextTint = desired; tintStarted = Time.unscaledTime; }
            return colors.fadeDuration <= 0 ? desired : current;
        }
    }
    internal Sprite StateSprite => transition != Transition.SpriteSwap ? null : currentSelectionState switch
    {
        SelectionState.Disabled => DisabledSprite,
        SelectionState.Pressed => PressedSprite,
        SelectionState.Selected => spriteState.selectedSprite,
        SelectionState.Highlighted => HighlightedSprite,
        _ => null
    };

    public SpriteState spriteState
    {
        get => new() { highlightedSprite = HighlightedSprite, pressedSprite = PressedSprite, disabledSprite = DisabledSprite, selectedSprite = selectedSprite };
        set { HighlightedSprite = value.highlightedSprite; PressedSprite = value.pressedSprite; DisabledSprite = value.disabledSprite; selectedSprite = value.selectedSprite; }
    }
    private Sprite selectedSprite;
    //提交反馈持续一个颜色过渡周期。
    internal void SubmitFeedback() => submitUntil = Time.unscaledTime + colors.fadeDuration;

    public virtual void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (IsInteractable() && navigation.mode != Navigation.Mode.None && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(gameObject, eventData);
        isPointerDown = true;
    }

    public virtual void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        isPointerDown = false;
    }

    public virtual void OnPointerEnter(PointerEventData eventData) => isPointerInside = true;
    public virtual void OnPointerExit(PointerEventData eventData) => isPointerInside = false;

    //焦点获得与丢失直接驱动选中状态。
    public virtual void OnSelect(BaseEventData data) => hasSelection = true;
    //清除焦点时保留鼠标悬停状态。
    public virtual void OnDeselect(BaseEventData data) => hasSelection = false;
}

//保存精灵切换的各交互状态。
public struct SpriteState
{
    public Sprite highlightedSprite, pressedSprite, selectedSprite, disabledSprite;
}

//保存按钮各交互状态对应的颜色。
public struct ColorBlock
{
    public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor;
    public float colorMultiplier, fadeDuration;
}
