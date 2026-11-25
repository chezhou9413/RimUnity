using System;
using RimUnity.Events;
using RimUnity.EventSystems;
using UEvent = UnityEngine.Event;
using UEventType = UnityEngine.EventType;
using KeyCode = UnityEngine.KeyCode;

namespace RimUnity.UI;

//标准单行 TMP 编辑器，保留原 SDF 文本并通过事件系统与 Waylith TextFocus / IME 同步焦点、候选窗位置与组合预览；
//RimWorld 1.7.5014 rev1195 的 UnityStubs 已实现 Input.compositionString / Input.imeCompositionMode / Input.compositionCursorPos 以及 ImeComposition / TextFocus。
public sealed partial class TMP_InputField : Selectable, IPointerClickHandler, IDragHandler, IInitializePotentialDragHandler, IUpdateSelectedHandler
{
    //与宿主 IMGUI 控件 ID 分离的保留段，用于向 Waylith TextFocus / TextInputBridge 声明当前文本焦点。
    private const int HostControlIdMask = 0x7F000000;
    private const int HostControlIdTag = 0x52000000;
    private static TMP_InputField s_focused;

    internal int HostControlId => HostControlIdTag | (GetInstanceID() & 0x00FFFFFF);
    internal static bool IsHostControlId(int controlId) => (controlId & HostControlIdMask) == HostControlIdTag;
    internal static TMP_InputField ActiveFocused
    {
        get
        {
            if (s_focused != null && (!s_focused || !s_focused.isFocused || !s_focused.IsActive() || !s_focused.IsInteractable()))
            {
                s_focused = null;
            }
            return s_focused;
        }
    }
    internal string ActiveComposition => isFocused && !readOnly ? UnityEngine.Input.compositionString ?? "" : "";
    internal string DisplayText
    {
        get
        {
            string composition = ActiveComposition;
            if (composition.Length == 0)
            {
                return value;
            }
            int start = Math.Min(caret, anchor);
            int length = Math.Abs(caret - anchor);
            return value.Remove(start, length).Insert(start, composition);
        }
    }

    public enum CharacterValidation { None = 0, Digit = 1, Integer = 2, Decimal = 3 }
    public CharacterValidation characterValidation { get; set; }
    public RectTransform textViewport { get; set; }
    public TextMeshProUGUI textComponent { get; set; }
    public Graphic placeholder { get; set; }
    public int characterLimit { get; set; }
    public bool readOnly { get; set; }
    public bool richText { get; set; } = true;
    public bool onFocusSelectAll { get; set; } = true;
    public bool resetOnDeActivation { get; set; } = true;
    public bool restoreOriginalTextOnEscape { get; set; } = true;
    public Color caretColor { get; set; } = Color.black;
    public bool customCaretColor { get; set; }
    public Color selectionColor { get; set; } = new(.1f, .4f, .8f, .5f);
    public float caretBlinkRate { get; set; } = 1;
    public int caretWidth { get; set; } = 1;
    public UnityEvent<string> onValueChanged = new(), onEndEdit = new(), onSubmit = new(), onSelect = new(), onDeselect = new();
    public UnityEvent<string, int, int> onTextSelection = new(), onEndTextSelection = new();
    public bool isFocused { get; private set; }
    public bool wasCanceled { get; private set; }
    private string value = "", original = "";
    private int caret, anchor;
    internal float ScrollOffset;
    private float blinkStart;
    public string text { get => value; set => Set(value, true); }
    public int stringPosition { get => caret; set { caret = Math.Clamp(value, 0, text.Length); Refresh(); } }
    public int stringSelectPosition { get => anchor; set { anchor = Math.Clamp(value, 0, text.Length); Refresh(); } }

    //业务赋值可以选择不通知监听器，加载预制体时尚未注册事件。
    public void SetTextWithoutNotify(string value) => Set(value, false);
    private void Set(string next, bool notify)
    {
        next ??= "";
        next = next.Replace("\r", "").Replace("\n", "");
        if (characterLimit > 0 && next.Length > characterLimit) next = next.Substring(0, characterLimit);
        if (next == value) return;
        value = next; caret = Math.Min(caret, value.Length); anchor = Math.Min(anchor, value.Length);
        Refresh(); if (notify) onValueChanged.Invoke(value);
    }
    //加载完成后同步可视文字与占位符。
    protected override void OnEnable() { base.OnEnable(); Refresh(); }
    protected override void OnDisable()
    {
        if (isFocused)
        {
            _DeactivateInputFieldInternal(invokeDeselect: true);
        }
        base.OnDisable();
    }
    public void ActivateInputField()
    {
        if (!IsActive() || !IsInteractable())
        {
            return;
        }
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject)
        {
            EventSystem.current.SetSelectedGameObject(gameObject);
        }
        else
        {
            _FocusInternal();
        }
    }
    public void DeactivateInputField() => DeactivateInputField(false);
    public void DeactivateInputField(bool clearSelection)
    {
        bool hadFocus = isFocused;
        if (hadFocus)
        {
            _DeactivateInputFieldInternal(invokeDeselect: false);
        }
        if (clearSelection && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
        if (hadFocus)
        {
            onDeselect.Invoke(text);
        }
    }
    public override void OnSelect(BaseEventData data)
    {
        if (!IsActive() || !IsInteractable())
        {
            return;
        }
        base.OnSelect(data);
        _FocusInternal();
    }
    public override void OnDeselect(BaseEventData data)
    {
        bool hadFocus = isFocused;
        _DeactivateInputFieldInternal(invokeDeselect: false);
        base.OnDeselect(data);
        if (hadFocus)
        {
            onDeselect.Invoke(text);
        }
    }
    public void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;
    //对齐 TMP 3.0.6：先记录点击前是否已聚焦，首次点击保留全选，已聚焦后再点击定位光标并刷新选区。
    public override void OnPointerDown(PointerEventData data)
    {
        if (!IsActive() || !IsInteractable() || data.button != PointerEventData.InputButton.Left)
        {
            return;
        }
        bool hadFocusBefore = isFocused;
        base.OnPointerDown(data);
        if (!isFocused)
        {
            _FocusInternal();
        }
        if (hadFocusBefore || !onFocusSelectAll)
        {
            int previousCaret = caret;
            int previousAnchor = anchor;
            caret = anchor = Hit(data.position, data.pressEventCamera);
            if (previousAnchor != previousCaret)
            {
                onEndTextSelection.Invoke(text, previousAnchor, previousCaret);
            }
            Refresh();
        }
        blinkStart = Time.unscaledTime;
    }
    public void OnPointerClick(PointerEventData data) { }
    public void OnDrag(PointerEventData data)
    {
        if (!isFocused || !IsActive() || !IsInteractable() || data.button != PointerEventData.InputButton.Left)
        {
            return;
        }
        caret = Hit(data.position, data.pressEventCamera);
        Refresh();
    }

    private void _FocusInternal()
    {
        if (isFocused || !IsActive() || !IsInteractable())
        {
            return;
        }
        if (s_focused != null && s_focused != this)
        {
            s_focused._DeactivateInputFieldInternal(invokeDeselect: true);
        }
        isFocused = true;
        s_focused = this;
        UnityEngine.GUIUtility.keyboardControl = readOnly ? 0 : HostControlId;
        wasCanceled = false;
        original = text;
        caret = text.Length;
        anchor = onFocusSelectAll ? 0 : caret;
        Refresh();
        onSelect.Invoke(text);
    }

    private void _DeactivateInputFieldInternal(bool invokeDeselect)
    {
        if (!isFocused)
        {
            return;
        }
        isFocused = false;
        if (s_focused == this)
        {
            s_focused = null;
        }
        if (UnityEngine.GUIUtility.keyboardControl == HostControlId)
        {
            UnityEngine.GUIUtility.keyboardControl = 0;
        }
        if (anchor != caret)
        {
            onEndTextSelection.Invoke(text, anchor, caret);
        }
        if (resetOnDeActivation)
        {
            caret = anchor = 0;
            ScrollOffset = 0;
        }
        Refresh();
        onEndEdit.Invoke(text);
        if (invokeDeselect)
        {
            onDeselect.Invoke(text);
        }
    }

    //输入使用宿主提交的字符事件，复制粘贴保留 Unicode 字符串。
    public void OnUpdateSelected(BaseEventData data)
    {
        if (!isFocused || !IsInteractable()) return;
        UEvent ev = UEvent.current;
        if (ev.type != UEventType.KeyDown) return;
        bool control = ev.control || ev.command, shift = ev.shift;
        int start = Math.Min(caret, anchor), length = Math.Abs(caret - anchor);
        if (control && ev.keyCode == KeyCode.A) { anchor = 0; caret = text.Length; }
        else if (control && (ev.keyCode == KeyCode.C || ev.keyCode == KeyCode.X))
        {
            if (length > 0) UnityEngine.GUIUtility.systemCopyBuffer = text.Substring(start, length);
            if (ev.keyCode == KeyCode.X && !readOnly) ReplaceSelection("");
        }
        else if (control && ev.keyCode == KeyCode.V) { if (!readOnly) ReplaceSelection(UnityEngine.GUIUtility.systemCopyBuffer); }
        else if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) { onSubmit.Invoke(text); DeactivateInputField(clearSelection: true); }
        else if (ev.keyCode == KeyCode.Escape) { wasCanceled = true; if (restoreOriginalTextOnEscape) text = original; DeactivateInputField(clearSelection: true); }
        else if (ev.keyCode == KeyCode.Home || ev.keyCode == KeyCode.End) { caret = ev.keyCode == KeyCode.Home ? 0 : text.Length; if (!shift) anchor = caret; }
        else if (ev.keyCode == KeyCode.LeftArrow || ev.keyCode == KeyCode.RightArrow)
        {
            bool right = ev.keyCode == KeyCode.RightArrow;
            caret = !shift && length > 0 ? (right ? start + length : start) : Step(caret, right);
            if (control) while (caret > 0 && caret < text.Length && !char.IsWhiteSpace(text[caret - (right ? 0 : 1)])) caret = Step(caret, right);
            if (!shift) anchor = caret;
        }
        else if (ev.keyCode == KeyCode.Backspace || ev.keyCode == KeyCode.Delete)
        {
            if (!readOnly) { if (length == 0) anchor = Step(caret, ev.keyCode == KeyCode.Delete); ReplaceSelection(""); }
        }
        else if (!control && !readOnly && ev.character != '\0' && !char.IsControl(ev.character)) ReplaceSelection(ev.character.ToString());
        else if (ev.keyCode == KeyCode.Tab) { DeactivateInputField(clearSelection: true); return; }
        else { data.Use(); return; }
        Refresh(); data.Use();
    }
    //移动和删除不拆开 UTF-16 代理对。
    private int Step(int position, bool right)
    {
        int next = Math.Clamp(position + (right ? 1 : -1), 0, text.Length);
        if (next > 0 && next < text.Length && char.IsLowSurrogate(text[next]) && char.IsHighSurrogate(text[next - 1])) next += right ? 1 : -1;
        return next;
    }
    private void ReplaceSelection(string inserted)
    {
        int start = Math.Min(caret, anchor), length = Math.Abs(caret - anchor);
        inserted = (inserted ?? "").Replace("\r", "").Replace("\n", "");
        if (characterValidation != CharacterValidation.None)
        {
            string remainder = text.Remove(start, length);
            var accepted = new System.Text.StringBuilder();
            foreach (char character in inserted)
            {
                int position = start + accepted.Length;
                string candidate = remainder.Insert(start, accepted.ToString());
                bool digit = character >= '0' && character <= '9' && !(position == 0 && candidate.StartsWith("-", StringComparison.Ordinal));
                bool sign = characterValidation != CharacterValidation.Digit && character == '-' && position == 0 && !candidate.Contains('-');
                bool separator = characterValidation == CharacterValidation.Decimal && character == '.' && !candidate.Contains('.');
                if (digit || sign || separator) accepted.Append(character);
            }
            inserted = accepted.ToString();
        }
        if (characterLimit > 0) inserted = inserted.Substring(0, Math.Min(inserted.Length, Math.Max(0, characterLimit - text.Length + length)));
        text = text.Remove(start, length).Insert(start, inserted); caret = anchor = start + inserted.Length;
    }
    //编辑时按普通字符显示，失焦恢复作者指定的富文本模式。
    internal void Refresh()
    {
        string display = DisplayText;
        if (textComponent != null) { textComponent.text = display; textComponent.richText = richText && !isFocused; textComponent.enableWordWrapping = false; }
        if (placeholder != null) placeholder.enabled = display.Length == 0;
        blinkStart = Time.unscaledTime;
        if (isFocused && anchor != caret) onTextSelection.Invoke(text, anchor, caret);
    }

    //同步 IME 组合预览串到可视文本与占位符，不改写已提交的 text 值或触发 onValueChanged。
    internal void SyncComposition()
    {
        string display = DisplayText;
        if (textComponent != null && textComponent.text != display)
        {
            textComponent.text = display;
            blinkStart = Time.unscaledTime;
        }
        if (placeholder != null)
        {
            bool showPlaceholder = display.Length == 0;
            if (placeholder.enabled != showPlaceholder)
            {
                placeholder.enabled = showPlaceholder;
            }
        }
    }

    //在宿主 GUI.ProcessPendingWindows 清理未绘制 IMGUI 焦点后同步 RimUnity 输入框焦点，确保 Waylith TextFocus 与 TextInputBridge 持续激活。
    internal static void SyncHostTextFocus()
    {
        TMP_InputField focused = ActiveFocused;
        int currentControl = UnityEngine.GUIUtility.keyboardControl;
        if (focused == null || focused.readOnly)
        {
            if (IsHostControlId(currentControl))
            {
                UnityEngine.GUIUtility.keyboardControl = 0;
            }
            return;
        }
        if (currentControl == 0 || IsHostControlId(currentControl))
        {
            UnityEngine.GUIUtility.keyboardControl = focused.HostControlId;
            return;
        }
        //原版 IMGUI 文本控件主动接管了焦点，退让当前 UGUI 输入框焦点。
        focused.DeactivateInputField(clearSelection: true);
    }
}
