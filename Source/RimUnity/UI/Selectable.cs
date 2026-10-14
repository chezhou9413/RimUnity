using RimUnity.EventSystems;
namespace RimUnity.UI;

//共享控件过渡枚举，保留业务代码中的状态设置入口。
public abstract partial class Selectable : MonoBehaviour, IMoveHandler, ISelectHandler, IDeselectHandler
{
    public enum Transition { None, ColorTint, SpriteSwap, Animation }
    public Transition transition { get; set; } = Transition.ColorTint;
    public bool interactable { get; set; } = true;
    public RimUnity.UI.Graphic targetGraphic { get; set; }
    public Image image { get => targetGraphic as Image; set => targetGraphic = value; }
    internal bool Hovered, Held;
    private bool selected;
    private float submitUntil;
    private bool Pressed => Held || Time.unscaledTime < submitUntil;
    public Navigation navigation { get; set; } = Navigation.defaultNavigation;
    internal Sprite HighlightedSprite = null, PressedSprite = null, DisabledSprite = null;
    private Color previousTint = Color.white, nextTint = Color.white;
    private float tintStarted;
    public ColorBlock colors { get; set; } = new()
    {
        normalColor = Color.white, highlightedColor = Color.white, pressedColor = Color.gray,
        selectedColor = Color.white, disabledColor = Color.gray, colorMultiplier = 1f, fadeDuration = 0.1f
    };
    //CanvasGroup 的交互规则与输入命中使用相同的父级范围。
    public bool IsInteractable()
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

    //绘制状态色不覆盖脚本设置的 Graphic.color。
    internal Color Tint
    {
        get
        {
            if (transition != Transition.ColorTint) return Color.white;
            Color desired = (!IsInteractable() ? colors.disabledColor : Pressed ? colors.pressedColor : selected ? colors.selectedColor : Hovered ? colors.highlightedColor : colors.normalColor) * colors.colorMultiplier;
            float progress = colors.fadeDuration <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - tintStarted) / colors.fadeDuration);
            Color current = Color.Lerp(previousTint, nextTint, progress);
            if (desired != nextTint) { previousTint = current; nextTint = desired; tintStarted = Time.unscaledTime; }
            return colors.fadeDuration <= 0 ? desired : current;
        }
    }
    internal Sprite StateSprite => transition != Transition.SpriteSwap ? null :
        !IsInteractable() ? DisabledSprite : Pressed ? PressedSprite : selected ? spriteState.selectedSprite : Hovered ? HighlightedSprite : null;

    public SpriteState spriteState
    {
        get => new() { highlightedSprite = HighlightedSprite, pressedSprite = PressedSprite, disabledSprite = DisabledSprite, selectedSprite = selectedSprite };
        set { HighlightedSprite = value.highlightedSprite; PressedSprite = value.pressedSprite; DisabledSprite = value.disabledSprite; selectedSprite = value.selectedSprite; }
    }
    private Sprite selectedSprite;
    //提交反馈持续一个颜色过渡周期。
    internal void SubmitFeedback() => submitUntil = Time.unscaledTime + colors.fadeDuration;
    //焦点获得与丢失直接驱动选中状态。
    public virtual void OnSelect(BaseEventData data) => selected = true;
    //清除焦点时保留鼠标悬停状态。
    public virtual void OnDeselect(BaseEventData data) => selected = false;
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
