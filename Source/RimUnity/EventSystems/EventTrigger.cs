using System.Collections.Generic;
using RimUnity.Events;
namespace RimUnity.EventSystems;

public enum EventTriggerType { PointerEnter, PointerExit, PointerDown, PointerUp, PointerClick, Drag, Drop, Scroll, UpdateSelected, Select, Deselect, Move, InitializePotentialDrag, BeginDrag, EndDrag, Submit, Cancel }

//分发业务代码登记的指针、拖动和焦点回调。
public class EventTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler,
    IEndDragHandler, IScrollHandler, IMoveHandler, ISubmitHandler, ICancelHandler,
    ISelectHandler, IDeselectHandler, IUpdateSelectedHandler, IInitializePotentialDragHandler, IDropHandler
{
    public class Entry
    {
        public EventTriggerType eventID;
        public UnityEvent<BaseEventData> callback = new();
    }
    public List<Entry> triggers { get; set; } = new();

    //同类事件允许注册多个监听器。
    private void Execute(EventTriggerType type, BaseEventData data)
    {
        foreach (Entry entry in triggers)
            if (entry.eventID == type) entry.callback.Invoke(data);
    }
    //转发进入事件。
    public void OnPointerEnter(PointerEventData data) => Execute(EventTriggerType.PointerEnter, data);
    //转发离开事件。
    public void OnPointerExit(PointerEventData data) => Execute(EventTriggerType.PointerExit, data);
    //转发按下事件。
    public void OnPointerDown(PointerEventData data) => Execute(EventTriggerType.PointerDown, data);
    //转发抬起事件。
    public void OnPointerUp(PointerEventData data) => Execute(EventTriggerType.PointerUp, data);
    //转发点击事件。
    public void OnPointerClick(PointerEventData data) => Execute(EventTriggerType.PointerClick, data);
    //转发开始拖动事件。
    public void OnBeginDrag(PointerEventData data) => Execute(EventTriggerType.BeginDrag, data);
    //转发拖动事件。
    public void OnDrag(PointerEventData data) => Execute(EventTriggerType.Drag, data);
    //转发结束拖动事件。
    public void OnEndDrag(PointerEventData data) => Execute(EventTriggerType.EndDrag, data);
    //转发滚轮事件。
    public void OnScroll(PointerEventData data) => Execute(EventTriggerType.Scroll, data);
    //转发方向导航事件。
    public void OnMove(AxisEventData data) => Execute(EventTriggerType.Move, data);
    //转发提交事件。
    public void OnSubmit(BaseEventData data) => Execute(EventTriggerType.Submit, data);
    //转发取消事件。
    public void OnCancel(BaseEventData data) => Execute(EventTriggerType.Cancel, data);
    //转发获得焦点事件。
    public void OnSelect(BaseEventData data) => Execute(EventTriggerType.Select, data);
    //转发失去焦点事件。
    public void OnDeselect(BaseEventData data) => Execute(EventTriggerType.Deselect, data);
    //转发焦点对象输入更新事件。
    public void OnUpdateSelected(BaseEventData data) => Execute(EventTriggerType.UpdateSelected, data);
    //转发拖动阈值初始化事件。
    public void OnInitializePotentialDrag(PointerEventData data) => Execute(EventTriggerType.InitializePotentialDrag, data);
    //转发拖动物体落在当前节点上的事件。
    public void OnDrop(PointerEventData data) => Execute(EventTriggerType.Drop, data);
}
