using RimUnity.EventSystems;
using RimUnity.Events;

namespace RimUnity.UI;

//复选框状态、图形和事件由同一次状态修改保持一致，对齐 UGUI 1.0.0 Toggle。
public class Toggle : Selectable, IPointerClickHandler, ISubmitHandler
{
    private const float FADE_DURATION = 0.1f;

    private ToggleGroup currentGroup;
    private bool _isOnValue;
    private float _previousAlpha;
    private float _nextAlpha;
    private float _fadeStartedTime;

    public enum ToggleTransition { None, Fade }

    public ToggleTransition toggleTransition { get; set; } = ToggleTransition.Fade;
    public RimUnity.UI.Graphic graphic { get; set; }
    public ToggleGroup group
    {
        get => currentGroup;
        set
        {
            _SetToggleGroup(value, true);
            _PlayEffect(true);
        }
    }
    public UnityEvent<bool> onValueChanged { get; } = new();
    public bool isOn { get => _isOnValue; set => Set(value, true); }

    internal float GraphicAlpha => toggleTransition == ToggleTransition.None
        ? (_isOnValue ? 1f : 0f)
        : Mathf.Lerp(_previousAlpha, _nextAlpha, Mathf.Clamp01((Time.unscaledTime - _fadeStartedTime) / FADE_DURATION));

    protected override void OnEnable()
    {
        base.OnEnable();
        _SetToggleGroup(currentGroup, false);
        _PlayEffect(true);
    }

    protected override void OnDisable()
    {
        _SetToggleGroup(null, false);
        base.OnDisable();
    }

    protected virtual void OnDestroy()
    {
        if (currentGroup != null)
        {
            currentGroup.EnsureValidState();
        }
    }

    //程序批量设置时可以显式跳过监听器。
    public void SetIsOnWithoutNotify(bool next) => Set(next, false);

    public virtual void OnPointerClick(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left)
        {
            return;
        }
        _InternalToggle();
    }

    //提交按键与点击一样切换勾选状态。
    public virtual void OnSubmit(BaseEventData data)
    {
        _InternalToggle();
        if (IsActive() && IsInteractable())
        {
            data.Use();
        }
    }

    internal void Set(bool next, bool notify)
    {
        if (_isOnValue == next)
        {
            return;
        }
        float currentAlpha = GraphicAlpha;
        _isOnValue = next;
        if (currentGroup != null && currentGroup.isActiveAndEnabled && IsActive())
        {
            if (_isOnValue || (!currentGroup.AnyTogglesOn() && !currentGroup.allowSwitchOff))
            {
                _isOnValue = true;
                currentGroup.NotifyToggleOn(this, notify);
            }
        }
        _previousAlpha = currentAlpha;
        _nextAlpha = _isOnValue ? 1f : 0f;
        _fadeStartedTime = Time.unscaledTime;
        if (notify)
        {
            onValueChanged.Invoke(_isOnValue);
        }
    }

    private void _SetToggleGroup(ToggleGroup newGroup, bool setMemberValue)
    {
        if (currentGroup != null)
        {
            currentGroup.UnregisterToggle(this);
        }
        if (setMemberValue)
        {
            currentGroup = newGroup;
        }
        if (newGroup != null && IsActive())
        {
            newGroup.RegisterToggle(this);
        }
        if (newGroup != null && _isOnValue && IsActive())
        {
            newGroup.NotifyToggleOn(this);
        }
    }

    private void _PlayEffect(bool instant)
    {
        if (instant)
        {
            _previousAlpha = _nextAlpha = _isOnValue ? 1f : 0f;
        }
        else
        {
            _previousAlpha = GraphicAlpha;
            _nextAlpha = _isOnValue ? 1f : 0f;
            _fadeStartedTime = Time.unscaledTime;
        }
    }

    private void _InternalToggle()
    {
        if (!IsActive() || !IsInteractable())
        {
            return;
        }
        isOn = !isOn;
    }
}
