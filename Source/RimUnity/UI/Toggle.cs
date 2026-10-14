using RimUnity.EventSystems;
using RimUnity.Events;

namespace RimUnity.UI;

//复选框状态、图形和事件由同一次状态修改保持一致。
public class Toggle : Selectable, IPointerClickHandler, ISubmitHandler
{
    public enum ToggleTransition { None, Fade }
    public ToggleTransition toggleTransition { get; set; }
    public RimUnity.UI.Graphic graphic { get; set; }
    private ToggleGroup currentGroup;
    public ToggleGroup group
    {
        get => currentGroup;
        set
        {
            if (currentGroup == value) return;
            currentGroup?.Unregister(this); currentGroup = value;
            if (isActiveAndEnabled) currentGroup?.Register(this);
        }
    }
    public UnityEvent<bool> onValueChanged { get; } = new();
    private bool value = true;
    private float previousAlpha = 1, nextAlpha = 1, fadeStarted;
    internal float GraphicAlpha => toggleTransition == ToggleTransition.None ? (value ? 1 : 0) : Mathf.Lerp(previousAlpha, nextAlpha, Mathf.Clamp01((Time.unscaledTime - fadeStarted) / 0.1f));
    public bool isOn { get => value; set => Set(value, true); }

    //程序批量设置时可以显式跳过监听器。
    public void SetIsOnWithoutNotify(bool next) => Set(next, false);

    internal void Set(bool next, bool notify)
    {
        if (value == next) { Refresh(); return; }
        if (!next && group != null && !group.CanDisable(this)) return;
        float alpha = GraphicAlpha;
        value = next;
        previousAlpha = alpha; nextAlpha = value ? 1 : 0; fadeStarted = Time.unscaledTime;
        if (next && group != null) group.Activate(this, notify);
        Refresh();
        if (notify) onValueChanged.Invoke(value);
    }

    //透明度在提交时乘入顶点，不改写作者设置的 Graphic.color。
    private void Refresh() { nextAlpha = value ? 1 : 0; }
    protected override void OnEnable() { base.OnEnable(); group?.Register(this); previousAlpha = nextAlpha = value ? 1 : 0; Refresh(); }
    protected override void OnDisable() { group?.Unregister(this); base.OnDisable(); }
    public void OnPointerClick(PointerEventData data)
    { if (data.button == PointerEventData.InputButton.Left && IsInteractable()) isOn = !isOn; }
    //提交按键与点击一样切换勾选状态。
    public void OnSubmit(BaseEventData data) { if (IsInteractable()) { isOn = !isOn; data.Use(); } }
}
