
namespace RimUnity.EventSystems;

//为普通按钮与拖动脚本保留常用的指针事件字段。
public class PointerEventData : BaseEventData
{
    public enum InputButton { Left, Right, Middle }
    public Vector2 position;
    public Vector2 delta;
    public Vector2 pressPosition;
    public Vector2 scrollDelta;
    public InputButton button;
    public bool dragging;
    public int clickCount;
    public readonly System.Collections.Generic.List<GameObject> hovered = new();
    public bool useDragThreshold = true;
    public GameObject pointerDrag, pointerPress, rawPointerPress;
    public Camera pressEventCamera => null;
    public RaycastResult pointerPressRaycast;

    //指针和导航事件共享处理标记与焦点访问。
    public PointerEventData(EventSystem system = null) : base(system) { }
}

//记录原始按下的图形节点，供遮罩背景判断点击来源。
public struct RaycastResult { public GameObject gameObject; }

//指针与拖动事件回调接口。
public interface IPointerDownHandler { void OnPointerDown(PointerEventData eventData); }
public interface IPointerUpHandler { void OnPointerUp(PointerEventData eventData); }
public interface IPointerClickHandler { void OnPointerClick(PointerEventData eventData); }
public interface IPointerEnterHandler { void OnPointerEnter(PointerEventData eventData); }
public interface IPointerExitHandler { void OnPointerExit(PointerEventData eventData); }
public interface IBeginDragHandler { void OnBeginDrag(PointerEventData eventData); }
public interface IDragHandler { void OnDrag(PointerEventData eventData); }
public interface IEndDragHandler { void OnEndDrag(PointerEventData eventData); }
public interface IScrollHandler { void OnScroll(PointerEventData eventData); }
public interface IInitializePotentialDragHandler { void OnInitializePotentialDrag(PointerEventData eventData); }
public interface IDropHandler { void OnDrop(PointerEventData eventData); }
