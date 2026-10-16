using System.Collections.Generic;
using RimUnity;
using RimUnity.UI;
using RimUnity.Events;
using RimUnity.EventSystems;

namespace RimUnity.UI;

//使用游戏菜单呈现选项，并把结果回写到原 TMP 标签和事件。
public sealed class TMP_Dropdown : Selectable, IPointerClickHandler, ISubmitHandler, ICancelHandler
{
    [System.Serializable] public sealed class OptionData
    {
        public string text;
        public Sprite image;
        public OptionData() { }
        public OptionData(string text) { this.text = text; }
    }
    public List<OptionData> options = new();
    public TextMeshProUGUI captionText;
    public Image captionImage;
    public UnityEvent<int> onValueChanged = new();
    private int selected;
    private Verse.FloatMenu activeMenu;
    public bool IsExpanded => activeMenu != null && Verse.Find.WindowStack.Windows.Contains(activeMenu);
    //关闭菜单并释放窗口引用。
    public void Hide() { activeMenu?.Close(); activeMenu = null; }
    public int value
    {
        get => selected;
        set
        {
            int previous = selected;
            SetValueWithoutNotify(value);
            if (selected != previous) onValueChanged.Invoke(selected);
        }
    }
    //外部同步选项时不触发回写事件。
    public void SetValueWithoutNotify(int value) { selected = options.Count == 0 ? 0 : System.Math.Clamp(value, 0, options.Count - 1); RefreshShownValue(); }
    //选项集合可由业务直接替换，刷新时同步有效下标。
    public void RefreshShownValue()
    {
        selected = options.Count == 0 ? 0 : System.Math.Clamp(selected, 0, options.Count - 1);
        if (captionText != null) captionText.text = options.Count > 0 ? options[selected].text : "";
        if (captionImage != null) captionImage.sprite = options.Count > 0 ? options[selected].image : null;
    }
    //清空选项并恢复空白标题。
    public void ClearOptions() { options.Clear(); selected = 0; RefreshShownValue(); }
    //追加文本选项。
    public void AddOptions(List<string> values) { foreach (string text in values) options.Add(new OptionData(text)); RefreshShownValue(); }
    //追加带图片的选项。
    public void AddOptions(List<OptionData> values) { options.AddRange(values); RefreshShownValue(); }
    //左键与键盘提交共用菜单入口。
    public void OnPointerClick(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left) Show();
    }
    //只为可交互且包含选项的控件创建菜单。
    public void Show()
    {
        if (!IsInteractable() || options.Count == 0 || IsExpanded) return;
        var menu = new List<Verse.FloatMenuOption>();
        for (int i = 0; i < options.Count; i++) { int index = i; menu.Add(new Verse.FloatMenuOption(options[i].text, () => value = index)); }
        Hide(); activeMenu = new Verse.FloatMenu(menu);
        Verse.Find.WindowStack.Add(activeMenu);
    }
    //提交当前焦点控件。
    public void OnSubmit(BaseEventData data) => Show();
    //取消展开的选项。
    public void OnCancel(BaseEventData data) => Hide();
    //关闭界面时同步关闭浮动菜单。
    protected override void OnDisable()
    {
        Hide();
        base.OnDisable();
    }
}
