using System;
using RimUnity.Events;
using RimUnity.EventSystems;
using UEvent = UnityEngine.Event;
using UEventType = UnityEngine.EventType;
using KeyCode = UnityEngine.KeyCode;

namespace RimUnity.UI;

//标准单行 TMP 编辑器，保留原 SDF 文本并通过事件系统管理焦点与输入。
public sealed partial class TMP_InputField : Selectable, IPointerDownHandler, IPointerClickHandler, IDragHandler, IUpdateSelectedHandler
{
    public enum CharacterValidation { None = 0, Integer = 2, Decimal = 3 }
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
    private bool firstClick;
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
        if (isFocused) OnDeselect(new BaseEventData(EventSystem.current));
        base.OnDisable();
    }
    public void ActivateInputField() { if (IsInteractable()) EventSystem.current?.SetSelectedGameObject(gameObject); }
    public void DeactivateInputField() { if (EventSystem.current?.currentSelectedGameObject == gameObject) EventSystem.current.SetSelectedGameObject(null); }
    public override void OnSelect(BaseEventData data)
    {
        base.OnSelect(data); isFocused = true; wasCanceled = false; original = text; firstClick = true;
        caret = text.Length; anchor = onFocusSelectAll ? 0 : caret;
        Refresh(); onSelect.Invoke(text);
    }
    public override void OnDeselect(BaseEventData data)
    {
        base.OnDeselect(data); isFocused = false;
        if (anchor != caret) onEndTextSelection.Invoke(text, anchor, caret);
        if (resetOnDeActivation) { caret = anchor = 0; ScrollOffset = 0; }
        Refresh(); onEndEdit.Invoke(text); onDeselect.Invoke(text);
    }
    //点击由 EventSystem 先赋予焦点，首击保留全选，随后点击定位光标。
    public void OnPointerDown(PointerEventData data)
    {
        if (!IsInteractable() || data.button != PointerEventData.InputButton.Left) return;
        if (!(firstClick && onFocusSelectAll)) caret = anchor = Hit(data.position);
        firstClick = false;
        blinkStart = Time.unscaledTime;
    }
    public void OnPointerClick(PointerEventData data) { }
    public void OnDrag(PointerEventData data) { if (isFocused) { caret = Hit(data.position); Refresh(); } }

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
        else if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) { onSubmit.Invoke(text); DeactivateInputField(); }
        else if (ev.keyCode == KeyCode.Escape) { wasCanceled = true; if (restoreOriginalTextOnEscape) text = original; DeactivateInputField(); }
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
        else if (ev.keyCode == KeyCode.Tab) { DeactivateInputField(); return; }
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
                bool sign = character == '-' && position == 0 && !candidate.Contains('-');
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
        if (textComponent != null) { textComponent.text = text; textComponent.richText = richText && !isFocused; textComponent.enableWordWrapping = false; }
        if (placeholder != null) placeholder.enabled = text.Length == 0;
        blinkStart = Time.unscaledTime;
        if (isFocused && anchor != caret) onTextSelection.Invoke(text, anchor, caret);
    }
}
