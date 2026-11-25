namespace RimUnity.EventSystems;

//事件系统处理器标记接口，与 UGUI ExecuteEvents 保持一致。
public interface IEventSystemHandler { }

//保存焦点事件的处理标记和所属事件系统。
public class BaseEventData
{
    public EventSystem eventSystem { get; }
    public bool used { get; private set; }
    public GameObject selectedObject
    {
        get => eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        set
        {
            if (eventSystem != null) eventSystem.SetSelectedGameObject(value, this);
        }
    }
    //未显式指定时使用当前事件系统。
    public BaseEventData(EventSystem system = null) => eventSystem = system != null ? system : EventSystem.current;
    //停止后续默认输入处理。
    public void Use() => used = true;
    //允许复用事件对象。
    public void Reset() => used = false;
}

public enum MoveDirection { Left, Up, Right, Down, None }

//保存方向键导航的方向和轴向量。
public class AxisEventData : BaseEventData
{
    public MoveDirection moveDir { get; set; } = MoveDirection.None;
    public Vector2 moveVector { get; set; }
    //保持与普通焦点事件相同的事件系统。
    public AxisEventData(EventSystem system = null) : base(system) { }
}

public interface IMoveHandler : IEventSystemHandler { void OnMove(AxisEventData eventData); }
public interface ISubmitHandler : IEventSystemHandler { void OnSubmit(BaseEventData eventData); }
public interface ICancelHandler : IEventSystemHandler { void OnCancel(BaseEventData eventData); }
public interface ISelectHandler : IEventSystemHandler { void OnSelect(BaseEventData eventData); }
public interface IDeselectHandler : IEventSystemHandler { void OnDeselect(BaseEventData eventData); }
public interface IUpdateSelectedHandler : IEventSystemHandler { void OnUpdateSelected(BaseEventData eventData); }
