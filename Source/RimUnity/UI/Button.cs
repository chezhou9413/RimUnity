using RimUnity.Events;
using RimUnity.EventSystems;

namespace RimUnity.UI;

//有效左键点击完成后由指针调度器调用事件。
public class Button : Selectable, ISubmitHandler
{
    public UnityEvent onClick { get; } = new();
    //键盘提交与有效点击使用同一个业务事件。
    public void OnSubmit(BaseEventData data)
    {
        if (!IsInteractable()) return;
        SubmitFeedback(); onClick.Invoke(); data.Use();
    }
}
