using RimUnity.Events;
using RimUnity.EventSystems;

namespace RimUnity.UI;

//对齐 UGUI 1.0.0 Button：通过 IPointerClickHandler 与 ISubmitHandler 触发 onClick。
public class Button : Selectable, IPointerClickHandler, ISubmitHandler
{
    public UnityEvent onClick { get; } = new();

    public virtual void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }
        _Press();
    }

    //键盘提交与有效点击使用同一个业务事件。
    public virtual void OnSubmit(BaseEventData data)
    {
        _Press();
        if (!IsActive() || !IsInteractable())
        {
            return;
        }
        SubmitFeedback();
        data.Use();
    }

    private void _Press()
    {
        if (!IsActive() || !IsInteractable())
        {
            return;
        }
        onClick.Invoke();
    }
}
